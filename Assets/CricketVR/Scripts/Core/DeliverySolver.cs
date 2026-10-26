using System;
using UnityEngine;

namespace CricketVR
{
    public static class Units
    {
        public static float KmhToMs(float kmh) => kmh / 3.6f;
        public static float MsToKmh(float ms) => ms * 3.6f;
    }

    /// <summary>
    /// One delivery request. Release position, speed, line and length are independent fields (CLAUDE.md §9).
    /// Line and length describe the requested FIRST BOUNCE point: x = line, z = length (metres in front of the
    /// striker's stumps, which stand at z = 0).
    /// </summary>
    [Serializable]
    public struct DeliveryConfig
    {
        public string label;
        [Tooltip("Release speed in km/h.")]
        public float speedKmh;
        [Tooltip("Ball centre at release (world, metres).")]
        public Vector3 releasePosition;
        [Tooltip("Bounce x (m). 0 = middle stump; + = off side for a right-hander.")]
        public float line;
        [Tooltip("Bounce distance in front of the striker's stumps (m).")]
        public float length;

        public static DeliveryConfig Create(string label, float speedKmh, float line, float length) => new DeliveryConfig
        {
            label = label,
            speedKmh = speedKmh,
            releasePosition = FieldDimensions.DefaultReleasePoint,
            line = line,
            length = length,
        };

        public override string ToString() => $"{label}: {speedKmh:F0} km/h, line {line:+0.00;-0.00;0.00} m, length {length:F2} m";
    }

    public struct DeliverySolution
    {
        public bool Reachable;
        /// <summary>Initial ball velocity (m/s, world). Magnitude equals the requested speed.</summary>
        public Vector3 Velocity;
        /// <summary>Requested bounce point (ball centre at contact height).</summary>
        public Vector3 Target;
        /// <summary>Where the simulated (discrete-step) trajectory reaches contact height.</summary>
        public Vector3 PredictedBounce;
        public float ElevationDegrees;
        /// <summary>Lowest speed (km/h) that can reach the target, for error messages.</summary>
        public float MinimumSpeedKmh;
        public string Error;
        /// <summary>Movement the runtime flight applies before the bounce (still for Stock).</summary>
        public FlightPlan Plan;
        /// <summary>Simulated swing deviation (m), DT-13; 0 without swing.</summary>
        public float PredictedCurve;
    }

    /// <summary>
    /// Computes the release velocity that lands the ball on a requested bounce point at a fixed speed, under gravity
    /// with no air drag (MVP decision). The speed is never changed: an unreachable request is reported, not adjusted.
    /// </summary>
    public static class DeliverySolver
    {
        const int MaxRefineIterations = 30;
        const float RefineTolerance = 0.005f; // metres along the ground

