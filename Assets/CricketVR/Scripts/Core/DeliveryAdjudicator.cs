using System.Collections.Generic;
using UnityEngine;

namespace CricketVR
{
    public enum BallEventKind
    {
        Ground,
        Stumps,
        Net,
        LeftEnclosure,
        /// <summary>GroundMode: the ball's centre passed z &lt; -1 m (behind the stumps).</summary>
        PassedStumps,
        /// <summary>GroundMode: the ball crossed the rope or was safety-despawned.</summary>
        LeftGround,
    }

    /// <summary>Which terminal rules apply (plan.md §6 for nets, §15 for the ground).</summary>
    public enum DeliveryRules { Nets, Ground }

    /// <summary>Something the ball touched or did during one physics step.</summary>
    public struct BallEvent
    {
        public BallEventKind Kind;
        public WallSide Wall;   // Net and LeftEnclosure only
        public Vector3 Point;

        public static BallEvent Ground(Vector3 p) => new BallEvent { Kind = BallEventKind.Ground, Point = p };
        public static BallEvent Stumps(Vector3 p) => new BallEvent { Kind = BallEventKind.Stumps, Point = p };
        public static BallEvent Net(WallSide wall, Vector3 p) => new BallEvent { Kind = BallEventKind.Net, Wall = wall, Point = p };
        public static BallEvent Exit(WallSide wall, Vector3 p) => new BallEvent { Kind = BallEventKind.LeftEnclosure, Wall = wall, Point = p };
        public static BallEvent PassedStumps(Vector3 p) => new BallEvent { Kind = BallEventKind.PassedStumps, Point = p };
        public static BallEvent LeftGround(Vector3 p) => new BallEvent { Kind = BallEventKind.LeftGround, Point = p };
    }

    /// <summary>
    /// Decides the result of one delivery from its events, in time order, exactly once (plan.md §6, REQ-008..010).
    /// The caller feeds it each physics step's events after the step and before that step's successor applies any
    /// bat contact, so "contact happened before this event" is simply "contact was recorded before this call".
    /// Events for another delivery id, or after finalization, are ignored.
    ///
    /// Ground rules (no nets): stumps -> Bowled; an UNHIT ball passing behind the stumps, leaving the ground,
    /// settling or timing out -> Miss; a HIT ball (edges behind the wicket included) leaving the ground, settling
    /// or timing out -> ScoringPending. A hit ball is never a Miss. Same-step priority: stumps first.
    /// </summary>
    public sealed class DeliveryAdjudicator
    {
        public ScoringRules Rules;
        public DeliveryRules Mode;

        int _id;
        bool _open;
        DeliveryConfig _config;
        bool _contact;
        ShotData _shot;
        bool _carryDecided;
        CarryState _carry;
        float _carryMetres;

        public DeliveryAdjudicator(ScoringRules rules, DeliveryRules mode = DeliveryRules.Nets)
        {
            Rules = rules;
            Mode = mode;
        }

        public int DeliveryId => _id;
        /// <summary>A delivery has begun and is not yet finalized or cancelled.</summary>
        public bool IsOpen => _open;
        public bool HasContact => _contact;

        public void Begin(int deliveryId, DeliveryConfig config)
        {
            _id = deliveryId;
            _open = true;
            _config = config;
            _contact = false;
            _shot = default;
            _carryDecided = false;
            _carry = CarryState.NotHit;
            _carryMetres = 0f;
        }

        /// <summary>Discards the open delivery without a result (relaunch, restart, interruption).</summary>
        public void Cancel(int deliveryId)
        {
            if (_open && deliveryId == _id)
                _open = false;
        }

        /// <summary>Records the first bat contact. Later contacts, stale ids and finalized deliveries are ignored.</summary>
        public bool RecordBatContact(int deliveryId, ShotData shot)
        {
            if (!_open || deliveryId != _id || _contact)
                return false;
            _contact = true;
            _shot = shot;
            return true;
        }

