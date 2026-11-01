using System;
using System.Collections.Generic;
using UnityEngine;

namespace CricketVR
{
    public enum DeliveryKind { Stock, Inswinger, Outswinger, OffSpin, LegSpin, Doosra, Googly, ArmBall }
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
        [Tooltip("Extra downward acceleration before the bounce (m/s²) at the type's fastest speed (the flattest ball).")]
        public float dip;
        [Tooltip("Dip (m/s²) at the type's slowest speed: the most tossed-up ball. Each ball's dip goes from this to dip as its speed rises, so flight varies with pace; 0 = dip at every speed.")]
        public float flightDip;
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

        [Header("Flighted variation (spin)")]
        [Tooltip("Share of this type's balls bowled as a flighted delivery: slower and tossed up (owner request 2026-10-27). 0 = none.")]
        public float flightedShare;
        [Tooltip("Speed range of a flighted ball (km/h).")]
        public float flightedMinKmh, flightedMaxKmh;
        [Tooltip("Launch angle above horizontal of a flighted ball (degrees); the solver finds the dip that lands it on its length.")]
        public float flightedMinLaunch, flightedMaxLaunch;

        public bool HasFlighted => flightedShare > 0f && flightedMaxLaunch > 0f;

        /// <summary>No swing, spin, dip or bounce change: the type bowls exactly like the pre-M7.5 machine.</summary>
        public bool IsStill => swingDirection == 0 && turnDirection == 0 && dip == 0f && flightDip == 0f && bounceFactor == 0f &&
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
            L("flightDip", d => d.flightDip, 0f, 8f),
            L("turnMin", d => d.turnMin, 0f, 15f),
            L("turnMax", d => d.turnMax, 0f, 15f),
            L("bounceFactor", d => d.bounceFactor, -0.5f, 0.3f),
            L("grip", d => d.grip, 0f, 0.4f),
            L("seamDegrees", d => d.seamDegrees, -5f, 5f),
            L("visualRevPerSecond", d => d.visualRevPerSecond, 0f, MaxVisualRevPerSecond),
            L("variation", d => d.variation, 0f, 0.5f),
            L("flightedShare", d => d.flightedShare, 0f, 1f),
            L("flightedMinKmh", d => d.flightedMinKmh, 0f, 150f),
            L("flightedMaxKmh", d => d.flightedMaxKmh, 0f, 150f),
            L("flightedMinLaunch", d => d.flightedMinLaunch, 0f, 15f),
            L("flightedMaxLaunch", d => d.flightedMaxLaunch, 0f, 15f),
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
            if (HasFlighted && (flightedMinKmh > flightedMaxKmh || flightedMinKmh < 40f)) errors.Add($"{displayName}: flighted speed range invalid");
            if (HasFlighted && flightedMinLaunch > flightedMaxLaunch) errors.Add($"{displayName}: flightedMinLaunch > flightedMaxLaunch");
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

        public static DeliveryTypeData OffSpin => Flighted(Spin(DeliveryKind.OffSpin, "Off-spin", -1, 75f, 92f, 0.05f, 0.10f, 1.0f, 5.0f, 3f, 6f, -0.21f, 0.10f,
            DeliveryKind.OffSpin, "Drifts away, then turns in from off to leg (toward a right-hander) after pitching."),
            0.25f, 70f, 80f, 4f, 10f);

        public static DeliveryTypeData LegSpin => Spin(DeliveryKind.LegSpin, "Leg-spin", +1, 72f, 90f, 0.06f, 0.12f, 1.2f, 5.5f, 4f, 9f, -0.19f, 0.12f,
            DeliveryKind.LegSpin, "Drifts in, then turns away from leg to off after pitching.");

        public static DeliveryTypeData Doosra => Spin(DeliveryKind.Doosra, "Doosra", +1, 75f, 92f, 0.05f, 0.10f, 1.0f, 5.0f, 2.5f, 5f, -0.23f, 0.10f,
            DeliveryKind.OffSpin, "The off-spinner's variation: looks like off-spin but turns the other way, away from a right-hander.");

        public static DeliveryTypeData Googly => Spin(DeliveryKind.Googly, "Googly", -1, 72f, 90f, 0.06f, 0.12f, 1.8f, 6.0f, 3.5f, 7.5f, -0.14f, 0.12f,
            DeliveryKind.LegSpin, "The leg-spinner's variation: looks like leg-spin but turns in, dips more and bounces higher.");

        /// <summary>
        /// The off-spinner's straighter one (arm ball; owner request 2026-10-27): looks like off-spin, comes out a little
        /// quicker and flatter, holds its line or drifts slightly away, barely turns, and skids on low with little grip.
        /// </summary>
        public static DeliveryTypeData ArmBall => Spin(DeliveryKind.ArmBall, "Straighter one", -1, 82f, 95f, 0.02f, 0.05f, 0.5f, 0f, 0f, 0.5f, -0.28f, 0.02f,
            DeliveryKind.OffSpin, "The off-spinner's arm ball: looks like off-spin but goes straight on with the arm, quicker and lower, hurrying onto the batter.");

        /// <summary>The starting types, in <see cref="DeliveryKind"/> order.</summary>
        public static DeliveryTypeData[] All => new[] { Stock, Inswinger, Outswinger, OffSpin, LegSpin, Doosra, Googly, ArmBall };

