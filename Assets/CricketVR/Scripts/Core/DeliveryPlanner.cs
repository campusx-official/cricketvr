using System;
using System.Collections.Generic;
using UnityEngine;

namespace CricketVR
{
    /// <summary>One entry of a plan's delivery-type mix.</summary>
    [Serializable]
    public struct TypeWeight
    {
        public DeliveryKind kind;
        public float weight;
    }

    /// <summary>
    /// Where a Net Practice bowler releases from (owner request 2026-10-27): over or around the wicket, as a right-arm
    /// bowler (<see cref="FieldDimensions.OverTheWicketRelease"/>).
    /// </summary>
    public struct WicketSide
    {
        /// <summary>Menu button name.</summary>
        public string Button;
        public string Label;
        public Vector3 Release;

        public static readonly WicketSide[] All =
        {
            new WicketSide { Button = "WicketOver", Label = "Over", Release = FieldDimensions.OverTheWicketRelease },
            new WicketSide { Button = "WicketAround", Label = "Around", Release = FieldDimensions.AroundTheWicketRelease },
        };
    }

    /// <summary>
    /// What a mode or difficulty bowls (REQ-028, spec.md "Two modes"). Lines are given for a right-hander (+ = off
    /// side) and mirrored for a left-hander. Lengths are bounce distances in front of the striker's stumps.
    /// </summary>
    [Serializable]
    public struct DeliveryPlanSettings
    {
        /// <summary>Pace types bowl no faster than this. Raised from 120 to 140 km/h once AC-29 passed at 140 (T075, E-047; DT-5).</summary>
        public const float DefaultPaceCapKmh = 140f;

        public string label;
        public float minSpeedKmh;
        public float maxSpeedKmh;
        [Tooltip("Off-side line range for a right-hander (m); mirrored for a left-hander.")]
        public float minLine;
        public float maxLine;
        [Tooltip("Lengths of a normal ball (m).")]
        public float minLength;
        public float maxLength;
        [Tooltip("Most likely normal length (m): lengths are drawn from a triangle min..peak..max. 0 = uniform (as before).")]
        public float lengthPeak;
        [Tooltip("Good-length band for the ball's label (m); 0..0 = the pace band, 5-7.5 m.")]
        public float goodLengthMin, goodLengthMax;
        [Tooltip("What the line range means: where the ball pitches (default) or where it reaches the batter's crease (D-1).")]
        public LineReference lineReference;
        [Tooltip("Release point (world, m); zero = the machine's default, centred over middle stump.")]
        public Vector3 releasePosition;
        [Range(0f, 1f)] public float yorkerShare;
        public float yorkerMinLength;
        public float yorkerMaxLength;
        [Range(0f, 1f)] public float shortShare;
        public float shortMinLength;
        public float shortMaxLength;
        [Tooltip("Delivery types and their weights (M7.5). Empty = Stock only, as before M7.5.")]
        public TypeWeight[] typeMix;
        [Tooltip("Pace types' speed range is clamped to this (km/h); 0 = the default cap (140 since AC-29, T075).")]
        public float paceCapKmh;
        [Tooltip("Swing for swing types in this plan (m off the release line at the crease, before natural variation); 0..0 = the type's own range.")]
        public float minSwing, maxSwing;

        public float PaceCap => paceCapKmh > 0f ? paceCapKmh : DefaultPaceCapKmh;
        public bool StockOnly => typeMix == null || typeMix.Length == 0;

        public static DeliveryPlanSettings NetPractice => new DeliveryPlanSettings
        {
            label = "Net Practice", minSpeedKmh = 70f, maxSpeedKmh = 90f, minLine = -0.1f, maxLine = 0.4f,
            minLength = 3.5f, maxLength = 9f,
        };


        // Swing per difficulty (owner, 2026-10-27): Easy and Medium about 0.15-0.3 m, Hard up to the 0.5 m cap.
        public static DeliveryPlanSettings Easy => Match("Easy", 70f, 85f, 0.05f, 0.10f, 0.15f, 0.30f);
        public static DeliveryPlanSettings Medium => Match("Medium", 80f, 100f, 0.10f, 0.15f, 0.15f, 0.30f);
        /// <summary>Capped at 120 km/h until AC-04 is re-run for 100-120 km/h (C-14).</summary>
        public static DeliveryPlanSettings Hard => Match("Hard", 95f, 120f, 0.20f, 0.20f, 0.20f, DeliveryTypeData.MaxSwing);

