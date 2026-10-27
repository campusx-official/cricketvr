using System;
using System.Collections.Generic;
using UnityEngine;

namespace CricketVR
{
    public enum DeliveryKind { Stock, Inswinger, Outswinger, OffSpin, LegSpin, Doosra, Googly }
    public enum BowlingStyle { Pace, Spin }

    /// <summary>What a request's line means (D-1).</summary>
    public enum LineReference
    {
        /// <summary>x where the ball pitches (exact).</summary>
        AtBounce,
        /// <summary>x where the ball reaches the striker's popping crease (about ±3 cm, T079).</summary>
        AtBatter,
    }

    /// <summary>
    /// Settings of one delivery type (REQ-041, plan.md §19). Directions are for a right-handed batter (off side = +X;
    /// deliveries travel −Z); <see cref="DeliveryRequest.ForBatter"/> mirrors them for a left-hander. Behaviour comes
    /// only from these numbers: no code outside the delivery-type module branches on <see cref="kind"/>.
    /// Movement settings are given at movement 0 and movement 1 of a request.
    /// </summary>
    [Serializable]
    public struct DeliveryTypeData
    {
        public DeliveryKind kind;
        public string displayName;
        [TextArea] public string description;
        public BowlingStyle style;
        [Tooltip("Release speed range (km/h); 0..0 = use the delivery plan's range. Pace types are clamped by the plan's pace cap.")]
        public float minSpeedKmh, maxSpeedKmh;

        [Header("Swing (before the bounce)")]
        [Tooltip("-1 = inswing (toward −X, into a right-hander), +1 = outswing (toward +X), 0 = none.")]
        public int swingDirection;
        [Tooltip("Swing (m) at movement 0 and 1: how far the ball is, at the striker's popping crease, from the straight line it was released along. At most 0.5 m (MaxSwing).")]
        public float swingMin, swingMax;
        [Tooltip("How late the swing comes: 0 = uniform, 2 = late.")]
        public float swingLateness;

        [Header("Spin")]
        [Tooltip("Turn at the bounce: -1 = toward −X (off-spin, googly), +1 = toward +X (leg-spin, doosra), 0 = none. Drift is the opposite way.")]
        public int turnDirection;
        [Tooltip("Air drift by the bounce (m) at movement 0 and 1.")]
        public float driftMin, driftMax;
        [Tooltip("Extra downward acceleration before the bounce (m/s²).")]
        public float dip;
        [Tooltip("Turn at the bounce (degrees) at movement 0 and 1.")]
        public float turnMin, turnMax;
        [Tooltip("Bounce-height factor: vertical speed after the bounce × (1 + b).")]
        public float bounceFactor;
        [Tooltip("Share of horizontal speed lost to grip at the bounce.")]
        public float grip;
        [Tooltip("Seam movement after the bounce (degrees); 0 in v1 (D-2).")]
        public float seamDegrees;

        [Header("Look")]
        [Tooltip("Visual rotation rate (rev/s); capped at 5 so the seam doesn't strobe at 72 Hz (D-4).")]
        public float visualRevPerSecond;
        [Tooltip("Type whose rotation the ball shows until the bounce (deception, D-3).")]
        public DeliveryKind looksLike;

        [Header("Natural variation")]
        [Tooltip("Each ball's swing, drift and turn are scaled by a seeded factor in [1 − v, 1 + v] (REQ-048).")]
        public float variation;

        /// <summary>No swing, spin, dip or bounce change: the type bowls exactly like the pre-M7.5 machine.</summary>
        public bool IsStill => swingDirection == 0 && turnDirection == 0 && dip == 0f && bounceFactor == 0f &&
                               grip == 0f && seamDegrees == 0f;

        public bool UsesPlanSpeed => maxSpeedKmh <= 0f;

        public override string ToString() => displayName;

        // ---------- Allowed ranges (REQ-041) ----------

        public const float MaxVisualRevPerSecond = 5f;
        /// <summary>
        /// Largest swing (m) any ball gets, after natural variation (owner decision 2026-10-27, plan.md open decision 4).
        /// Going beyond it needs an owner-approved "exaggerated" setting, which does not exist yet.
        /// </summary>
        public const float MaxSwing = 0.5f;

        struct Limit
        {
            public string Name;
            public Func<DeliveryTypeData, float> Get;
            public float Min, Max;
        }

        static readonly Limit[] Limits =
        {
            L("minSpeedKmh", d => d.minSpeedKmh, 0f, 150f),
            L("maxSpeedKmh", d => d.maxSpeedKmh, 0f, 150f),
            L("swingDirection", d => d.swingDirection, -1f, 1f),
            L("swingMin", d => d.swingMin, 0f, MaxSwing),
            L("swingMax", d => d.swingMax, 0f, MaxSwing),
            L("swingLateness", d => d.swingLateness, 0f, 3f),
            L("turnDirection", d => d.turnDirection, -1f, 1f),
            L("driftMin", d => d.driftMin, 0f, 0.3f),
            L("driftMax", d => d.driftMax, 0f, 0.3f),
            L("dip", d => d.dip, 0f, 3f),
            L("turnMin", d => d.turnMin, 0f, 15f),
            L("turnMax", d => d.turnMax, 0f, 15f),
            L("bounceFactor", d => d.bounceFactor, -0.5f, 0.3f),
            L("grip", d => d.grip, 0f, 0.2f),
            L("seamDegrees", d => d.seamDegrees, -5f, 5f),
            L("visualRevPerSecond", d => d.visualRevPerSecond, 0f, MaxVisualRevPerSecond),
            L("variation", d => d.variation, 0f, 0.5f),
        };

