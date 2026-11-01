using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;

namespace CricketVR
{
    /// <summary>
    /// Runs the shared session framework (REQ-011..014, REQ-021, REQ-025..028; plan.md §16) on top of the pure
    /// <see cref="SessionStateMachine"/>: start menu, countdown, ball-by-ball, end screen, play again / menu, pause
    /// on focus or tracking loss. It owns no gameplay rules: the bowling machine bowls, ScoreManager decides each
    /// ball's single result, and this component only sequences and records.
    /// Until later milestones: Net Practice shows Hit / Miss / Bowled (Shot Confidence arrives in M8); Match
    /// Simulation bowls a placeholder number of balls on the ground with "scoring pending" (scorer and chase in M9/M10).
    /// </summary>
    public class SessionManager : MonoBehaviour
    {
        [SerializeField] GameConfig config;
        [SerializeField] BowlingMachine machine;
        [SerializeField] BallController ball;
        [SerializeField] ScoreManager score;
        [SerializeField] BatBallContactResolver resolver;
        [SerializeField] BatController bat;
        [SerializeField] HandednessSetup handedness;
        [SerializeField] EnvironmentModeSwitch environment;
        [SerializeField] BowlingDebugControls debugControls;
        [SerializeField] BatSetupControls setupControls;
        [SerializeField] SessionUI ui;
        [SerializeField] MenuPointer pointer;
        [SerializeField] BallLogWriter log;
        [SerializeField] Transform xrOrigin;
        [Tooltip("The striker's wicket reaction (cosmetic; reset before every ball and on restart, menu, pause, mode switch).")]
        [SerializeField] WicketReaction wicket;

        SessionStateMachine _session;
        System.Random _rng;
        DeliveryTypeData[] _types;
        string _sessionId = "";
        float _sessionStart;
        float _untrackedFor;
        bool _batSetup;
        bool _menuPending;

        // Menu selections
        SessionMode _mode = SessionMode.NetPractice;
        int _overs;
        string _difficulty = "Medium";
        int _netBowler; // index into NetBowler.All; 0 = pace
        int _wicketSide; // index into WicketSide.All; 0 = over the wicket

        readonly Dictionary<int, BatContactReport> _contacts = new Dictionary<int, BatContactReport>();
        string _lastResultLine = "";
        InputAction _doneTap;
        InputAction _readyPress;
        float _doneDown = -1f;

        public SessionPhase Phase => _session != null ? _session.Phase : SessionPhase.Menu;
        public SessionStateMachine Session => _session;

        void Awake()
        {
            if (config == null || machine == null || ball == null || score == null || ui == null)
            {
                Debug.LogError("[CricketVR] SessionManager is missing references; run CricketVR > Setup > 6. Build or Repair Main Scene.", this);
                enabled = false;
                return;
            }
            _session = new SessionStateMachine(config.session.timings);
            _overs = Mathf.Max(1, config.session.defaultOvers);
        }

        void OnEnable()
        {
            if (score != null) score.DeliveryFinalized += OnDeliveryFinalized;
            if (resolver != null) resolver.BatContact += OnBatContact;
            if (ui != null) ui.ButtonPressed += OnButton;
            if (setupControls != null) setupControls.FitConfirmed += ExitBatSetup;
            if (setupControls != null) setupControls.FitModeChanged += ShowFitPanel;
            _doneTap = new InputAction("BatSetupDone", InputActionType.Button, "<XRController>/{PrimaryButton}");
            _doneTap.Enable();
            // The batter asks for each ball with B (owner decision 2026-10-26: "as a batsman I want that control").
            _readyPress = new InputAction("Ready", InputActionType.Button, "<XRController>/{SecondaryButton}");
            _readyPress.AddBinding("<Keyboard>/n");
            _readyPress.Enable();
        }

