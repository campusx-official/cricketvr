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

    /// <summary>
    /// What the flight applies before the bounce, as solved for one ball: swing acceleration A along the side
    /// direction, growing as (t/T)^p. Solved by <see cref="DeliverySolver"/>; the runtime flight applies exactly this.
    /// </summary>
    public struct FlightPlan
    {
        /// <summary>-1 = toward −X, +1 = toward +X, 0 = no swing (world).</summary>
        public int SwingDirection;
        /// <summary>Swing acceleration at t = T (m/s²).</summary>
        public float SwingAcceleration;
        public float SwingLateness;
        /// <summary>The solver's planned time from release to the pitch point (s).</summary>
        public float PlannedTime;

        public bool IsStill => SwingDirection == 0 || SwingAcceleration == 0f;
    }

    /// <summary>
    /// Pure delivery-type flight model (plan.md §19), used identically by the solver and by the runtime flight.
    /// Arcade-believable movement, not aerodynamics: explicit accelerations, no drag.
    /// </summary>
    public static class DeliveryFlightModel
    {
        /// <summary>
        /// Extra acceleration (m/s², world, excluding gravity) for the step starting at time <paramref name="t"/>
        /// after release, from the velocity at the start of that step. Swing acts sideways: perpendicular to the
        /// horizontal velocity, on the side of the swing direction.
        /// </summary>
        public static Vector3 Acceleration(float t, Vector3 velocity, in FlightPlan plan)
        {
            if (plan.IsStill || !(plan.PlannedTime > 0f))
                return Vector3.zero;
            var side = new Vector3(-velocity.z, 0f, velocity.x);
            float length = side.magnitude;
            if (length < 1e-6f)
                return Vector3.zero;
            side /= length;
            if (side.x * plan.SwingDirection < 0f)
                side = -side;
            float u = Mathf.Max(0f, t / plan.PlannedTime);
            float growth = plan.SwingLateness > 0f ? Mathf.Pow(u, plan.SwingLateness) : 1f;
            return side * (plan.SwingAcceleration * growth);
        }

        /// <summary>
        /// Swing acceleration A that curves a ball of flight time T by <paramref name="curve"/> (m), the largest
        /// sideways distance from the straight release-to-bounce line (DT-13). With a = A (t/T)^p and both ends on the
        /// line, the deviation peaks at u* = (p + 2)^(−1/(p+1)) and equals A T² (u* − u*^(p+2)) / ((p+1)(p+2)).
        /// Small-angle result; the solver corrects the remainder so the simulated curve matches.
        /// </summary>
        public static float SwingAccelerationFor(float curve, float flightTime, float lateness)
        {
            if (!(curve > 0f) || !(flightTime > 0f))
                return 0f;
            float p = Mathf.Max(0f, lateness);
            float u = Mathf.Pow(p + 2f, -1f / (p + 1f));
            float k = (u - Mathf.Pow(u, p + 2f)) / ((p + 1f) * (p + 2f));
            return curve / (flightTime * flightTime * k);
        }
    }

    /// <summary>
    /// How the ball's seam is shown (DT-9, DT-10, D-4): a readable stand-in for the real rotation, never faster than
    /// <see cref="MaxDegreesPerFrame"/> so the seam doesn't strobe at 72 Hz. The seam is the ball mesh's equator, so the
    /// spin axis is the mesh's local up.
    /// </summary>
    public static class BallSpinVisual
    {
        /// <summary>5 rev/s at 72 Hz.</summary>
        public const float MaxDegreesPerFrame = 25f;
        /// <summary>How far a swinging ball's upright seam points off its line of travel (visual only).</summary>
        public const float SwingSeamAngleDegrees = 15f;

        /// <summary>Visual rotation for one rendered frame (degrees), capped.</summary>
        public static float DegreesThisFrame(float revPerSecond, float deltaTime) =>
            Mathf.Min(Mathf.Max(0f, revPerSecond) * 360f * Mathf.Max(0f, deltaTime), MaxDegreesPerFrame);

        /// <summary>
        /// Spin axis (world, unit) for a pace ball: the seam stands upright, angled toward fine leg (inswing, world
        /// direction −1) or the slips (outswing, +1) off the horizontal travel direction; the axis is perpendicular
        /// to the seam's plane and oriented for backspin (positive angle about it turns the top of the ball back
        /// toward the bowler).
        /// </summary>
        public static Vector3 PaceSpinAxis(Vector3 travel, int swingDirection)
        {
            var forward = new Vector3(travel.x, 0f, travel.z);
            forward = forward.sqrMagnitude > 1e-8f ? forward.normalized : Vector3.back;
            // Turn the seam off the travel toward world x = swingDirection (fine leg for inswing, slip for outswing).
            var seam = Quaternion.AngleAxis(-swingDirection * SwingSeamAngleDegrees, Vector3.up) * forward;
            if (swingDirection != 0 && Mathf.Sign(seam.x - forward.x) != swingDirection)
                seam = Quaternion.AngleAxis(swingDirection * SwingSeamAngleDegrees, Vector3.up) * forward;
            var axis = Vector3.Cross(Vector3.up, seam).normalized;
            // Orient by the effect, not by handedness conventions: a small positive turn must move the top backward.
            var top = Quaternion.AngleAxis(1f, axis) * Vector3.up;
            return Vector3.Dot(top - Vector3.up, -forward) >= 0f ? axis : -axis;
        }
    }
}