        static Limit L(string name, Func<DeliveryTypeData, float> get, float min, float max) =>
            new Limit { Name = name, Get = get, Min = min, Max = max };

        /// <summary>Every out-of-range or inconsistent value, as readable messages (empty when valid).</summary>
        public List<string> Validate()
        {
            var errors = new List<string>();
            foreach (var l in Limits)
            {
                float v = l.Get(this);
                if (!(v >= l.Min && v <= l.Max))
                    errors.Add($"{displayName}: {l.Name} = {v} is outside {l.Min}..{l.Max}");
            }
            if (minSpeedKmh > maxSpeedKmh) errors.Add($"{displayName}: minSpeedKmh > maxSpeedKmh");
            if (!UsesPlanSpeed && minSpeedKmh < 40f) errors.Add($"{displayName}: speeds below 40 km/h are not bowled");
            if (swingMin > swingMax) errors.Add($"{displayName}: swingMin > swingMax");
            if (driftMin > driftMax) errors.Add($"{displayName}: driftMin > driftMax");
            if (turnMin > turnMax) errors.Add($"{displayName}: turnMin > turnMax");
            if ((swingDirection == 0) != (swingMax == 0f)) errors.Add($"{displayName}: swing amount without a direction (or the reverse)");
            if ((turnDirection == 0) != (turnMax == 0f && driftMax == 0f)) errors.Add($"{displayName}: turn or drift without a direction (or the reverse)");
            if (string.IsNullOrEmpty(displayName)) errors.Add($"{kind}: no display name");
            return errors;
        }

        // ---------- Starting values (plan.md §19 table; calibrated in T083) ----------

        public static DeliveryTypeData Stock => new DeliveryTypeData
        {
            kind = DeliveryKind.Stock, displayName = "Stock", style = BowlingStyle.Pace, looksLike = DeliveryKind.Stock,
            description = "A straight ball: no swing or spin. Bowls exactly like the machine before delivery types.",
            visualRevPerSecond = 3f,
        };

        public static DeliveryTypeData Inswinger => Swing(DeliveryKind.Inswinger, "Inswinger", -1,
            "Curves in toward a right-hander's pads (toward leg) before pitching.");

        public static DeliveryTypeData Outswinger => Swing(DeliveryKind.Outswinger, "Outswinger", +1,
            "Curves away from a right-hander toward the slips before pitching.");

        public static DeliveryTypeData OffSpin => Spin(DeliveryKind.OffSpin, "Off-spin", -1, 80f, 92f, 0.05f, 0.10f, 0.6f, 3f, 6f, -0.21f, 0.05f,
            DeliveryKind.OffSpin, "Drifts away, then turns in from off to leg (toward a right-hander) after pitching.");

        public static DeliveryTypeData LegSpin => Spin(DeliveryKind.LegSpin, "Leg-spin", +1, 75f, 90f, 0.06f, 0.12f, 0.8f, 4f, 9f, -0.19f, 0.06f,
            DeliveryKind.LegSpin, "Drifts in, then turns away from leg to off after pitching.");

        public static DeliveryTypeData Doosra => Spin(DeliveryKind.Doosra, "Doosra", +1, 80f, 92f, 0.05f, 0.10f, 0.6f, 2.5f, 5f, -0.23f, 0.05f,
            DeliveryKind.OffSpin, "The off-spinner's variation: looks like off-spin but turns the other way, away from a right-hander.");

        public static DeliveryTypeData Googly => Spin(DeliveryKind.Googly, "Googly", -1, 75f, 90f, 0.06f, 0.12f, 1.2f, 3.5f, 7.5f, -0.16f, 0.06f,
            DeliveryKind.LegSpin, "The leg-spinner's variation: looks like leg-spin but turns in, dips more and bounces higher.");

        /// <summary>The seven starting types, in <see cref="DeliveryKind"/> order.</summary>
        public static DeliveryTypeData[] All => new[] { Stock, Inswinger, Outswinger, OffSpin, LegSpin, Doosra, Googly };

        static DeliveryTypeData Swing(DeliveryKind kind, string name, int direction, string description) => new DeliveryTypeData
        {
            kind = kind, displayName = name, description = description, style = BowlingStyle.Pace, looksLike = kind,
            minSpeedKmh = 115f, maxSpeedKmh = 140f,
            swingDirection = direction, swingMin = 0.1f, swingMax = 0.5f, swingLateness = 1.5f,
            visualRevPerSecond = 3f, variation = 0.15f,
        };

