using UnityEngine;

namespace CricketVR
{
    /// <summary>
    /// One ball's movement, resolved from a world-frame request (plan.md §19): the type's settings at the request's
    /// movement 0–1, scaled by the ball's natural-variation factors (REQ-048). Directions are already mirrored for
    /// the batter by <see cref="DeliveryRequest.ForBatter"/>. The solver and the runtime flight both use this, so the
    /// solver lands the varied ball where it was requested.
    /// </summary>
    public struct DeliveryMovement
    {
        /// <summary>-1 = toward −X, +1 = toward +X, 0 = none (world).</summary>
        public int SwingDirection;
        /// <summary>Curve (m): largest sideways distance from the straight release-to-bounce line.</summary>
        public float SwingCurve;
        public float SwingLateness;
        /// <summary>Turn direction at the bounce (world); drift goes the other way.</summary>
        public int TurnDirection;
        /// <summary>Air drift by the bounce (m).</summary>
        public float Drift;
        /// <summary>Extra downward acceleration before the bounce (m/s²).</summary>
        public float Dip;
        public float TurnDegrees;
        public float BounceFactor;
        public float Grip;
        public float SeamDegrees;
        /// <summary>Applied natural-variation factors (1 = nominal).</summary>
        public float VarSwing, VarDrift, VarTurn;

        public bool IsStill => SwingCurve == 0f && Drift == 0f && Dip == 0f && TurnDegrees == 0f &&
                               BounceFactor == 0f && Grip == 0f && SeamDegrees == 0f;

        /// <param name="request">A request after <see cref="DeliveryRequest.ForBatter"/>.</param>
        public static DeliveryMovement For(in DeliveryRequest request)
        {
            var t = request.type;
            float m = Mathf.Clamp01(request.movement);
            var factors = VariationFactors(request.seed, t.variation);
            return new DeliveryMovement
            {
                SwingDirection = t.swingDirection,
                SwingCurve = t.swingDirection == 0 ? 0f : Mathf.Lerp(t.swingMin, t.swingMax, m) * factors.x,
                SwingLateness = t.swingLateness,
                TurnDirection = t.turnDirection,
                Drift = t.turnDirection == 0 ? 0f : Mathf.Lerp(t.driftMin, t.driftMax, m) * factors.y,
                Dip = t.dip,
                TurnDegrees = t.turnDirection == 0 ? 0f : Mathf.Lerp(t.turnMin, t.turnMax, m) * factors.z,
                BounceFactor = t.bounceFactor,
                Grip = t.grip,
                SeamDegrees = t.seamDegrees,
                VarSwing = factors.x,
                VarDrift = factors.y,
                VarTurn = factors.z,
            };
        }

        /// <summary>
        /// Swing, drift and turn factors, each uniform in [1 − v, 1 + v], drawn in that order from System.Random(seed)
        /// (deterministic on every platform). v = 0 gives exactly 1.
        /// </summary>
        public static Vector3 VariationFactors(int seed, float variation)
        {
            if (!(variation > 0f))
                return Vector3.one;
            var rng = new System.Random(seed);
            float F() => 1f + variation * (2f * (float)rng.NextDouble() - 1f);
            float swing = F(), drift = F(), turn = F();
            return new Vector3(swing, drift, turn);
        }
    }
}
