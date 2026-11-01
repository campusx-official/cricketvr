using System;
using System.Collections.Generic;
using UnityEngine;

namespace CricketVR
{
    public enum SessionMode { NetPractice, MatchSimulation }

    public enum SessionPhase
    {
        /// <summary>Start menu; no session running.</summary>
        Menu,
        /// <summary>Waiting for the batter to say "ready" (tap B) before the next ball (owner decision 2026-10-26).</summary>
        AwaitingReady,
        /// <summary>Short delay between "ready" and the ball (0 s by default), or the automatic countdown.</summary>
        Countdown,
        /// <summary>A ball has been requested or is in flight; waiting for its single result.</summary>
        Delivering,
        /// <summary>The last ball's result is shown; the next countdown follows.</summary>
        BetweenBalls,
        /// <summary>Focus or tracking lost; waiting for the player to confirm readiness.</summary>
        Paused,
        /// <summary>All balls played (or the session ended early); end screen.</summary>
        SessionEnd,
    }

    public struct SessionOptions
    {
        public SessionMode Mode;
        /// <summary>Net Practice: number of overs (6 balls each).</summary>
        public int Overs;
        /// <summary>Total balls the session may bowl (Net Practice: overs x 6; Match Simulation: the scenario's balls).</summary>
        public int BallBudget;
        /// <summary>Seed for the delivery plan (and later the scenario), recorded for reproduction.</summary>
        public int Seed;

        public static SessionOptions NetPractice(int overs, int seed) =>
            new SessionOptions { Mode = SessionMode.NetPractice, Overs = overs, BallBudget = overs * 6, Seed = seed };

        public static SessionOptions MatchSimulation(int balls, int seed) =>
            new SessionOptions { Mode = SessionMode.MatchSimulation, Overs = (balls + 5) / 6, BallBudget = balls, Seed = seed };
    }

    [Serializable]
    public struct SessionTimings
    {
        [Tooltip("True: every ball waits for the batter's ready press (tap B). False: automatic countdowns.")]
        public bool waitForReady;
        [Tooltip("Delay from the ready press to the ball (s).")]
        public float readyDelay;
        [Tooltip("Automatic mode only: countdown before the first ball (s).")]
        public float firstCountdown;
        [Tooltip("Automatic mode only: countdown before later balls (s).")]
        public float countdown;
        [Tooltip("Time the result is shown before the next ball can be requested automatically, or the end screen opens (s).")]
        public float betweenBalls;

        public static SessionTimings Default => new SessionTimings { waitForReady = true, readyDelay = 0f, firstCountdown = 3f, countdown = 2f, betweenBalls = 3f };
    }

    public enum SessionAction
    {
        None,
        /// <summary>The runtime must bowl ball <see cref="SessionStep.BallIndex"/> and report it with <see cref="SessionStateMachine.BallLaunched"/>.</summary>
        LaunchBall,
        /// <summary>The runtime must cancel delivery <see cref="SessionStep.DeliveryId"/> (it is not consumed).</summary>
        CancelBall,
        /// <summary>The session has ended; show the end screen.</summary>
        SessionEnded,
    }

    public struct SessionStep
    {
        public SessionAction Action;
        public int BallIndex;
        public int DeliveryId;

        public static SessionStep None => default;
        public static SessionStep Launch(int ball) => new SessionStep { Action = SessionAction.LaunchBall, BallIndex = ball };
        public static SessionStep Cancel(int id) => new SessionStep { Action = SessionAction.CancelBall, DeliveryId = id };
        public static SessionStep Ended => new SessionStep { Action = SessionAction.SessionEnded };
    }

    /// <summary>
    /// The session flow shared by both modes (REQ-011..014, REQ-026; plan.md §11, §16). Pure C#: the runtime calls
    /// <see cref="Tick"/> from Update and reports launches and results; the returned <see cref="SessionStep"/> says
    /// what to do. No coroutines or delayed callbacks exist, so restart, menu and pause can never leave stale work;
    /// <see cref="Generation"/> changes whenever outstanding work is invalidated.
    /// Rules: a ball is requested only after the previous one is finalized and, by default, the batter has pressed
    /// ready (otherwise after the automatic countdown); the ball budget is never exceeded; a result is accepted once, only for the active delivery; pausing during a delivery
    /// cancels it without consuming the ball.
    /// </summary>
    public sealed class SessionStateMachine
    {
        readonly List<BallRecord> _records = new List<BallRecord>();
        SessionPhase _pausedFrom;

        public SessionTimings Timings;
        /// <summary>Extra end rule (Match Simulation: won or out of wickets). Checked after each accepted result.</summary>
        public Func<IReadOnlyList<BallRecord>, bool> EndEarly;

        public SessionPhase Phase { get; private set; } = SessionPhase.Menu;
        public SessionOptions Options { get; private set; }
        public int Generation { get; private set; }
        /// <summary>Remaining time in the current timed phase (countdown, between balls), seconds.</summary>
        public float TimeLeft { get; private set; }
        /// <summary>Index (0-based) of the ball being, or about to be, bowled.</summary>
        public int NextBall => _records.Count;
        /// <summary>The delivery id reported for the ball in flight, or -1.</summary>
        public int ActiveDeliveryId { get; private set; } = -1;
        public IReadOnlyList<BallRecord> Records => _records;
        public bool IsOver => _records.Count >= Options.BallBudget || (EndEarly != null && _records.Count > 0 && EndEarly(_records));

