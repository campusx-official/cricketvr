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
        /// <summary>
        /// Swing (m): how far the ball is, at the striker's popping crease, from the straight line it was released
        /// along (plan.md §19, owner decision 2026-10-27). Never more than <see cref="DeliveryTypeData.MaxSwing"/>.
        /// </summary>
        public float Swing;
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

        public bool IsStill => Swing == 0f && Drift == 0f && Dip == 0f && TurnDegrees == 0f &&
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
                // The cap applies after variation, so a varied ball never swings more than 0.5 m.
                Swing = t.swingDirection == 0 ? 0f : Mathf.Min(Mathf.Lerp(t.swingMin, t.swingMax, m) * factors.x, DeliveryTypeData.MaxSwing),
                SwingLateness = t.swingLateness,
                TurnDirection = t.turnDirection,
                Drift = t.turnDirection == 0 ? 0f : Mathf.Lerp(t.driftMin, t.driftMax, m) * factors.y,
                Dip = FlightDip(t, request.speedKmh),
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
        /// A spinner's dip at this speed (owner feedback 2026-10-27: more flight, and varying): the slowest ball of the
        /// type's range is the most tossed up (<see cref="DeliveryTypeData.flightDip"/>), the fastest the flattest
        /// (<see cref="DeliveryTypeData.dip"/>). The solver launches a ball that dips more higher, so it loops and
        /// arrives steeper.
        /// </summary>
        public static float FlightDip(in DeliveryTypeData t, float speedKmh)
        {
            if (!(t.flightDip > 0f) || t.UsesPlanSpeed || !(t.maxSpeedKmh > t.minSpeedKmh))
                return t.dip;
            return Mathf.Lerp(t.flightDip, t.dip, Mathf.InverseLerp(t.minSpeedKmh, t.maxSpeedKmh, speedKmh));
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
    /// What the flight applies, as solved for one ball. Before the bounce: swing acceleration A along the side
    /// direction, growing as (t/T)^p; a constant sideways drift; a constant downward dip. At the bounce: the turn.
    /// Solved by <see cref="DeliverySolver"/>; the runtime flight applies exactly this.
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
        /// <summary>-1 = toward −X, +1 = toward +X, 0 = no drift (world); opposite to the turn.</summary>
        public int DriftDirection;
        /// <summary>Constant sideways drift acceleration before the bounce (m/s²).</summary>
        public float DriftAcceleration;
        /// <summary>Extra downward acceleration before the bounce (m/s², top-spin dip).</summary>
        public float Dip;
        /// <summary>-1 = turns toward −X, +1 = toward +X, 0 = no turn (world).</summary>
        public int TurnDirection;
        /// <summary>Heading change at the bounce (degrees, ≥ 0).</summary>
        public float TurnDegrees;
        /// <summary>Vertical speed added at the bounce, as a fraction of PhysX's rebound (b).</summary>
        public float BounceFactor;
        /// <summary>Fraction of the horizontal speed lost to grip at the bounce.</summary>
        public float Grip;

        public bool AcceleratesBeforeBounce => (SwingDirection != 0 && SwingAcceleration != 0f) ||
                                               (DriftDirection != 0 && DriftAcceleration != 0f) || Dip != 0f;
        public bool ActsAtBounce => (TurnDirection != 0 && TurnDegrees != 0f) || BounceFactor != 0f || Grip != 0f;
        public bool IsStill => !AcceleratesBeforeBounce && !ActsAtBounce;
    }

    /// <summary>
    /// Pure delivery-type flight model (plan.md §19), used identically by the solver and by the runtime flight.
    /// Arcade-believable movement, not aerodynamics: explicit accelerations, no drag.
    /// </summary>
    public static class DeliveryFlightModel
    {
        /// <summary>
        /// Extra acceleration (m/s², world, excluding gravity) for the step starting at time <paramref name="t"/>
        /// after release, from the velocity at the start of that step. Swing and drift act sideways: perpendicular to
        /// the horizontal velocity, on the side of their direction. Dip acts straight down.
        /// </summary>
        public static Vector3 Acceleration(float t, Vector3 velocity, in FlightPlan plan)
        {
            if (!plan.AcceleratesBeforeBounce || !(plan.PlannedTime > 0f))
                return Vector3.zero;
            var a = new Vector3(0f, -plan.Dip, 0f);
            var side = new Vector3(-velocity.z, 0f, velocity.x);
            float length = side.magnitude;
            if (length < 1e-6f)
                return a;
            side /= length;
            if (side.x < 0f)
                side = -side; // toward +X; each effect's direction picks the side
            if (plan.SwingDirection != 0 && plan.SwingAcceleration != 0f)
            {
                float u = Mathf.Max(0f, t / plan.PlannedTime);
                float growth = plan.SwingLateness > 0f ? Mathf.Pow(u, plan.SwingLateness) : 1f;
                a += side * (plan.SwingDirection * plan.SwingAcceleration * growth);
            }
            if (plan.DriftDirection != 0)
                a += side * (plan.DriftDirection * plan.DriftAcceleration);
            return a;
        }

        /// <summary>
        /// Signed rotation about world up (degrees) that turns the horizontal <paramref name="velocity"/> by the plan's
        /// turn toward its turn direction; 0 without a turn.
        /// </summary>
        public static float TurnAngle(Vector3 velocity, in FlightPlan plan)
        {
            if (plan.TurnDirection == 0 || !(plan.TurnDegrees > 0f))
                return 0f;
            // Rotating (x, z) by +a about +Y gives x' = x cos a + z sin a: the sign that moves x toward the turn
            // depends on which way the ball travels.
            float sign = velocity.z * plan.TurnDirection > 0f ? 1f : -1f;
            return sign * plan.TurnDegrees;
        }

        /// <summary>
        /// The velocity just after the bounce (plan.md §19, Option A): PhysX's rebound <paramref name="velocity"/>
        /// with its heading turned by the plan's turn, its horizontal speed reduced by grip, and its vertical speed
        /// raised by the bounce factor. Applied once, in the step after the ball turns upward.
        /// </summary>
        public static Vector3 TurnAtBounce(Vector3 velocity, in FlightPlan plan)
        {
            var horizontal = Quaternion.AngleAxis(TurnAngle(velocity, plan), Vector3.up) * new Vector3(velocity.x, 0f, velocity.z);
            horizontal *= 1f - Mathf.Clamp01(plan.Grip);
            return new Vector3(horizontal.x, velocity.y * (1f + plan.BounceFactor), horizontal.z);
        }

        /// <summary>
        /// Constant sideways acceleration that moves the ball <paramref name="drift"/> (m) off its release line by the
        /// pitch point, <paramref name="flightTime"/> seconds after release: d = a T² / 2. The solver rescales it until
        /// the simulated drift matches.
        /// </summary>
        public static float DriftAccelerationFor(float drift, float flightTime) =>
            drift > 0f && flightTime > 0f ? 2f * drift / (flightTime * flightTime) : 0f;

        /// <summary>
        /// Swing acceleration A that puts the ball <paramref name="swing"/> (m) off its release line at the popping
        /// crease. With a = A (t/T)^p sideways, the ball is A T² / ((p+1)(p+2)) off the release line at the pitch point
        /// (t = T) and moving sideways at A T / (p+1), which it keeps after pitching for <paramref name="pitchToCrease"/>
        /// seconds (negative when it reaches the crease before pitching). Small-angle result; the solver rescales A
        /// until the simulated swing matches.
        /// </summary>
        public static float SwingAccelerationFor(float swing, float flightTime, float lateness, float pitchToCrease)
        {
            if (!(swing > 0f) || !(flightTime > 0f))
                return 0f;
            float p = Mathf.Max(0f, lateness);
            float reach = flightTime / (p + 2f) + pitchToCrease;
            reach = Mathf.Max(reach, 0.1f * flightTime / (p + 2f)); // a yorker reaches the crease just before it pitches
            return swing * (p + 1f) / (flightTime * reach);
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
