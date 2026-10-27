using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace CricketVR.Tests
{
    /// <summary>M7 session framework (REQ-011..014, REQ-026; T047): transitions, budget, stale results, pause, restart.</summary>
    public class SessionStateMachineTests
    {
        static readonly SessionTimings T = new SessionTimings { firstCountdown = 3f, countdown = 2f, betweenBalls = 3f };
        SessionStateMachine _s;
        int _nextId = 100;

        [SetUp]
        public void SetUp()
        {
            _s = new SessionStateMachine(T);
            _nextId = 100;
        }

        static BallRecord Record(int id) => new BallRecord { DeliveryId = id };

        /// <summary>Runs the countdown out, launches, and returns the delivery id.</summary>
        int LaunchNext()
        {
            var step = _s.Tick(10f);
            Assert.AreEqual(SessionAction.LaunchBall, step.Action, $"expected a launch in {_s.Phase}");
            Assert.AreEqual(_s.NextBall, step.BallIndex);
            int id = _nextId++;
            _s.BallLaunched(id);
            return id;
        }

        void PlayBall()
        {
            int id = LaunchNext();
            Assert.IsTrue(_s.BallFinalized(Record(id)));
            Assert.AreEqual(SessionPhase.BetweenBalls, _s.Phase);
        }

        [Test]
        public void NetPracticeOfTwoOversBowlsExactlyTwelveBallsThenEnds()
        {
            _s.Start(SessionOptions.NetPractice(2, 7));
            Assert.AreEqual(SessionPhase.Countdown, _s.Phase);
            Assert.AreEqual(12, _s.Options.BallBudget);
            for (int i = 0; i < 12; i++)
            {
                PlayBall();
                var step = _s.Tick(10f);
                if (i < 11) Assert.AreEqual(SessionPhase.Countdown, _s.Phase);
                else Assert.AreEqual(SessionAction.SessionEnded, step.Action);
            }
            Assert.AreEqual(SessionPhase.SessionEnd, _s.Phase);
            Assert.AreEqual(12, _s.Records.Count);
            Assert.AreEqual(SessionAction.None, _s.Tick(10f).Action, "no thirteenth ball");
            Assert.IsFalse(_s.BallFinalized(Record(999)));
            Assert.AreEqual(11, _s.Records[11].BallIndex);
            Assert.AreEqual(2, _s.Records[11].Over);
            Assert.AreEqual(6, _s.Records[11].BallInOver);
        }

        [Test]
        public void CountdownAndIntervalTimesAreRespected()
        {
            _s.Start(SessionOptions.NetPractice(1, 0));
            Assert.AreEqual(SessionAction.None, _s.Tick(2.9f).Action, "first countdown is 3 s");
            Assert.AreEqual(SessionAction.LaunchBall, _s.Tick(0.2f).Action);
            _s.BallLaunched(1);
            _s.BallFinalized(Record(1));
            Assert.AreEqual(SessionAction.None, _s.Tick(2.9f).Action);
            Assert.AreEqual(SessionPhase.BetweenBalls, _s.Phase);
            _s.Tick(0.2f);
            Assert.AreEqual(SessionPhase.Countdown, _s.Phase);
            Assert.AreEqual(SessionAction.None, _s.Tick(1.9f).Action, "later countdowns are 2 s");
            Assert.AreEqual(SessionAction.LaunchBall, _s.Tick(0.2f).Action);
        }

        [Test]
        public void StaleDuplicateAndOutOfPhaseResultsAreRejected()
        {
            _s.Start(SessionOptions.NetPractice(1, 0));
            Assert.IsFalse(_s.BallFinalized(Record(5)), "nothing in flight during the countdown");
            int id = LaunchNext();
            Assert.IsFalse(_s.BallFinalized(Record(id + 1)), "wrong delivery id");
            Assert.IsTrue(_s.BallFinalized(Record(id)));
            Assert.IsFalse(_s.BallFinalized(Record(id)), "duplicate");
            Assert.AreEqual(1, _s.Records.Count);
        }

        [Test]
        public void PausingDuringADeliveryCancelsItWithoutConsumingTheBall()
        {
            _s.Start(SessionOptions.NetPractice(1, 0));
            PlayBall();
            _s.Tick(10f);
            int id = LaunchNext();
            var step = _s.Pause();
            Assert.AreEqual(SessionAction.CancelBall, step.Action);
            Assert.AreEqual(id, step.DeliveryId);
            Assert.AreEqual(SessionPhase.Paused, _s.Phase);
            Assert.IsFalse(_s.BallFinalized(Record(id)), "the cancelled ball's late result is stale");
            Assert.AreEqual(SessionAction.None, _s.Tick(100f).Action, "no automatic resume");
            _s.Resume();
            Assert.AreEqual(SessionPhase.Countdown, _s.Phase);
            var launch = _s.Tick(10f);
            Assert.AreEqual(1, launch.BallIndex, "the same ball is bowled again");
            Assert.AreEqual(1, _s.Records.Count);
        }

        [Test]
        public void PauseFromCountdownOrBetweenBallsResumesSafely()
        {
            _s.Start(SessionOptions.NetPractice(1, 0));
            Assert.AreEqual(SessionAction.None, _s.Pause().Action);
            _s.Resume();
            PlayBall();
            _s.Pause();
            _s.Resume();
            Assert.AreEqual(SessionPhase.Countdown, _s.Phase);
            Assert.AreEqual(1, _s.NextBall);
        }

        [Test]
        public void PauseAfterTheLastBallResumesToTheEndScreen()
        {
            _s.Start(SessionOptions.MatchSimulation(1, 0));
            PlayBall();
            _s.Pause();
            Assert.AreEqual(SessionAction.SessionEnded, _s.Resume().Action);
            Assert.AreEqual(SessionPhase.SessionEnd, _s.Phase);
        }

        [Test]
        public void PlayAgainAndMenuWorkFromEveryPhaseWithoutStaleWork()
        {
            foreach (var target in new[] { SessionPhase.Countdown, SessionPhase.Delivering, SessionPhase.BetweenBalls, SessionPhase.Paused, SessionPhase.SessionEnd })
                foreach (bool toMenu in new[] { false, true })
                {
                    _s = new SessionStateMachine(T);
                    _s.Start(SessionOptions.NetPractice(1, 0));
                    int active = Reach(target);
                    int generation = _s.Generation;
                    var step = toMenu ? _s.ToMenu() : _s.PlayAgain();
                    Assert.Greater(_s.Generation, generation, $"{target}: work invalidated");
                    if (target == SessionPhase.Delivering)
                        Assert.AreEqual((SessionAction.CancelBall, active), (step.Action, step.DeliveryId), $"{target}: the ball in flight is cancelled");
                    Assert.AreEqual(toMenu ? SessionPhase.Menu : SessionPhase.Countdown, _s.Phase, $"{target}");
                    Assert.AreEqual(0, _s.Records.Count, $"{target}: records cleared");
                    Assert.AreEqual(-1, _s.ActiveDeliveryId);
                    if (active > 0)
                        Assert.IsFalse(_s.BallFinalized(Record(active)), $"{target}: the old ball's result is stale");
                }
        }

        int Reach(SessionPhase target)
        {
            switch (target)
            {
                case SessionPhase.Countdown: return -1;
                case SessionPhase.Delivering: return LaunchNext();
                case SessionPhase.BetweenBalls: PlayBall(); return -1;
                case SessionPhase.Paused: { int id = LaunchNext(); _s.Pause(); return id; }
                default:
                    for (int i = 0; i < 6; i++) { PlayBall(); _s.Tick(10f); }
                    Assert.AreEqual(SessionPhase.SessionEnd, _s.Phase);
                    return -1;
            }
        }

        [Test]
        public void EarlyEndRuleStopsTheSessionAfterTheDecidingBall()
        {
            _s.Start(SessionOptions.MatchSimulation(18, 0));
            _s.EndEarly = records => records.Count >= 4; // e.g. target reached on the 4th ball
            for (int i = 0; i < 4; i++)
            {
                PlayBall();
                _s.Tick(10f);
            }
            Assert.AreEqual(SessionPhase.SessionEnd, _s.Phase);
            Assert.AreEqual(4, _s.Records.Count);
        }

        [Test]
        public void ARejectedLaunchRetriesTheSameBall()
        {
            _s.Start(SessionOptions.NetPractice(1, 0));
            _s.Tick(10f);
            _s.BallLaunched(0);
            Assert.AreEqual(SessionPhase.Countdown, _s.Phase);
            Assert.AreEqual(0, _s.Tick(10f).BallIndex);
        }
    }

    /// <summary>The batter controls each ball with a ready press (tap B; owner decision 2026-10-26).</summary>
    public class ReadyPressTests
    {
        SessionStateMachine _s;

        [SetUp]
        public void SetUp() => _s = new SessionStateMachine(SessionTimings.Default);

        [Test]
        public void DefaultSettingsWaitForTheBatterBeforeEveryBall()
        {
            Assert.IsTrue(SessionTimings.Default.waitForReady);
            Assert.AreEqual(0f, SessionTimings.Default.readyDelay);
            _s.Start(SessionOptions.NetPractice(1, 0));
            Assert.AreEqual(SessionPhase.AwaitingReady, _s.Phase);
            Assert.AreEqual(SessionAction.None, _s.Tick(60f).Action, "nothing is bowled without a ready press");
            Assert.IsTrue(_s.Ready());
            Assert.AreEqual(SessionAction.LaunchBall, _s.Tick(0.01f).Action, "bowled straight after the press");
            _s.BallLaunched(1);
            Assert.IsFalse(_s.Ready(), "no ready press during a delivery");
            Assert.IsTrue(_s.BallFinalized(new BallRecord { DeliveryId = 1 }));
            Assert.AreEqual(SessionPhase.BetweenBalls, _s.Phase);
            Assert.AreEqual(SessionAction.None, _s.Tick(60f).Action);
            Assert.AreEqual(SessionPhase.AwaitingReady, _s.Phase, "after the result the game waits again");
            Assert.AreEqual(SessionAction.None, _s.Tick(60f).Action);
        }

        [Test]
        public void ReadyDuringTheResultDisplaySkipsTheWait()
        {
            _s.Start(SessionOptions.NetPractice(1, 0));
            _s.Ready();
            _s.Tick(0.01f);
            _s.BallLaunched(5);
            _s.BallFinalized(new BallRecord { DeliveryId = 5 });
            Assert.IsTrue(_s.Ready(), "the batter may ask for the next ball while the result is shown");
            Assert.AreEqual(SessionAction.LaunchBall, _s.Tick(0.01f).Action);
        }

        [Test]
        public void NoReadyAfterTheLastBallAndPauseReturnsToWaiting()
        {
            _s.Start(SessionOptions.MatchSimulation(1, 0));
            _s.Pause();
            Assert.AreEqual(SessionPhase.Paused, _s.Phase, "pause works while waiting");
            _s.Resume();
            Assert.AreEqual(SessionPhase.AwaitingReady, _s.Phase, "resume waits for the batter, never bowls by itself");
            _s.Ready();
            _s.Tick(0.01f);
            _s.BallLaunched(9);
            _s.BallFinalized(new BallRecord { DeliveryId = 9 });
            Assert.IsFalse(_s.Ready(), "budget used: no further ball");
            Assert.AreEqual(SessionAction.SessionEnded, _s.Tick(60f).Action);
        }
    }

    /// <summary>Per-ball record and calibration CSV (REQ-027; T048).</summary>
    public class BallRecordTests
    {
        [Test]
        public void CsvRoundTripKeepsEveryValue()
        {
            var result = new DeliveryResult(42, DeliveryOutcome.Hit, DeliveryEnd.NetHit, ScoringZone.FrontMiddle, true, false,
                CarryState.Measured, 12.375f, default, default);
            var r = new BallRecord
            {
                SessionId = "s1, \"quoted\"", Mode = SessionMode.NetPractice, BallIndex = 7, DeliveryId = 42, Time = 61.25f,
                Delivery = DeliveryConfig.Create("Net Practice: Good length", 82f, 0.125f, 6.5f), Seed = 1234,
                Result = result, ContactPoint = new Vector3(0.1f, 0.6f, 1.2f), BallVelocityOut = new Vector3(1f, 5f, 30f),
                SweetFactor = 0.83f, EdgeFactor = -0.1f, SwingTimingOffset = -0.021f, ShotClass = "Middled",
                DeliveryType = "Googly", Movement = 0.625f, Hand = Handedness.Left, LineRef = LineReference.AtBatter, BallSeed = -77,
                VarSwing = 1f, VarDrift = 0.875f, VarTurn = 1.125f, PredictedSwing = 0.4375f, MeasuredSwing = 0.4f,
                PredictedCurve = 0.3125f, MeasuredCurve = 0.3f,
                TurnDegrees = -5.5f, ArrivalLine = 0.0625f, CreaseHeight = 0.71875f, Bounce = new Vector3(0.05f, 0.036f, 5.998f), BounceError = 0.0021f, ReboundOffset = -0.03125f,
            };
            var back = BallRecord.FromCsv(BallRecord.CsvHeader, r.ToCsvRow());
            Assert.AreEqual(r.SessionId, back.SessionId);
            Assert.AreEqual(r.Mode, back.Mode);
            Assert.AreEqual(7, back.BallIndex);
            Assert.AreEqual(2, back.Over);
            Assert.AreEqual(2, back.BallInOver);
            Assert.AreEqual(42, back.DeliveryId);
            Assert.AreEqual(61.25f, back.Time);
            Assert.AreEqual(r.Delivery.label, back.Delivery.label);
            Assert.AreEqual(82f, back.Delivery.speedKmh);
            Assert.AreEqual(0.125f, back.Delivery.line);
            Assert.AreEqual(1234, back.Seed);
            Assert.AreEqual(DeliveryOutcome.Hit, back.Result.Outcome);
            Assert.AreEqual(ScoringZone.FrontMiddle, back.Result.Zone);
            Assert.IsTrue(back.Result.BatContact);
            Assert.AreEqual(CarryState.Measured, back.Result.Carry);
            Assert.AreEqual(12.375f, back.Result.CarryMetres);
            Assert.AreEqual(r.ContactPoint, back.ContactPoint);
            Assert.AreEqual(r.BallVelocityOut, back.BallVelocityOut);
            Assert.AreEqual(0.83f, back.SweetFactor);
            Assert.AreEqual(-0.021f, back.SwingTimingOffset);
            Assert.IsNaN(back.ClosestGap, "unknown stays unknown");
            Assert.IsNaN(back.BallVelocityIn.x);
            Assert.AreEqual("Middled", back.ShotClass);
            Assert.AreEqual(-1, back.SimRuns);
            // M7.5 delivery-type columns (T072, REQ-045)
            Assert.AreEqual("Googly", back.DeliveryType);
            Assert.AreEqual(0.625f, back.Movement);
            Assert.AreEqual(Handedness.Left, back.Hand);
            Assert.AreEqual(LineReference.AtBatter, back.LineRef);
            Assert.AreEqual(-77, back.BallSeed);
            Assert.AreEqual(1f, back.VarSwing);
            Assert.AreEqual(0.875f, back.VarDrift);
            Assert.AreEqual(1.125f, back.VarTurn);
            Assert.AreEqual(0.4375f, back.PredictedSwing);
            Assert.AreEqual(0.4f, back.MeasuredSwing);
            Assert.AreEqual(0.3125f, back.PredictedCurve);
            Assert.AreEqual(0.3f, back.MeasuredCurve);
            Assert.AreEqual(-5.5f, back.TurnDegrees);
            Assert.AreEqual(0.0625f, back.ArrivalLine);
            Assert.AreEqual(0.71875f, back.CreaseHeight);
            Assert.AreEqual(r.Bounce, back.Bounce);
            Assert.AreEqual(0.0021f, back.BounceError);
            Assert.AreEqual(-0.03125f, back.ReboundOffset);
            foreach (var column in new[] { "type", "movement", "hand", "line_ref", "ball_seed", "var_swing", "var_drift", "var_turn",
                         "pred_swing_m", "swing_m", "pred_curve_m", "curve_m", "turn_deg", "arrival_line_m", "bounce_x", "bounce_z", "bounce_error_m", "rebound_offset_m" })
                StringAssert.Contains(column, BallRecord.CsvHeader);
        }

        [Test]
        public void StockRecordLeavesMovementMeasurementsEmpty()
        {
            var r = new BallRecord { Delivery = DeliveryConfig.Create("x", 80f, 0f, 6f), DeliveryType = "Stock", Movement = 0f };
            var back = BallRecord.FromCsv(BallRecord.CsvHeader, r.ToCsvRow());
            Assert.AreEqual("Stock", back.DeliveryType);
            Assert.AreEqual(0f, back.Movement);
            Assert.IsNaN(back.MeasuredCurve, "not measured = empty cell");
            Assert.IsNaN(back.PredictedSwing);
            Assert.IsNaN(back.TurnDegrees);
            Assert.IsNaN(back.Bounce.x);
            Assert.IsNaN(back.BounceError);
        }

        [Test]
        public void HeaderAndRowHaveTheSameColumnCountAndAreLocaleProof()
        {
            var prev = System.Threading.Thread.CurrentThread.CurrentCulture;
            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE"); // decimal comma
                var r = new BallRecord { Time = 1.5f, Delivery = DeliveryConfig.Create("x", 80f, 0.1f, 6f) };
                string row = r.ToCsvRow();
                Assert.AreEqual(BallRecord.CsvHeader.Split(',').Length, row.Split(',').Length);
                StringAssert.Contains("1.5", row);
                Assert.AreEqual(1.5f, BallRecord.FromCsv(BallRecord.CsvHeader, row).Time);
            }
            finally
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = prev;
            }
        }
    }

    /// <summary>Delivery plans (REQ-028; T049): reachable, within range, mirrored, deterministic.</summary>
    public class DeliveryPlannerTests
    {
        static readonly System.Func<DeliveryConfig, bool> Solver = DeliveryPlanner.SolverCheck(9.81f, 1f / 144f, 0.036f);

        static IEnumerable<DeliveryPlanSettings> Plans()
        {
            yield return DeliveryPlanSettings.NetPractice;
            yield return DeliveryPlanSettings.Easy;
            yield return DeliveryPlanSettings.Medium;
            yield return DeliveryPlanSettings.Hard;
        }

        [Test]
        public void ThousandDrawsPerPlanAreAllReachableAndInRange()
        {
            foreach (var plan in Plans())
            {
                var rng = new System.Random(11);
                int redraws = 0, yorkers = 0, shorts = 0;
                for (int i = 0; i < 1000; i++)
                {
                    var d = DeliveryPlanner.Draw(plan, rng, false, Solver, out int r);
                    redraws += r;
                    Assert.IsTrue(Solver(d), $"{plan.label}: {d} unreachable");
                    Assert.That(d.speedKmh, Is.InRange(plan.minSpeedKmh, plan.maxSpeedKmh), plan.label);
                    Assert.That(d.line, Is.InRange(plan.minLine, plan.maxLine), plan.label);
                    Assert.LessOrEqual(d.speedKmh, 120f, "Hard stays at or below 120 km/h (C-14)");
                    if (d.label.Contains("Yorker")) yorkers++;
                    if (d.label.Contains("Short")) shorts++;
                }
                Debug.Log($"[CricketVR] Plan {plan.label}: {redraws} redraws in 1000, yorkers {yorkers}, short {shorts}");
                Assert.AreEqual(plan.yorkerShare * 1000f, yorkers, 40f, $"{plan.label} yorker share");
                Assert.AreEqual(plan.shortShare * 1000f, shorts, 40f, $"{plan.label} short share");
            }
        }

        [Test]
        public void LeftHandersGetMirroredLinesAndTheSameSeedRepeats()
        {
            var plan = DeliveryPlanSettings.Medium;
            var a = DeliveryPlanner.Draw(plan, new System.Random(3), false, Solver, out _);
            var b = DeliveryPlanner.Draw(plan, new System.Random(3), true, Solver, out _);
            var c = DeliveryPlanner.Draw(plan, new System.Random(3), false, Solver, out _);
            Assert.AreEqual(-a.line, b.line, 1e-6f);
            Assert.AreEqual(a.length, b.length);
            Assert.AreEqual(a.ToString(), c.ToString(), "same seed, same delivery");
        }

        [Test]
        public void UnreachableDrawsAreRedrawnNotAltered()
        {
            int calls = 0;
            var d = DeliveryPlanner.Draw(DeliveryPlanSettings.Easy, new System.Random(1), false, x => ++calls > 3, out int redraws);
            Assert.AreEqual(3, redraws);
            Assert.That(d.speedKmh, Is.InRange(70f, 85f));
            var fallback = DeliveryPlanner.Draw(DeliveryPlanSettings.Easy, new System.Random(1), false, x => false, out redraws);
            Assert.AreEqual(DeliveryPlanner.MaxRedraws + 1, redraws);
            StringAssert.Contains("fallback", fallback.label);
        }
    }
}