        /// <param name="gravity">Downward acceleration magnitude (m/s², positive).</param>
        /// <param name="fixedDeltaTime">Physics step; used to correct for the step-wise integration (0 = skip).</param>
        /// <param name="ballRadius">The bounce happens when the ball centre is this high above the ground.</param>
        public static DeliverySolution Solve(DeliveryConfig config, float gravity, float fixedDeltaTime, float ballRadius)
        {
            var result = new DeliverySolution
            {
                Target = new Vector3(config.line, ballRadius, config.length),
            };
            float speed = Units.KmhToMs(config.speedKmh);

            if (!(speed > 0f) || !float.IsFinite(speed) || !(gravity > 0f))
                return Fail(result, $"invalid speed {config.speedKmh} km/h or gravity {gravity}");

            var release = config.releasePosition;
            var toTarget = result.Target - release;
            var horizontal = new Vector3(toTarget.x, 0f, toTarget.z);
            float d = horizontal.magnitude;
            float dy = toTarget.y;
            if (d < 1f)
                return Fail(result, $"bounce point is only {d:F2} m from the release point");
            if (release.y <= ballRadius)
                return Fail(result, "release point is at or below ground level");

            // Slowest speed that reaches the target at all (the 45-degree-ish optimum), for the error message.
            float minSpeed = Mathf.Sqrt(gravity * (dy + Mathf.Sqrt(dy * dy + d * d)));
            result.MinimumSpeedKmh = Units.MsToKmh(minSpeed);

            // Continuous projectile: tan(theta) = (s^2 - sqrt(s^4 - g(g d^2 + 2 dy s^2))) / (g d). Lower root = flat delivery.
            float s2 = speed * speed;
            float disc = s2 * s2 - gravity * (gravity * d * d + 2f * dy * s2);
            if (disc < 0f)
                return Fail(result, $"{config.speedKmh:F0} km/h cannot reach a bounce {d:F1} m away; needs at least {result.MinimumSpeedKmh:F1} km/h");

            float theta = Mathf.Atan((s2 - Mathf.Sqrt(disc)) / (gravity * d));

            // Refine for the physics engine's step-wise (semi-implicit Euler) integration, which lands slightly
            // differently from the continuous formula. Bisect the elevation until the stepped flight lands on target.
            if (fixedDeltaTime > 0f)
            {
                float lo = theta - 0.2f, hi = theta + 0.2f;
                float landLo = SimulatedRange(release, speed, lo, gravity, fixedDeltaTime, ballRadius);
                float landHi = SimulatedRange(release, speed, hi, gravity, fixedDeltaTime, ballRadius);
                if (landLo <= d && landHi >= d)
                {
                    for (int i = 0; i < MaxRefineIterations; i++)
                    {
                        float mid = 0.5f * (lo + hi);
                        float land = SimulatedRange(release, speed, mid, gravity, fixedDeltaTime, ballRadius);
                        if (land < d) lo = mid; else hi = mid;
                        theta = mid;
                        if (Mathf.Abs(land - d) < RefineTolerance) break;
                    }
                }
            }

            var dir = horizontal / d;
            result.Velocity = dir * (speed * Mathf.Cos(theta)) + Vector3.up * (speed * Mathf.Sin(theta));
            result.ElevationDegrees = theta * Mathf.Rad2Deg;
            float landed = fixedDeltaTime > 0f
                ? SimulatedRange(release, speed, theta, gravity, fixedDeltaTime, ballRadius)
                : d;
            var predicted = release + dir * landed;
            result.PredictedBounce = new Vector3(predicted.x, ballRadius, predicted.z);
            result.Reachable = true;
            return result;
        }

        /// <summary>
        /// Horizontal distance travelled before the ball centre drops to <paramref name="contactHeight"/>, integrating the
        /// way PhysX does (velocity first, then position) and interpolating within the crossing step.
        /// Returns +infinity if it never comes down within 10 s.
        /// </summary>
        public static float SimulatedRange(Vector3 release, float speed, float elevation, float gravity, float dt, float contactHeight)
        {
            float vh = speed * Mathf.Cos(elevation);
            float vy = speed * Mathf.Sin(elevation);
            float x = 0f, y = release.y;
            for (int step = 0; step < 10f / dt; step++)
            {
                vy -= gravity * dt;
                float nx = x + vh * dt;
                float ny = y + vy * dt;
                if (ny <= contactHeight)
                {
                    float f = (y - contactHeight) / (y - ny);
                    return x + (nx - x) * f;
                }
                x = nx;
                y = ny;
            }
            return float.PositiveInfinity;
        }

        // ---------- Movement-aware solve (M7.5, plan.md §19) ----------

        const int MaxMovingIterations = 20;
        const float MovingTolerance = 0.001f; // metres: lands within 1 mm in simulation
        const float MaxYaw = 0.5f;            // radians off the straight line to the bounce point

        /// <summary>
        /// Solves a world-frame request (after <see cref="DeliveryRequest.ForBatter"/>). A ball without movement uses
        /// <see cref="Solve(DeliveryConfig,float,float,float)"/> unchanged; a moving ball uses the shooting solve
        /// below, with its natural variation already in <paramref name="movement"/>.
        /// </summary>
        public static DeliverySolution Solve(in DeliveryRequest worldRequest, in DeliveryMovement movement, float gravity, float fixedDeltaTime, float ballRadius)
        {
            var config = worldRequest.ToConfig();
            if (movement.IsStill)
                return Solve(config, gravity, fixedDeltaTime, ballRadius);
            if (worldRequest.lineReference != LineReference.AtBounce)
                return Fail(new DeliverySolution { Target = new Vector3(config.line, ballRadius, config.length) },
                    "arrival-line requests are not available yet (T079)");
            if (movement.Drift != 0f || movement.Dip != 0f || movement.TurnDegrees != 0f || movement.BounceFactor != 0f ||
                movement.Grip != 0f || movement.SeamDegrees != 0f)
                return Fail(new DeliverySolution { Target = new Vector3(config.line, ballRadius, config.length) },
                    "spin movement is not available yet (M7.5 phase B)");
            return SolveMoving(config, movement, gravity, fixedDeltaTime, ballRadius);
        }