        void OnDisable()
        {
            if (score != null) score.DeliveryFinalized -= OnDeliveryFinalized;
            if (resolver != null) resolver.BatContact -= OnBatContact;
            if (ui != null) ui.ButtonPressed -= OnButton;
            if (setupControls != null) setupControls.FitConfirmed -= ExitBatSetup;
            if (setupControls != null) setupControls.FitModeChanged -= ShowFitPanel;
            _doneTap?.Dispose();
            _readyPress?.Dispose();
        }

        void Start()
        {
            // The session owns bowling now: the M4 debug taps and the always-on alignment controls are off.
            if (debugControls != null) debugControls.allowDebugInput = false;
            if (setupControls != null) setupControls.allowSetupInput = false;
            // Shown on the first Update: HandednessSetup.Start may move the rig to the saved stance first.
            _menuPending = true;
        }

        void Update()
        {
            if (_session == null)
                return;
            if (_menuPending)
            {
                _menuPending = false;
                ShowMenu();
            }
            if (_batSetup)
            {
                if (bat != null && bat.Fitting)
                    _doneDown = -1f; // A moves the ghost controller; a stick click confirms the fit
                else if (Tapped(_doneTap, ref _doneDown))
                    ExitBatSetup();
                return;
            }

            if (_readyPress.WasPressedThisFrame())
                PressReady();

            if (_session.Phase == SessionPhase.Countdown || _session.Phase == SessionPhase.Delivering)
                WatchTracking();
            if (_session.Phase == SessionPhase.Countdown && wicket != null)
                wicket.ResetAfterHold(); // the next ball is called: put the wicket back once it has been seen

            var step = _session.Tick(Time.deltaTime);
            Handle(step);
            RefreshBoard();
        }

        // ---------- Session steps ----------

        void Handle(SessionStep step)
        {
            switch (step.Action)
            {
                case SessionAction.LaunchBall:
                    Launch(step.BallIndex);
                    break;
                case SessionAction.CancelBall:
                    if (ball.InFlight && ball.ActiveDeliveryId == step.DeliveryId)
                        ball.Cancel();
                    break;
                case SessionAction.SessionEnded:
                    ShowEnd();
                    break;
            }
        }

        void Launch(int ballIndex)
        {
            var plan = _mode == SessionMode.NetPractice ? NetBowler.All[_netBowler].Plan(WicketSide.All[_wicketSide].Release)
                : _difficulty == "Easy" ? DeliveryPlanSettings.Easy
                : _difficulty == "Hard" ? DeliveryPlanSettings.Hard : DeliveryPlanSettings.Medium;
            var hand = handedness != null ? handedness.Current : Handedness.Right;
            int redraws = 0;
            var delivery = WicketCheck ? DeliveryPlanner.WicketCheckRequest(ballIndex, hand, _types)
                : SpinCheck ? DeliveryPlanner.SpinCheckRequest(ballIndex, hand, _types)
                : SwingCheck ? DeliveryPlanner.SwingCheckRequest(ballIndex, hand, _types)
                : DeliveryPlanner.DrawRequest(plan, _rng, hand, _types,
                    DeliveryPlanner.RequestSolverCheck(-Physics.gravity.y, Time.fixedDeltaTime, config.delivery.ballRadius), out redraws);
            if (redraws > 0)
                Debug.LogWarning($"[CricketVR] Delivery plan {plan.label}: {redraws} unreachable draw(s) redrawn for ball {ballIndex + 1}");
            if (wicket != null) wicket.ResetWicket(); // never bowl at a broken wicket
            int id = machine.Bowl(delivery);
            _session.BallLaunched(id);
        }

        string BowlerName => NetBowler.All[_netBowler].Label.ToLowerInvariant();