        static DeliveryPlanSettings Match(string label, float min, float max, float yorkers, float shorts, float minSwing, float maxSwing) => new DeliveryPlanSettings
        {
            label = label, minSpeedKmh = min, maxSpeedKmh = max, minLine = -0.1f, maxLine = 0.3f,
            minLength = 4f, maxLength = 7.5f,
            yorkerShare = yorkers, yorkerMinLength = 0.8f, yorkerMaxLength = 1.5f,
            shortShare = shorts, shortMinLength = 8.5f, shortMaxLength = 10f,
            minSwing = minSwing, maxSwing = maxSwing,
        };
    }

    /// <summary>
    /// Draws deliveries from a plan. Every draw is checked with the solver before use; an unreachable draw is
    /// redrawn (never altered), up to <see cref="MaxRedraws"/> times, then the plan's safest delivery is used.
    /// Deterministic for a given System.Random seed. A Stock-only plan draws exactly the sequence it drew before M7.5.
    /// </summary>
    public static class DeliveryPlanner
    {
        public const int MaxRedraws = 10;

        /// <summary>World-frame <see cref="DeliveryConfig"/> draw (Stock-only plans), kept for pre-M7.5 callers.</summary>
        public static DeliveryConfig Draw(in DeliveryPlanSettings plan, System.Random rng, bool leftHanded,
            Func<DeliveryConfig, bool> reachable, out int redraws)
        {
            var request = DrawRequest(plan, rng, leftHanded ? Handedness.Left : Handedness.Right, null,
                reachable == null ? (Func<DeliveryRequest, bool>)null : r => reachable(r.ForBatter().ToConfig()), out redraws);
            return request.ForBatter().ToConfig();
        }

        /// <summary>
        /// Draws one request in the batter's frame (mirrored later by <see cref="DeliveryRequest.ForBatter"/>).
        /// The type comes from the plan's mix, looked up in <paramref name="types"/> (null = the code defaults).
        /// </summary>
        public static DeliveryRequest DrawRequest(in DeliveryPlanSettings plan, System.Random rng, Handedness handedness,
            IReadOnlyList<DeliveryTypeData> types, Func<DeliveryRequest, bool> reachable, out int redraws)
        {
            redraws = 0;
            for (int attempt = 0; attempt <= MaxRedraws; attempt++)
            {
                var d = DrawOnce(plan, rng, handedness, types);
                if (reachable == null || reachable(d))
                    return d;
                redraws++;
            }
            // Fallback: a Stock ball at the fastest speed and a middle length is the easiest to reach.
            var config = DeliveryConfig.Create($"{plan.label} fallback", plan.maxSpeedKmh, 0f, (plan.minLength + plan.maxLength) * 0.5f);
            if (plan.releasePosition != Vector3.zero)
                config.releasePosition = plan.releasePosition;
            var fallback = DeliveryRequest.FromConfig(config);
            fallback.handedness = handedness;
            return fallback;
        }

