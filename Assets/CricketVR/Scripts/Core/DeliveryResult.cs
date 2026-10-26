using UnityEngine;

namespace CricketVR
{
    public enum DeliveryOutcome
    {
        /// <summary>Net Practice: bat contact, not bowled. Scores no runs (REQ-009 amended); the zone says where it went.</summary>
        Hit,
        /// <summary>No bat contact and not bowled.</summary>
        Miss,
        /// <summary>Stumps hit, with or without a bat contact first. Always 0 runs.</summary>
        Bowled,
        /// <summary>GroundMode: bat contact; runs await the virtual-field scorer (plan.md §15).</summary>
        ScoringPending,
    }

    /// <summary>The event that finalized a delivery.</summary>
    public enum DeliveryEnd
    {
        StumpsHit,
        NetHit,
        /// <summary>NetMode: went over a net wall.</summary>
        LeftEnclosure,
        Settled,
        Timeout,
        /// <summary>GroundMode: an unhit ball passed 1 m behind the stumps.</summary>
        PassedStumps,
        /// <summary>GroundMode: crossed the boundary rope (or the safety despawn fired).</summary>
        LeftGround,
    }

    public enum CarryState
    {
        /// <summary>No bat contact, so there is no carry.</summary>
        NotHit,
        /// <summary>Real first landing after contact, before any net.</summary>
        Measured,
        /// <summary>The ball met a net before landing: carry not measurable.</summary>
        NetFirst,
        /// <summary>Hit, but finalized before any landing (cleared the net, bowled on the full, timed out).</summary>
        NoLanding,
    }

    /// <summary>Shot data measured at the bat contact.</summary>
    public struct ShotData
    {
        public Vector3 ContactPoint;
        public Vector3 ExitVelocity;
        public float ContactHeightFromToe;

        public float ExitSpeed => ExitVelocity.magnitude;
        public float LaunchDegrees => Mathf.Atan2(ExitVelocity.y, new Vector2(ExitVelocity.x, ExitVelocity.z).magnitude) * Mathf.Rad2Deg;
        /// <summary>0 = straight back past the bowler (+Z), positive toward +X.</summary>
        public float DirectionDegrees => Mathf.Atan2(ExitVelocity.x, ExitVelocity.z) * Mathf.Rad2Deg;
    }

    /// <summary>The final, immutable record of one playable delivery (REQ-008).</summary>
    public readonly struct DeliveryResult
    {
        public readonly int DeliveryId;
        public readonly DeliveryOutcome Outcome;
        public readonly DeliveryEnd End;
        /// <summary>Net Practice: where a hit ball went (no runs).</summary>
        public readonly ScoringZone Zone;
        public readonly bool BatContact;
        public readonly bool Bowled;
        public readonly CarryState Carry;
        /// <summary>Measured carry in metres; only meaningful when <see cref="Carry"/> is Measured.</summary>
        public readonly float CarryMetres;
        public readonly ShotData Shot;
        public readonly DeliveryConfig Config;

        public DeliveryResult(int deliveryId, DeliveryOutcome outcome, DeliveryEnd end, ScoringZone zone,
            bool batContact, bool bowled, CarryState carry, float carryMetres, ShotData shot, DeliveryConfig config)
        {
            DeliveryId = deliveryId;
            Outcome = outcome;
            End = end;
            Zone = zone;
            BatContact = batContact;
            Bowled = bowled;
            Carry = carry;
            CarryMetres = carryMetres;
            Shot = shot;
            Config = config;
        }

        public bool Miss => Outcome == DeliveryOutcome.Miss;

        /// <summary>Honest carry label (REQ-010): a distance only when a real landing was measured.</summary>
        public string CarryLabel
        {
            get
            {
                switch (Carry)
                {
                    case CarryState.Measured: return $"Measured carry {CarryMetres:F1} m";
                    case CarryState.NetFirst: return "Carry: net hit";
                    case CarryState.NoLanding: return "Carry: N/A";
                    default: return "Carry: -";
                }
            }
        }

        public string Summary
        {
            get
            {
                switch (Outcome)
                {
                    case DeliveryOutcome.Bowled: return BatContact ? "BOWLED (played on)" : "BOWLED";
                    case DeliveryOutcome.Miss: return "Miss";
                    case DeliveryOutcome.ScoringPending: return "Hit, practice scoring pending";
                    default: return Zone == ScoringZone.None ? "Hit" : $"Hit, {PracticeScoring.ZoneLabel(Zone)}";
                }
            }
        }

        public override string ToString() =>
            $"#{DeliveryId} {Summary} | end {End}, zone {Zone}, contact {BatContact}, bowled {Bowled} | {CarryLabel}" +
            (BatContact ? $" | exit {Shot.ExitSpeed:F1} m/s, launch {Shot.LaunchDegrees:F0} deg, direction {Shot.DirectionDegrees:F0} deg" : "");
    }
}