        /// <summary>T076 (AC-26) build: Net Practice bowls the fixed swing-check sequence.</summary>
        bool SwingCheck => _mode == SessionMode.NetPractice && config.delivery.swingCheck;
        /// <summary>T082 (AC-27, AC-28) build: Net Practice bowls the fixed spin-check sequence.</summary>
        bool SpinCheck => _mode == SessionMode.NetPractice && config.delivery.spinCheck;
        /// <summary>T088 (AC-32/AC-33) build: both modes bowl the fixed wicket-check sequence.</summary>
        bool WicketCheck => config.delivery.wicketCheck;

        /// <summary>The delivery type and its measured swing, shown after a moving ball (nothing for Stock).</summary>
        static string MovementLine(BallRecord r)
        {
            string line = "";
            if (r.PredictedSwing > 0f)
            {
                string measured = float.IsNaN(r.MeasuredSwing) ? "not measured (hit before the crease)" : $"{r.MeasuredSwing:F2} m";
                line += $"\n{r.DeliveryType}: swing {measured}, planned {r.PredictedSwing:F2} m";
            }
            if (!float.IsNaN(r.TurnDegrees))
                line += $"\n{r.DeliveryType}{(r.Flighted ? " (flighted)" : "")}: launched {r.LaunchDegrees:+0;-0}°, turned {Mathf.Abs(r.TurnDegrees):F1}°";
            if (!float.IsNaN(r.CreaseHeight))
                line += $"\nHeight at the crease: {r.CreaseHeight:F2} m";
            return line;
        }

        void OnBatContact(BatContactReport report) => _contacts[report.DeliveryId] = report;

        void OnDeliveryFinalized(DeliveryResult result)
        {
            if (_session == null || _session.Phase != SessionPhase.Delivering || result.DeliveryId != _session.ActiveDeliveryId)
                return; // a debug launch or a stale delivery
            var record = BuildRecord(result);
            if (!_session.BallFinalized(record))
                return;
            log?.Append(record);
            _lastResultLine = $"Ball {record.Over - 1}.{record.BallInOver}: {result.Summary}" +
                              (result.BatContact ? $"\n{result.CarryLabel}" : "") + MovementLine(record);
            _contacts.Remove(result.DeliveryId);
        }

        BallRecord BuildRecord(DeliveryResult result)
        {
            var r = new BallRecord
            {
                SessionId = _sessionId,
                Mode = _mode,
                DeliveryId = result.DeliveryId,
                Time = Time.time - _sessionStart,
                Delivery = machine.LastConfig,
                Seed = _session.Options.Seed,
                DeliveryType = machine.LastRequest.type.displayName ?? "",
                Movement = machine.LastRequest.movement,
                Hand = machine.LastRequest.handedness,
                LineRef = machine.LastRequest.lineReference,
                BallSeed = machine.LastRequest.seed,
                VarSwing = machine.LastMovement.VarSwing,
                VarDrift = machine.LastMovement.VarDrift,
                VarTurn = machine.LastMovement.VarTurn,
                PredictedSwing = machine.LastMovement.IsStill ? float.NaN : machine.LastSolution.PredictedSwing,
                MeasuredSwing = machine.LastMeasuredSwing,
                PredictedCurve = machine.LastMovement.IsStill ? float.NaN : machine.LastSolution.PredictedCurve,
                MeasuredCurve = machine.LastMeasuredCurve,
                ArrivalLine = machine.LastArrivalLine,
                CreaseHeight = machine.LastCreaseHeight,
                Flighted = machine.LastRequest.flighted,
                LaunchDegrees = machine.LastSolution.Reachable ? machine.LastSolution.ElevationDegrees : float.NaN,
                TurnDegrees = machine.LastTurn,
                Bounce = machine.LastBounce ?? BallRecord.NaN3,
                BounceError = machine.LastBounceError,
                ReboundOffset = machine.LastReboundOffset,
                Result = result,
                ClosestGap = resolver != null ? resolver.ClosestGapFor(result.DeliveryId) : float.NaN,
            };
            if (_contacts.TryGetValue(result.DeliveryId, out var c))
            {
                r.ContactPoint = c.Point;
                r.ContactNormal = c.Normal;
                r.BallVelocityIn = c.BallVelocityIn;
                r.BallVelocityOut = c.BallVelocityOut;
                r.BatPointVelocity = c.BatPointVelocity;
                r.ApproachSpeed = c.Response.ApproachSpeed;
                r.SweetFactor = c.Response.SweetFactor;
                r.EdgeFactor = c.Response.EdgeFactor;
                r.HeightFromToe = c.HeightFromToe;
            }
            return r;
        }