        public SessionStateMachine(SessionTimings timings) { Timings = timings; }

        /// <summary>Starts a new session (from the menu or the end screen).</summary>
        public SessionStep Start(SessionOptions options)
        {
            if (options.BallBudget <= 0)
                throw new ArgumentException("A session needs at least one ball.", nameof(options));
            var step = CancelActive();
            Options = options;
            _records.Clear();
            EnterBeforeBall(Timings.firstCountdown);
            return step;
        }

        /// <summary>"Play again": the same mode and options from the beginning.</summary>
        public SessionStep PlayAgain() => Options.BallBudget <= 0 ? SessionStep.None : Start(Options);

        /// <summary>Back to the start menu, cancelling anything in progress.</summary>
        public SessionStep ToMenu()
        {
            var step = CancelActive();
            _records.Clear();
            Enter(SessionPhase.Menu, 0f);
            return step;
        }

        public SessionStep Tick(float deltaTime)
        {
            if (Phase != SessionPhase.Countdown && Phase != SessionPhase.BetweenBalls)
                return SessionStep.None;
            TimeLeft -= deltaTime;
            if (TimeLeft > 0f)
                return SessionStep.None;

            if (Phase == SessionPhase.Countdown)
            {
                Enter(SessionPhase.Delivering, 0f);
                return SessionStep.Launch(NextBall);
            }
            // BetweenBalls finished.
            if (IsOver)
            {
                Enter(SessionPhase.SessionEnd, 0f);
                return SessionStep.Ended;
            }
            EnterBeforeBall(Timings.countdown);
            return SessionStep.None;
        }

        /// <summary>
        /// The batter is ready (tap B). Accepted while waiting, or while the last result is still shown (the batter
        /// may skip the display), but never after the last ball or during a delivery. Returns true if accepted.
        /// </summary>
        public bool Ready()
        {
            if (Phase != SessionPhase.AwaitingReady && !(Phase == SessionPhase.BetweenBalls && Timings.waitForReady))
                return false;
            if (IsOver)
                return false;
            Enter(SessionPhase.Countdown, Timings.readyDelay);
            return true;
        }

        /// <summary>The runtime bowled the requested ball as <paramref name="deliveryId"/> (0 = the request failed).</summary>
        public void BallLaunched(int deliveryId)
        {
            if (Phase != SessionPhase.Delivering || ActiveDeliveryId != -1)
                return;
            if (deliveryId <= 0)
            {
                // The launcher rejected the request: the same ball is requested again (it is not consumed).
                EnterBeforeBall(Timings.countdown);
                return;
            }
            ActiveDeliveryId = deliveryId;
        }

        /// <summary>
        /// The single result of the active delivery. Returns false (and changes nothing) for a stale or duplicate
        /// result, a result outside a delivery, or one beyond the budget.
        /// </summary>
        public bool BallFinalized(BallRecord record)
        {
            if (record == null || Phase != SessionPhase.Delivering || ActiveDeliveryId == -1 ||
                record.DeliveryId != ActiveDeliveryId || _records.Count >= Options.BallBudget)
                return false;
            record.BallIndex = _records.Count;
            _records.Add(record);
            ActiveDeliveryId = -1;
            Enter(SessionPhase.BetweenBalls, Timings.betweenBalls);
            return true;
        }

        /// <summary>Focus or tracking lost. A delivery in progress is cancelled and will be bowled again.</summary>
        public SessionStep Pause()
        {
            if (Phase != SessionPhase.Countdown && Phase != SessionPhase.Delivering && Phase != SessionPhase.BetweenBalls &&
                Phase != SessionPhase.AwaitingReady)
                return SessionStep.None;
            _pausedFrom = Phase;
            var step = CancelActive();
            Enter(SessionPhase.Paused, 0f);
            return step;
        }

        /// <summary>The player confirmed readiness after a pause.</summary>
        public SessionStep Resume()
        {
            if (Phase != SessionPhase.Paused)
                return SessionStep.None;
            if (_pausedFrom == SessionPhase.BetweenBalls && IsOver)
            {
                Enter(SessionPhase.SessionEnd, 0f);
                return SessionStep.Ended;
            }
            EnterBeforeBall(Timings.countdown);
            return SessionStep.None;
        }

        /// <summary>Wait for the ready press, or count down automatically.</summary>
        void EnterBeforeBall(float automaticCountdown)
        {
            if (Timings.waitForReady) Enter(SessionPhase.AwaitingReady, 0f);
            else Enter(SessionPhase.Countdown, automaticCountdown);
        }

        SessionStep CancelActive()
        {
            int id = ActiveDeliveryId;
            ActiveDeliveryId = -1;
            return id > 0 ? SessionStep.Cancel(id) : SessionStep.None;
        }

        void Enter(SessionPhase phase, float time)
        {
            Phase = phase;
            TimeLeft = time;
            Generation++;
        }
    }
}