        static DeliveryRequest DrawOnce(in DeliveryPlanSettings plan, System.Random rng, Handedness handedness,
            IReadOnlyList<DeliveryTypeData> types)
        {
            // Stock-only plans draw no type, so their random sequence is unchanged from before M7.5.
            var type = plan.StockOnly ? DeliveryTypeData.Stock : PickType(plan.typeMix, (float)rng.NextDouble(), types);
            float u = (float)rng.NextDouble();
            string kind;
            float length;
            if (u < plan.yorkerShare)
            {
                kind = "Yorker";
                length = Range(rng, plan.yorkerMinLength, plan.yorkerMaxLength);
            }
            else if (u < plan.yorkerShare + plan.shortShare)
            {
                kind = "Short";
                length = Range(rng, plan.shortMinLength, plan.shortMaxLength);
            }
            else
            {
                length = plan.lengthPeak > 0f
                    ? Triangular(rng, plan.minLength, plan.lengthPeak, plan.maxLength)
                    : Range(rng, plan.minLength, plan.maxLength);
                bool ownBand = plan.goodLengthMax > 0f;
                float good = ownBand ? plan.goodLengthMin : 5f, beyond = ownBand ? plan.goodLengthMax : 7.5f;
                kind = length < good ? "Full" : length < beyond ? "Good length" : ownBand ? "Short" : "Back of a length";
            }
            float minSpeed = plan.minSpeedKmh, maxSpeed = plan.maxSpeedKmh;
            if (!type.UsesPlanSpeed)
            {
                minSpeed = type.minSpeedKmh;
                maxSpeed = type.style == BowlingStyle.Pace ? Mathf.Min(type.maxSpeedKmh, plan.PaceCap) : type.maxSpeedKmh;
                minSpeed = Mathf.Min(minSpeed, maxSpeed); // a range wholly above the cap bowls at the cap
            }
            // A type with a flighted variation (off-spin) tosses some balls up: slower, launched upward. Only such types
            // draw these numbers, so every other plan's random sequence is unchanged.
            bool flighted = type.HasFlighted && rng.NextDouble() < type.flightedShare;
            float launch = 0f;
            if (flighted)
            {
                minSpeed = type.flightedMinKmh;
                maxSpeed = type.flightedMaxKmh;
                launch = Mathf.Round(Range(rng, type.flightedMinLaunch, type.flightedMaxLaunch) * 10f) / 10f;
            }
            float speed = Mathf.Round(Range(rng, minSpeed, maxSpeed));
            float line = Range(rng, plan.minLine, plan.maxLine);

            var config = DeliveryConfig.Create($"{plan.label}: {kind}", speed, line, length);
            if (plan.releasePosition != Vector3.zero)
                config.releasePosition = plan.releasePosition;
            var request = DeliveryRequest.FromConfig(config);
            request.handedness = handedness;
            request.lineReference = plan.lineReference;
            if (!type.IsStill)
            {
                request.type = type;
                request.label = $"{plan.label}: {type.displayName}{(flighted ? " (flighted)" : "")}, {kind}";
                request.flighted = flighted;
                request.launchDegrees = launch;
                request.movement = Movement(plan, type, (float)rng.NextDouble());
                request.seed = rng.Next();
            }
            return request;
        }

        /// <summary>
        /// The type's movement 0-1 for a uniform draw <paramref name="u"/>: spread over the plan's swing range (clipped
        /// to the type's) for a swing type, or the whole range otherwise.
        /// </summary>
        static float Movement(in DeliveryPlanSettings plan, in DeliveryTypeData type, float u)
        {
            if (type.swingDirection == 0 || !(plan.maxSwing > 0f) || !(type.swingMax > type.swingMin))
                return u;
            float lo = Mathf.InverseLerp(type.swingMin, type.swingMax, plan.minSwing);
            float hi = Mathf.InverseLerp(type.swingMin, type.swingMax, plan.maxSwing);
            return Mathf.Lerp(lo, hi, u);
        }

        static DeliveryTypeData PickType(TypeWeight[] mix, float u, IReadOnlyList<DeliveryTypeData> types)
        {
            float total = 0f;
            foreach (var w in mix) total += Mathf.Max(0f, w.weight);
            float pick = u * total;
            int chosen = -1;
            for (int i = 0; i < mix.Length; i++)
            {
                float weight = Mathf.Max(0f, mix[i].weight);
                if (weight <= 0f) continue;
                chosen = i; // the last positive entry also absorbs rounding at u -> 1
                if (pick < weight) break;
                pick -= weight;
            }
            if (chosen < 0)
                throw new ArgumentException("A plan's type mix has no positive weight");
            return Find(mix[chosen].kind, types);
        }

        static DeliveryTypeData Find(DeliveryKind kind, IReadOnlyList<DeliveryTypeData> types)
        {
            var source = types ?? DeliveryTypeData.All;
            foreach (var t in source)
                if (t.kind == kind)
                    return t;
            throw new ArgumentException($"Delivery type {kind} is in a plan's mix but has no settings");
        }

        static float Range(System.Random rng, float min, float max) => min + (float)rng.NextDouble() * (max - min);

        /// <summary>One draw from the triangular distribution min..peak..max (one random number, like <see cref="Range"/>).</summary>
        static float Triangular(System.Random rng, float min, float peak, float max)
        {
            float u = (float)rng.NextDouble();
            if (!(max > min)) return min;
            peak = Mathf.Clamp(peak, min, max);
            float f = (peak - min) / (max - min);
            return u < f
                ? min + Mathf.Sqrt(u * (max - min) * (peak - min))
                : max - Mathf.Sqrt((1f - u) * (max - min) * (max - peak));
        }

        // ---------- T076 headset swing check (AC-26) ----------