        // ---------- Pause ----------

        void OnApplicationFocus(bool focused)
        {
            if (!focused) Pause("The headset lost focus.");
        }

        void OnApplicationPause(bool paused)
        {
            if (paused) Pause("The app was paused.");
        }

        void WatchTracking()
        {
            if (bat == null || bat.IsControllerTracked(bat.Hand))
            {
                _untrackedFor = 0f;
                return;
            }
            _untrackedFor += Time.unscaledDeltaTime;
            if (_untrackedFor >= config.session.trackingLossPauseSeconds)
                Pause("The bat controller lost tracking.");
        }

        void Pause(string reason)
        {
            if (_session == null)
                return;
            var step = _session.Pause();
            if (_session.Phase != SessionPhase.Paused)
                return;
            Handle(step);
            if (wicket != null) wicket.ResetWicket();
            _untrackedFor = 0f;
            if (bat != null) bat.ResetMotion(MotionResetReason.FocusChanged);
            Debug.Log($"[CricketVR] Session paused: {reason}");
            ui.PlaceNear(xrOrigin);
            ui.ShowPaused($"{reason}\nBall {_session.NextBall + 1} of {_session.Options.BallBudget} will be bowled again.\nClear your swing space, then press Resume.");
            pointer?.SetVisible(true);
        }

        // ---------- Menu ----------

        /// <summary>Same as pressing the named menu button (tests, desktop debugging).</summary>
        public void Press(string buttonName) => OnButton(buttonName);

        /// <summary>The batter's ready press (tap B): bowls the next ball. Ignored when no ball can be bowled.</summary>
        public void PressReady()
        {
            if (_session == null || !_session.Ready())
                return;
            if (_readyPress?.activeControl?.device is XRControllerWithRumble c)
                c.SendImpulse(0.25f, 0.04f);
        }

        /// <summary>Simulates losing focus (tests): the same path as taking the headset off.</summary>
        public void SimulateFocusLoss() => Pause("Focus lost (simulated).");

        void OnButton(string name)
        {
            switch (name)
            {
                case "NetPractice": _mode = SessionMode.NetPractice; ShowMenu(); break;
                case "MatchSim": _mode = SessionMode.MatchSimulation; ShowMenu(); break;
                case "Overs1": _overs = 1; ShowMenu(); break;
                case "Overs2": _overs = 2; ShowMenu(); break;
                case "Overs3": _overs = 3; ShowMenu(); break;
                case "Overs5": _overs = 5; ShowMenu(); break;
                case "Easy": case "Medium": case "Hard": _difficulty = name; ShowMenu(); break;
                case "BowlerPace": case "BowlerOffSpin": case "BowlerLegSpin":
                    _netBowler = Mathf.Max(0, System.Array.FindIndex(NetBowler.All, b => b.Button == name)); ShowMenu(); break;
                case "WicketOver": case "WicketAround":
                    _wicketSide = Mathf.Max(0, System.Array.FindIndex(WicketSide.All, w => w.Button == name)); ShowMenu(); break;
                case "RightHanded": handedness?.Set(Handedness.Right); ShowMenu(); break;
                case "LeftHanded": handedness?.Set(Handedness.Left); ShowMenu(); break;
                case "BatInHand": bat?.SetMode(BatMode.InHand); ShowMenu(); break;
                case "BatStrapped": bat?.SetMode(BatMode.Strapped); ShowMenu(); break;
                case "BatSetup": EnterBatSetup(); break;
                case "Start": StartSession(); break;
                case "PlayAgain": StartSession(); break;
                case "NewScenario": StartSession(); break;
                case "Menu": Handle(_session.ToMenu()); if (wicket != null) wicket.ResetWicket(); ShowMenu(); break;
                case "Resume":
                    if (bat != null) bat.ResetMotion(MotionResetReason.FocusChanged);
                    var step = _session.Resume();
                    pointer?.SetVisible(false);
                    ui.ShowPlaying("");
                    Handle(step);
                    break;
            }
        }