        /// <summary>
        /// Shooting solve on (yaw, elevation) at the requested speed. Integrates gravity plus
        /// <see cref="DeliveryFlightModel.Acceleration"/> exactly as PhysX steps the ball (velocity, then position)
        /// down to the pitch point. An inner Newton loop corrects yaw and elevation until it lands within 0.1 mm; an
        /// outer loop then updates the planned flight time T and rescales the swing so the simulated curve equals the
        /// requested one (the closed form is a small-angle result). Converges in about three outer passes.
        /// </summary>
        static DeliverySolution SolveMoving(DeliveryConfig config, in DeliveryMovement movement, float gravity, float dt, float radius)
        {
            var still = Solve(config, gravity, dt, radius);
            if (!still.Reachable || !(dt > 0f))
                return still.Reachable ? Fail(still, "a moving delivery needs the physics step") : still;

            var result = still;
            var release = config.releasePosition;
            var target = still.Target;
            var toTarget = new Vector3(target.x - release.x, 0f, target.z - release.z);
            var forward = toTarget.normalized;
            var side = new Vector3(-forward.z, 0f, forward.x); // the yaw axis's positive turn
            float speed = Units.KmhToMs(config.speedKmh);

            float yaw = 0f, elevation = still.ElevationDegrees * Mathf.Deg2Rad;
            var start = Simulate(release, Launch(forward, side, speed, yaw, elevation), default, gravity, dt, radius, target);
            float curve = movement.SwingCurve, scale = 1f;
            var plan = new FlightPlan
            {
                SwingDirection = movement.SwingDirection,
                SwingLateness = movement.SwingLateness,
                PlannedTime = start.Time,
            };

            // Outer loop: swing strength and planned time from a flight that lands on target. Inner loop: aim.
            for (int outer = 0; outer < MaxMovingIterations; outer++)
            {
                plan.SwingAcceleration = scale * DeliveryFlightModel.SwingAccelerationFor(curve, plan.PlannedTime, plan.SwingLateness);
                SimulatedFlight flight = default;
                bool aimed = false;
                for (int inner = 0; inner < MaxMovingIterations && !aimed; inner++)
                {
                    flight = Simulate(release, Launch(forward, side, speed, yaw, elevation), plan, gravity, dt, radius, target);
                    if (!flight.Landed)
                        return Fail(result, $"{config.speedKmh:F0} km/h with this movement never comes down");
                    var miss = new Vector3(target.x - flight.Bounce.x, 0f, target.z - flight.Bounce.z);
                    if (miss.magnitude < MovingTolerance * 0.1f)
                    {
                        aimed = true;
                        break;
                    }

                    // Newton step on (yaw, elevation) from finite differences of the landing point (side, forward).
                    const float h = 1e-4f;
                    var dYaw = Simulate(release, Launch(forward, side, speed, yaw + h, elevation), plan, gravity, dt, radius, target);
                    var dEl = Simulate(release, Launch(forward, side, speed, yaw, elevation + h), plan, gravity, dt, radius, target);
                    if (!dYaw.Landed || !dEl.Landed)
                        return Fail(result, $"{config.speedKmh:F0} km/h with this movement never comes down");
                    var basePoint = flight.Bounce;
                    float a11 = Vector3.Dot(dYaw.Bounce - basePoint, side) / h, a12 = Vector3.Dot(dEl.Bounce - basePoint, side) / h;
                    float a21 = Vector3.Dot(dYaw.Bounce - basePoint, forward) / h, a22 = Vector3.Dot(dEl.Bounce - basePoint, forward) / h;
                    float b1 = Vector3.Dot(miss, side), b2 = Vector3.Dot(miss, forward);
                    float det = a11 * a22 - a12 * a21;
                    if (Mathf.Abs(det) < 1e-9f)
                        return Fail(result, "the aim cannot be corrected for this movement");
                    yaw += Mathf.Clamp((b1 * a22 - a12 * b2) / det, -0.1f, 0.1f);
                    elevation += Mathf.Clamp((a11 * b2 - a21 * b1) / det, -0.1f, 0.1f);
                    if (Mathf.Abs(yaw) > MaxYaw)
                        return Fail(result, $"too much movement to reach line {config.line:+0.00;-0.00} m (aim {yaw * Mathf.Rad2Deg:F0} degrees off)");
                }
                if (!aimed)
                    return Fail(result, $"no aim lands {config} with this movement within {MovingTolerance * 1000f:F0} mm");

                bool timed = Mathf.Abs(flight.Time - plan.PlannedTime) < 1e-4f;
                bool curved = curve <= 0f || Mathf.Abs(flight.Curve - curve) < MovingTolerance * 0.5f;
                if (timed && curved)
                {
                    result.Velocity = Launch(forward, side, speed, yaw, elevation);
                    result.ElevationDegrees = elevation * Mathf.Rad2Deg;
                    result.PredictedBounce = flight.Bounce;
                    result.PredictedCurve = flight.Curve;
                    result.Plan = plan;
                    return result;
                }
                plan.PlannedTime = flight.Time;
                if (curve > 0f && flight.Curve > 1e-4f)
                    scale *= curve / flight.Curve;
            }
            return Fail(result, $"the swing for {config} did not settle within {MaxMovingIterations} passes");
        }

