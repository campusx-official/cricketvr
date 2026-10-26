using System;
using UnityEngine;

namespace CricketVR
{
    /// <summary>
    /// What a mode or difficulty bowls (REQ-028, spec.md "Two modes"). Lines are given for a right-hander (+ = off
    /// side) and mirrored for a left-hander. Lengths are bounce distances in front of the striker's stumps.
    /// </summary>
    [Serializable]
    public struct DeliveryPlanSettings
    {
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
    /// Deterministic for a given System.Random seed.
    /// </summary>
    public static class DeliveryPlanner
    {
        public const int MaxRedraws = 10;

        public static DeliveryConfig Draw(in DeliveryPlanSettings plan, System.Random rng, bool leftHanded,
            Func<DeliveryConfig, bool> reachable, out int redraws)
        {
            redraws = 0;
            for (int attempt = 0; attempt <= MaxRedraws; attempt++)
            {
                var d = DrawOnce(plan, rng, leftHanded);
                if (reachable == null || reachable(d))
                    return d;
                redraws++;
            }
            // Fallback: the fastest speed at a middle length is the easiest to reach.
            var fallback = DeliveryConfig.Create($"{plan.label} fallback", plan.maxSpeedKmh, 0f, (plan.minLength + plan.maxLength) * 0.5f);
            return fallback;
        }

        static DeliveryConfig DrawOnce(in DeliveryPlanSettings plan, System.Random rng, bool leftHanded)
        {
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
            float speed = Mathf.Round(Range(rng, plan.minSpeedKmh, plan.maxSpeedKmh));
            float line = Range(rng, plan.minLine, plan.maxLine);
            if (leftHanded) line = -line;
            return DeliveryConfig.Create($"{plan.label}: {kind}", speed, line, length);
        }

        static float Range(System.Random rng, float min, float max) => min + (float)rng.NextDouble() * (max - min);

        /// <summary>The solver's reachability test at the game's physics settings.</summary>
        public static Func<DeliveryConfig, bool> SolverCheck(float gravity, float fixedDeltaTime, float ballRadius) =>
            d => DeliverySolver.Solve(d, gravity, fixedDeltaTime, ballRadius).Reachable;
    }
}