        void ShowMenu()
        {
            if (_session.Phase != SessionPhase.Menu)
                return;
            var envMode = _mode == SessionMode.NetPractice ? EnvironmentMode.NetMode : EnvironmentMode.GroundMode;
            ApplyEnvironment(envMode);
            ui.PlaceNear(xrOrigin);
            bool strapped = bat != null && bat.Mode == BatMode.Strapped;
            ui.ShowMenu(_mode == SessionMode.NetPractice, strapped
                ? "Tap a button with the bat."
                : "Point with the right controller and pull the trigger, or tap a button with the bat.");
            ui.Select("BatInHand", !strapped);
            ui.Select("BatStrapped", strapped);
            ui.Select("NetPractice", _mode == SessionMode.NetPractice);
            ui.Select("MatchSim", _mode == SessionMode.MatchSimulation);
            foreach (int o in new[] { 1, 2, 3, 5 }) ui.Select("Overs" + o, _overs == o);
            foreach (var d in new[] { "Easy", "Medium", "Hard" }) ui.Select(d, _difficulty == d);
            for (int b = 0; b < NetBowler.All.Length; b++) ui.Select(NetBowler.All[b].Button, _netBowler == b);
            for (int w = 0; w < WicketSide.All.Length; w++) ui.Select(WicketSide.All[w].Button, _wicketSide == w);
            bool right = handedness == null || handedness.Current == Handedness.Right;
            ui.Select("RightHanded", right);
            ui.Select("LeftHanded", !right);
            string board = _mode == SessionMode.NetPractice
                ? $"NET PRACTICE\n{_overs} over{(_overs == 1 ? "" : "s")} ({_overs * 6} balls), {BowlerName}, {WicketSide.All[_wicketSide].Label.ToLowerInvariant()} the wicket\nNo runs: every ball gets a result." +
                  (SwingCheck ? "\n<b>SWING CHECK BUILD (T076)</b>: in/out at 0.15, 0.30, 0.50 m;\nover 1 at 120 km/h, over 2 at 140 km/h." : "") +
                  (SpinCheck ? "\n<b>SPIN CHECK BUILD (T082)</b>: off-spin, doosra, leg-spin, googly;\nballs 1-4 good length, 5-8 full, 9-12 short; 85 km/h." : "") +
                  (WicketCheck ? "\n<b>WICKET CHECK BUILD (T088)</b>: every ball hits the stumps;\n80, 120, 140 km/h, grazes, spin. Leave them." : "")
                : $"MATCH SIMULATION\n{_difficulty}\nScenarios and scoring arrive in M9/M10;\nthis build bowls {config.session.matchPlaceholderBalls} balls, scoring pending." +
                  (WicketCheck ? "\n<b>WICKET CHECK BUILD (T088)</b>: every ball hits the stumps (LED on)." : "");
            if (strapped)
                board += bat.MountFitted ? "\nReal bat (fitted)." : "\n<b>Real bat: not fitted yet.</b> Open Bat setup.";
            ui.Board(board);
            pointer?.SetVisible(true);
        }

        void EnterBatSetup()
        {
            _batSetup = true;
            _doneDown = -1f;
            pointer?.SetVisible(false);
            if (setupControls != null) setupControls.allowSetupInput = true;
            ShowBatSetupPanel();
        }