        static DeliveryTypeData Swing(DeliveryKind kind, string name, int direction, string description) => new DeliveryTypeData
        {
            kind = kind, displayName = name, description = description, style = BowlingStyle.Pace, looksLike = kind,
            minSpeedKmh = 115f, maxSpeedKmh = 140f,
            swingDirection = direction, swingMin = 0.1f, swingMax = 0.5f, swingLateness = 1.5f,
            visualRevPerSecond = 3f, variation = 0.15f,
        };

        /// <summary>Adds the flighted variation (owner request 2026-10-27, off-spin first).</summary>
        static DeliveryTypeData Flighted(DeliveryTypeData t, float share, float minKmh, float maxKmh, float minLaunch, float maxLaunch)
        {
            t.flightedShare = share;
            t.flightedMinKmh = minKmh;
            t.flightedMaxKmh = maxKmh;
            t.flightedMinLaunch = minLaunch;
            t.flightedMaxLaunch = maxLaunch;
            return t;
        }

        static DeliveryTypeData Spin(DeliveryKind kind, string name, int turn, float minKmh, float maxKmh, float driftMin, float driftMax,
            float dip, float flightDip, float turnMin, float turnMax, float bounce, float grip, DeliveryKind looksLike, string description) => new DeliveryTypeData
        {
            kind = kind, displayName = name, description = description, style = BowlingStyle.Spin, looksLike = looksLike,
            minSpeedKmh = minKmh, maxSpeedKmh = maxKmh,
            turnDirection = turn, driftMin = driftMin, driftMax = driftMax, dip = dip, flightDip = flightDip, turnMin = turnMin, turnMax = turnMax,
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
        [Tooltip("A flighted ball (spin): launched launchDegrees above horizontal; the solver finds the dip.")]
        public bool flighted;
        public float launchDegrees;

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
                : $"{label} [{type.displayName}{(flighted ? $", flighted {launchDegrees:F1} deg" : "")}, movement {movement:F2}, {handedness}-handed]: {speedKmh:F0} km/h, line {line:+0.00;-0.00;0.00} m, length {length:F2} m";
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
        /// <summary>A spinner's lengths (m): drawn from the triangle min..peak..max, labelled with the spin good-length
        /// band; 0 = Net Practice's pace lengths.</summary>
        public float MinLength, PeakLength, MaxLength;
        /// <summary>A spinner's line range (batter frame, m) as where the ball reaches the crease (D-1), so it is
        /// pitched where its turn takes it onto the stumps; 0..0 = Net Practice's pitch lines.</summary>
        public float MinFinishLine, MaxFinishLine;

        // Spin lengths (owner feedback 2026-10-27, "almost all the balls are very short"): the pace range 3.5-9 m put
        // half the balls beyond 6 m, short for a spinner. Spin good length is about 4-6 m from the stumps.
        const float SpinMinLength = 2.5f, SpinPeakLength = 4.75f, SpinMaxLength = 7.5f;
        public const float SpinGoodLengthMin = 4f, SpinGoodLengthMax = 6f;

        public static readonly NetBowler[] All =
        {
            new NetBowler { Button = "BowlerPace", Label = "Pace", Mix = new TypeWeight[0] },
            // Finish lines (owner feedback 2026-10-27: pitched on middle, the off-spinner turned down the leg side).
            // Off-spin turns in: finish from leg stump to just outside off. Leg-spin turns away: middle to outside off.
            // The off-spinner's variations (owner request 2026-10-27): 1 ball in 12 a doosra, 1 in 12 the straighter one.
            Spinner("BowlerOffSpin", "Off-spin", -0.10f, 0.25f,
                new TypeWeight { kind = DeliveryKind.OffSpin, weight = 10f },
                new TypeWeight { kind = DeliveryKind.Doosra, weight = 1f },
                new TypeWeight { kind = DeliveryKind.ArmBall, weight = 1f }),
            Spinner("BowlerLegSpin", "Leg-spin", -0.05f, 0.35f, new TypeWeight { kind = DeliveryKind.LegSpin, weight = 1f }),
        };

        static NetBowler Spinner(string button, string label, float minFinish, float maxFinish, params TypeWeight[] mix) => new NetBowler
        {
            Button = button, Label = label, Mix = mix,
            MinLength = SpinMinLength, PeakLength = SpinPeakLength, MaxLength = SpinMaxLength,
            MinFinishLine = minFinish, MaxFinishLine = maxFinish,
        };

        /// <summary>The Net Practice plan with this bowler, released from <paramref name="release"/> (zero = the
        /// centred default): Net Practice lines and lengths for pace; a spinner's own lengths and finish lines; its
        /// speed (and so its flight) comes from its type.</summary>
        public DeliveryPlanSettings Plan(Vector3 release = default)
        {
            var plan = DeliveryPlanSettings.NetPractice;
            plan.releasePosition = release;
            plan.typeMix = Mix != null && Mix.Length > 0 ? Mix : null;
            if (MaxFinishLine > MinFinishLine)
            {
                plan.minLine = MinFinishLine;
                plan.maxLine = MaxFinishLine;
                plan.lineReference = LineReference.AtBatter;
            }
            if (MaxLength > 0f)
            {
                plan.minLength = MinLength;
                plan.lengthPeak = PeakLength;
                plan.maxLength = MaxLength;
                plan.goodLengthMin = SpinGoodLengthMin;
                plan.goodLengthMax = SpinGoodLengthMax;
            }
            return plan;
        }
    }
}