        /// <summary>
        /// Processes everything that happened to the ball in one physics step. Returns true (with the result) if a
        /// terminal event finalized the delivery. Several terminal events in one step resolve by priority:
        /// stumps, then <see cref="ScoringZone"/> order (front high .. over the back net).
        /// </summary>
        public bool ProcessStep(int deliveryId, IReadOnlyList<BallEvent> events, out DeliveryResult result)
        {
            result = default;
            if (!_open || deliveryId != _id || events == null || events.Count == 0)
                return false;

            // Measured carry: the first landing after contact, unless a net came first (or in the same step).
            if (_contact && !_carryDecided)
            {
                bool net = false, ground = false;
                Vector3 landing = default;
                foreach (var e in events)
                {
                    if (e.Kind == BallEventKind.Net) net = true;
                    else if (e.Kind == BallEventKind.Ground && !ground) { ground = true; landing = e.Point; }
                }
                if (net)
                {
                    _carryDecided = true;
                    _carry = CarryState.NetFirst;
                }
                else if (ground)
                {
                    _carryDecided = true;
                    _carry = CarryState.Measured;
                    var d = landing - _shot.ContactPoint;
                    _carryMetres = new Vector2(d.x, d.z).magnitude;
                }
            }

            bool found = false;
            int bestRank = int.MaxValue;
            DeliveryEnd end = default;
            ScoringZone zone = ScoringZone.None;
            foreach (var e in events)
            {
                int rank;
                ScoringZone z;
                DeliveryEnd en;
                switch (e.Kind)
                {
                    case BallEventKind.Stumps:
                        rank = 0; z = ScoringZone.None; en = DeliveryEnd.StumpsHit;
                        break;
                    case BallEventKind.PassedStumps when Mode == DeliveryRules.Ground && !_contact:
                        rank = 1; z = ScoringZone.None; en = DeliveryEnd.PassedStumps;
                        break;
                    case BallEventKind.LeftGround when Mode == DeliveryRules.Ground:
                        rank = 2; z = ScoringZone.None; en = DeliveryEnd.LeftGround;
                        break;
                    case BallEventKind.Net when Mode == DeliveryRules.Nets:
                        z = PracticeScoring.NetZone(e.Wall, e.Point.y, Rules); rank = (int)z; en = DeliveryEnd.NetHit;
                        break;
                    case BallEventKind.LeftEnclosure when Mode == DeliveryRules.Nets:
                        z = PracticeScoring.ExitZone(e.Wall); rank = (int)z; en = DeliveryEnd.LeftEnclosure;
                        break;
                    default:
                        continue;
                }
                if (rank < bestRank)
                {
                    bestRank = rank;
                    found = true;
                    end = en;
                    zone = z;
                }
            }
            if (!found)
                return false;
            result = Finalize(end, zone);
            return true;
        }

        /// <summary>Finalizes on settle or timeout (no terminal event reached). Returns false if already finalized or stale.</summary>
        public bool EndWithoutTerminal(int deliveryId, DeliveryEnd reason, out DeliveryResult result)
        {
            result = default;
            if (!_open || deliveryId != _id)
                return false;
            result = Finalize(reason, ScoringZone.None);
            return true;
        }

        DeliveryResult Finalize(DeliveryEnd end, ScoringZone zone)
        {
            _open = false;
            bool bowled = end == DeliveryEnd.StumpsHit;
            int runs = _contact && !bowled ? PracticeScoring.Runs(zone, Rules) : 0;
            DeliveryOutcome outcome = bowled ? DeliveryOutcome.Bowled
                : !_contact ? DeliveryOutcome.Miss
                : Mode == DeliveryRules.Ground ? DeliveryOutcome.ScoringPending
                : runs > 0 ? DeliveryOutcome.Scored : DeliveryOutcome.NoRuns;
            var carry = !_contact ? CarryState.NotHit : _carryDecided ? _carry : CarryState.NoLanding;
            return new DeliveryResult(_id, outcome, end, zone, runs, _contact, bowled, carry,
                carry == CarryState.Measured ? _carryMetres : 0f, _contact ? _shot : default, _config);
        }
    }
}