        void ShowBatSetupPanel()
        {
            if (bat != null && bat.Mode == BatMode.Strapped)
            {
                // The frozen bat stands to the right of the panel, upright, its controller side toward the player.
                var at = (xrOrigin != null ? xrOrigin.position : Vector3.zero) + new Vector3(0.62f, 1.25f, 0.65f);
                var toPlayer = (xrOrigin != null ? xrOrigin.position : Vector3.zero) - at;
                toPlayer.y = 0f;
                var face = Quaternion.LookRotation(bat.HitsWithControllerSide ? toPlayer : -toPlayer, Vector3.up);
                bat.BeginFit(new Pose(at, face));
                ShowFitPanel();
                return;
            }
            ui.ShowBatSetup("Thumbstick up/down: tilt.  Left/right: spin.\nTrigger + left/right: lean.  Grip + up/down: slide.\nGrip + left/right: face turn.  Stick click: reset (hold: flip face).\nHold B: move the bat to the other controller.");
        }

        void ShowFitPanel()
        {
            bool turning = setupControls != null && setupControls.FitTurning;
            string move = "<b>B / A</b>: up / down the bat\n<b>Stick left / right</b>: across the blade\n<b>Stick up / down</b>: away from / onto the blade";
            string turn = "<b>B / A</b>: roll it about its own length\n<b>Stick left / right</b>: turn it on the bat\n<b>Stick up / down</b>: tilt it";
            ui.ShowBatSetup(
                "<b>REAL BAT: fit the controller</b>\nThe virtual bat is frozen on your right. Put the cyan controller exactly where yours is taped on your real bat.\n\n" +
                $"<b>{(turning ? "TURN" : "MOVE")}</b> (tap the stick to switch to {(turning ? "MOVE" : "TURN")})\n" + (turning ? turn : move),
                "Hold the stick click 1 s: OK (save)", "Bat setup");
        }

        void ExitBatSetup()
        {
            if (!_batSetup)
                return;
            _batSetup = false;
            if (bat != null && bat.Fitting) bat.EndFit(false);
            if (setupControls != null) setupControls.allowSetupInput = false;
            // Shown on the first Update: HandednessSetup.Start may move the rig to the saved stance first.
            _menuPending = true;
        }

        void StartSession()
        {
            int seed = System.Environment.TickCount;
            var options = _mode == SessionMode.NetPractice
                ? SessionOptions.NetPractice(_overs, seed)
                : SessionOptions.MatchSimulation(Mathf.Max(1, config.session.matchPlaceholderBalls), seed);
            ApplyEnvironment(_mode == SessionMode.NetPractice ? EnvironmentMode.NetMode : EnvironmentMode.GroundMode);
            _rng = new System.Random(seed);
            _types = config.delivery.TypeCatalogue();
            _sessionId = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
            _sessionStart = Time.time;
            _lastResultLine = "";
            _contacts.Clear();
            score.ClearResults();
            if (wicket != null) wicket.ResetWicket();
            log?.BeginSession(_sessionId);
            Handle(_session.Start(options));
            pointer?.SetVisible(false);
            ui.ShowPlaying("");
            if (bat != null) bat.ResetMotion(MotionResetReason.Manual);
            Debug.Log($"[CricketVR] Session {_sessionId}: {_mode}, {options.BallBudget} balls, seed {seed}" +
                      (_mode == SessionMode.MatchSimulation ? $", {_difficulty}" : ""));
        }

        void ApplyEnvironment(EnvironmentMode mode)
        {
            if (environment != null && environment.Mode != mode)
                environment.Apply(mode);
            score.SetEnvironment(mode);
            if (wicket != null) wicket.SetMode(mode);
        }