        static Vector3 Launch(Vector3 forward, Vector3 side, float speed, float yaw, float elevation)
        {
            var heading = forward * Mathf.Cos(yaw) + side * Mathf.Sin(yaw);
            return heading * (speed * Mathf.Cos(elevation)) + Vector3.up * (speed * Mathf.Sin(elevation));
        }

        public struct SimulatedFlight
        {
            public bool Landed;
            /// <summary>Ball centre where it reaches contact height (the pitch point).</summary>
            public Vector3 Bounce;
            /// <summary>Time from release to the pitch point (s).</summary>
            public float Time;
            /// <summary>Largest horizontal distance from the straight line release → chord end, before the pitch point (m).</summary>
            public float Curve;
            /// <summary>Velocity on arrival at the pitch point.</summary>
            public Vector3 ArrivalVelocity;
        }

        /// <summary>
        /// Steps the ball as PhysX does (v += (g + a)·dt, then x += v·dt; a from the velocity at the step's start)
        /// until its centre crosses <paramref name="contactHeight"/>, interpolating within the crossing step. The curve
        /// is measured against the line from the release to <paramref name="chordEnd"/> (the requested pitch point,
        /// which the converged flight reaches).
        /// </summary>
        public static SimulatedFlight Simulate(Vector3 release, Vector3 velocity, in FlightPlan plan, float gravity, float dt,
            float contactHeight, Vector3 chordEnd)
        {
            var result = new SimulatedFlight();
            var chord = new Vector2(chordEnd.x - release.x, chordEnd.z - release.z);
            var normal = chord.sqrMagnitude > 1e-12f ? new Vector2(-chord.y, chord.x).normalized : Vector2.zero;
            var p = release;
            var v = velocity;
            var g = new Vector3(0f, -gravity, 0f);
            int maxSteps = Mathf.CeilToInt(10f / dt);
            for (int step = 0; step < maxSteps; step++)
            {
                var a = DeliveryFlightModel.Acceleration(step * dt, v, plan);
                v += (g + a) * dt;
                var next = p + v * dt;
                if (next.y <= contactHeight)
                {
                    float f = (p.y - contactHeight) / (p.y - next.y);
                    result.Landed = true;
                    result.Bounce = Vector3.LerpUnclamped(p, next, f);
                    result.Time = (step + f) * dt;
                    result.ArrivalVelocity = v;
                    return result;
                }
                p = next;
                result.Curve = Mathf.Max(result.Curve, Mathf.Abs(Vector2.Dot(new Vector2(p.x - release.x, p.z - release.z), normal)));
            }
            return result;
        }

        static DeliverySolution Fail(DeliverySolution r, string error)
        {
            r.Reachable = false;
            r.Error = error;
            return r;
        }
    }
}