        /// <summary>Swing sizes (m) shown by the check, in order, each as an inswinger then an outswinger.</summary>
        public static readonly float[] SwingCheckSizes = { 0.15f, 0.30f, DeliveryTypeData.MaxSwing };
        public const float SwingCheckLength = 6f;
        public const float SwingCheckSlowKmh = 120f;
        /// <summary>Centre of the off stump (m, batter frame).</summary>
        public const float OffStumpLine = (FieldDimensions.StumpSetWidth - FieldDimensions.StumpDiameter) * 0.5f;

        /// <summary>
        /// A fixed sequence instead of a random plan, so the owner knows what each ball should do (T076 build only).
        /// Each over: inswinger, outswinger at 0.15 m, then 0.30 m, then 0.50 m; odd overs at 120 km/h, even overs at
        /// the pace cap. Good length on off stump, no natural variation.
        /// </summary>
        public static DeliveryRequest SwingCheckRequest(int ballIndex, Handedness handedness, IReadOnlyList<DeliveryTypeData> types)
        {
            int inOver = ballIndex % 6;
            bool fast = ballIndex / 6 % 2 == 1;
            var type = Find(inOver % 2 == 0 ? DeliveryKind.Inswinger : DeliveryKind.Outswinger, types);
            type.variation = 0f;
            float swing = SwingCheckSizes[inOver / 2];
            float speed = fast ? DeliveryPlanSettings.DefaultPaceCapKmh : SwingCheckSlowKmh;
            var request = DeliveryRequest.FromConfig(DeliveryConfig.Create(
                $"Swing check: {type.displayName} {swing:F2} m", speed, OffStumpLine, SwingCheckLength));
            request.type = type;
            request.handedness = handedness;
            request.seed = ballIndex + 1;
            request.movement = type.swingMax > type.swingMin ? Mathf.InverseLerp(type.swingMin, type.swingMax, swing) : 1f;
            return request;
        }

        // ---------- T082 headset spin check (AC-27, AC-28) ----------

        /// <summary>Order within each group of four: each spinner's stock ball, then its variation.</summary>
        public static readonly DeliveryKind[] SpinCheckTypes = { DeliveryKind.OffSpin, DeliveryKind.Doosra, DeliveryKind.LegSpin, DeliveryKind.Googly };
        /// <summary>Lengths (m) of the three groups of four: good, full, short.</summary>
        public static readonly float[] SpinCheckLengths = { 6f, 4f, 8f };
        public const float SpinCheckKmh = 85f;
        public const float SpinCheckMovement = 0.5f;

        /// <summary>
        /// A fixed sequence for the spin headset check (T082 build only): balls 1-4 off-spin, doosra, leg-spin, googly
        /// at a good length (6 m), balls 5-8 the same at full (4 m), balls 9-12 at short (8 m), then repeat. 85 km/h,
        /// pitched on off stump, movement 0.5, no natural variation.
        /// </summary>
        public static DeliveryRequest SpinCheckRequest(int ballIndex, Handedness handedness, IReadOnlyList<DeliveryTypeData> types)
        {
            int i = ballIndex % (SpinCheckTypes.Length * SpinCheckLengths.Length);
            var type = Find(SpinCheckTypes[i % SpinCheckTypes.Length], types);
            type.variation = 0f;
            float length = SpinCheckLengths[i / SpinCheckTypes.Length];
            string name = length < 5f ? "full" : length < 7.5f ? "good length" : "short";
            var request = DeliveryRequest.FromConfig(DeliveryConfig.Create(
                $"Spin check: {type.displayName}, {name}", SpinCheckKmh, OffStumpLine, length));
            request.type = type;
            request.handedness = handedness;
            request.seed = ballIndex + 1;
            request.movement = SpinCheckMovement;
            return request;
        }

        /// <summary>Reachability of a request (any type) as the machine will solve it.</summary>
        public static Func<DeliveryRequest, bool> RequestSolverCheck(float gravity, float fixedDeltaTime, float ballRadius) =>
            r =>
            {
                var world = r.ForBatter();
                return DeliverySolver.Solve(world, DeliveryMovement.For(world), gravity, fixedDeltaTime, ballRadius).Reachable;
            };

        /// <summary>The solver's reachability test at the game's physics settings.</summary>
        public static Func<DeliveryConfig, bool> SolverCheck(float gravity, float fixedDeltaTime, float ballRadius) =>
            d => DeliverySolver.Solve(d, gravity, fixedDeltaTime, ballRadius).Reachable;
    }
}