        void ShowEnd()
        {
            log?.EndSession();
            var records = _session.Records;
            var t = SessionTotals.From(records);
            var sb = new StringBuilder();
            for (int over = 0; over * 6 < records.Count; over++)
            {
                sb.Append($"Over {over + 1}:");
                for (int i = over * 6; i < Mathf.Min(records.Count, over * 6 + 6); i++)
                    sb.Append("  ").Append(Symbol(records[i].Result));
                sb.Append('\n');
            }
            sb.Append($"\n{t}\nH hit   M miss   B bowled   P scoring pending");
            string heading = _mode == SessionMode.NetPractice ? $"Net Practice: {records.Count} balls" : $"Match Simulation ({_difficulty}): {records.Count} balls";
            ui.PlaceNear(xrOrigin);
            ui.ShowEnd(heading, sb.ToString(), _mode == SessionMode.MatchSimulation);
            ui.Board($"{heading}\n{t}");
            pointer?.SetVisible(true);
            Debug.Log($"[CricketVR] Session {_sessionId} ended: {t}");
        }

        static string Symbol(DeliveryResult r)
        {
            switch (r.Outcome)
            {
                case DeliveryOutcome.Bowled: return "B";
                case DeliveryOutcome.Miss: return "M";
                case DeliveryOutcome.ScoringPending: return "P";
                default: return "H";
            }
        }

        void RefreshBoard()
        {
            switch (_session.Phase)
            {
                case SessionPhase.AwaitingReady:
                case SessionPhase.Countdown:
                case SessionPhase.Delivering:
                case SessionPhase.BetweenBalls:
                    var o = _session.Options;
                    int n = Mathf.Min(_session.NextBall + (_session.Phase == SessionPhase.BetweenBalls ? 0 : 1), _session.Options.BallBudget);
                    var head = _mode == SessionMode.NetPractice
                        ? $"NET PRACTICE  ball {n} of {o.BallBudget}  (over {(n - 1) / 6 + 1} of {o.Overs})"
                        : $"MATCH SIMULATION  ball {n} of {o.BallBudget}  (scoring pending)";
                    string middle = _session.Phase == SessionPhase.AwaitingReady ? "<size=140%>Press B when ready</size>"
                        : _session.Phase == SessionPhase.Countdown ? (_session.TimeLeft > 0.5f ? $"<size=180%>{Mathf.CeilToInt(_session.TimeLeft)}</size>" : "")
                        : _session.Phase == SessionPhase.Delivering ? "<size=140%>Play!</size>"
                        : _session.Timings.waitForReady && !_session.IsOver ? "<size=100%>Press B for the next ball</size>" : "";
                    ui.Board($"{head}\n{middle}\n{_lastResultLine}\n<size=80%>{SessionTotals.From(_session.Records)}</size>");
                    break;
            }
        }

        static bool Tapped(InputAction action, ref float downTime)
        {
            if (action.WasPressedThisFrame())
                downTime = Time.unscaledTime;
            if (action.WasReleasedThisFrame() && downTime >= 0f)
            {
                bool tap = Time.unscaledTime - downTime < 0.4f;
                downTime = -1f;
                return tap;
            }
            return false;
        }

        public void ConfigureWicket(WicketReaction wicketReaction) => wicket = wicketReaction;


        public void Configure(GameConfig cfg, BowlingMachine bowlingMachine, BallController ballController, ScoreManager scoreManager,
            BatBallContactResolver contactResolver, BatController batController, HandednessSetup handednessSetup,
            EnvironmentModeSwitch environmentSwitch, BowlingDebugControls debug, BatSetupControls setup, SessionUI sessionUi,
            MenuPointer menuPointer, BallLogWriter ballLog, Transform origin)
        {
            config = cfg;
            machine = bowlingMachine;
            ball = ballController;
            score = scoreManager;
            resolver = contactResolver;
            bat = batController;
            handedness = handednessSetup;
            environment = environmentSwitch;
            debugControls = debug;
            setupControls = setup;
            ui = sessionUi;
            pointer = menuPointer;
            log = ballLog;
            xrOrigin = origin;
        }
    }
}
