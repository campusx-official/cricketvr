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

        public float PaceCap => paceCapKmh > 0f ? paceCapKmh : DefaultPaceCapKmh;
        public bool StockOnly => typeMix == null || typeMix.Length == 0;

        public static DeliveryPlanSettings NetPractice => new DeliveryPlanSettings
        {
            label = "Net Practice", minSpeedKmh = 70f, maxSpeedKmh = 90f, minLine = -0.1f, maxLine = 0.4f,
            minLength = 3.5f, maxLength = 9f,
        };

        public static DeliveryPlanSettings Easy => Match("Easy", 70f, 85f, 0.05f, 0.10f);
        public static DeliveryPlanSettings Medium => Match("Medium", 80f, 100f, 0.10f, 0.15f);
        /// <summary>Capped at 120 km/h until AC-04 is re-run for 100-120 km/h (C-14).</summary>
        public static DeliveryPlanSettings Hard => Match("Hard", 95f, 120f, 0.20f, 0.20f);

        static DeliveryPlanSettings Match(string label, float min, float max, float yorkers, float shorts) => new DeliveryPlanSettings
        {
            label = label, minSpeedKmh = min, maxSpeedKmh = max, minLine = -0.1f, maxLine = 0.3f,
            minLength = 4f, maxLength = 7.5f,
            yorkerShare = yorkers, yorkerMinLength = 0.8f, yorkerMaxLength = 1.5f,
            shortShare = shorts, shortMinLength = 8.5f, shortMaxLength = 10f,
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
            var fallback = DeliveryRequest.FromConfig(
                DeliveryConfig.Create($"{plan.label} fallback", plan.maxSpeedKmh, 0f, (plan.minLength + plan.maxLength) * 0.5f));
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
                length = Range(rng, plan.minLength, plan.maxLength);
                kind = length < 5f ? "Full" : length < 7.5f ? "Good length" : "Back of a length";
            }
            float minSpeed = plan.minSpeedKmh, maxSpeed = plan.maxSpeedKmh;
            if (!type.UsesPlanSpeed)
            {
                minSpeed = type.minSpeedKmh;
                maxSpeed = type.style == BowlingStyle.Pace ? Mathf.Min(type.maxSpeedKmh, plan.PaceCap) : type.maxSpeedKmh;
                minSpeed = Mathf.Min(minSpeed, maxSpeed); // a range wholly above the cap bowls at the cap
            }
            float speed = Mathf.Round(Range(rng, minSpeed, maxSpeed));
            float line = Range(rng, plan.minLine, plan.maxLine);

            var request = DeliveryRequest.FromConfig(DeliveryConfig.Create($"{plan.label}: {kind}", speed, line, length));
            request.handedness = handedness;
            if (!type.IsStill)
            {
                request.type = type;
                request.label = $"{plan.label}: {type.displayName}, {kind}";
                request.movement = (float)rng.NextDouble();
                request.seed = rng.Next();
            }
            return request;
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