        static DeliveryTypeData Spin(DeliveryKind kind, string name, int turn, float minKmh, float maxKmh, float driftMin, float driftMax,
            float dip, float turnMin, float turnMax, float bounce, float grip, DeliveryKind looksLike, string description) => new DeliveryTypeData
        {
            kind = kind, displayName = name, description = description, style = BowlingStyle.Spin, looksLike = looksLike,
            minSpeedKmh = minKmh, maxSpeedKmh = maxKmh,
            turnDirection = turn, driftMin = driftMin, driftMax = driftMax, dip = dip, turnMin = turnMin, turnMax = turnMax,
            bounceFactor = bounce, grip = grip, visualRevPerSecond = 4f, variation = 0.15f,
        };
    }

    /// <summary>
    /// The only way to ask for a ball (REQ-040). Line is in the batter's frame (+ = off side); the request's
    /// handedness says how to mirror it and its movement into the world, done once by <see cref="ForBatter"/>.
    /// A Stock request with movement 0 converts both ways with <see cref="DeliveryConfig"/>, so pre-M7.5 callers and
    /// tests keep working.
    /// </summary>
    [Serializable]
    public struct DeliveryRequest
    {
        public string label;
        public DeliveryTypeData type;
        [Tooltip("Release speed in km/h.")]
        public float speedKmh;
        [Tooltip("Ball centre at release (world, metres).")]
        public Vector3 releasePosition;
        [Tooltip("Batter frame (m): 0 = middle stump, + = off side. See lineReference.")]
        public float line;
        [Tooltip("Bounce distance in front of the striker's stumps (m).")]
        public float length;
        public LineReference lineReference;
        [Range(0f, 1f)] public float movement;
        public Handedness handedness;
        [Tooltip("Seeds this ball's natural variation (REQ-048).")]
        public int seed;
        [Tooltip("Set by ForBatter: line and movement directions are in world coordinates.")]
        public bool worldFrame;

        public static DeliveryRequest FromConfig(DeliveryConfig config) => new DeliveryRequest
        {
            label = config.label,
            type = DeliveryTypeData.Stock,
            speedKmh = config.speedKmh,
            releasePosition = config.releasePosition,
            line = config.line,
            length = config.length,
        };

        /// <summary>The launch fields as a <see cref="DeliveryConfig"/> (line as stored: call on a ForBatter result for the world line).</summary>
        public DeliveryConfig ToConfig() => new DeliveryConfig
        {
            label = label,
            speedKmh = speedKmh,
            releasePosition = releasePosition,
            line = line,
            length = length,
        };

        /// <summary>
        /// World-frame copy for the request's batter: a left-hander gets x mirrored and every movement direction
        /// reversed (REQ-047). The only mirroring in the game (DT-7); applying it twice changes nothing.
        /// </summary>
        public DeliveryRequest ForBatter()
        {
            if (worldFrame)
                return this;
            var r = this;
            r.worldFrame = true;
            if (handedness == Handedness.Left)
            {
                r.line = -line;
                r.type.swingDirection = -type.swingDirection;
                r.type.turnDirection = -type.turnDirection;
                r.type.seamDegrees = -type.seamDegrees;
            }
            return r;
        }

        public override string ToString() =>
            type.IsStill && movement == 0f ? ToConfig().ToString()
                : $"{label} [{type.displayName}, movement {movement:F2}, {handedness}-handed]: {speedKmh:F0} km/h, line {line:+0.00;-0.00;0.00} m, length {length:F2} m";
    }

    /// <summary>
    /// A Net Practice bowler choice (owner request 2026-10-27; an early part of the M8 delivery-mix menu, REQ-029):
    /// its menu button and the type mix it bowls for the whole session. The menu and the session only list these, so
    /// nothing outside the module branches on a type.
    /// </summary>
    public struct NetBowler
    {
        /// <summary>Menu button name.</summary>
        public string Button;
        /// <summary>Button text, and the name on the menu board.</summary>
        public string Label;
        /// <summary>Empty = Stock only (the pace bowler, unchanged).</summary>
        public TypeWeight[] Mix;

        public static readonly NetBowler[] All =
        {
            new NetBowler { Button = "BowlerPace", Label = "Pace", Mix = new TypeWeight[0] },
            new NetBowler { Button = "BowlerOffSpin", Label = "Off-spin", Mix = new[] { new TypeWeight { kind = DeliveryKind.OffSpin, weight = 1f } } },
            new NetBowler { Button = "BowlerLegSpin", Label = "Leg-spin", Mix = new[] { new TypeWeight { kind = DeliveryKind.LegSpin, weight = 1f } } },
        };

        /// <summary>The Net Practice plan with this bowler: same lines and lengths; a spinner's speed comes from its type.</summary>
        public DeliveryPlanSettings Plan()
        {
            var plan = DeliveryPlanSettings.NetPractice;
            plan.typeMix = Mix != null && Mix.Length > 0 ? Mix : null;
            return plan;
        }
    }
}
