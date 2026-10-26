using NUnit.Framework;
using UnityEngine;

namespace CricketVR.Tests
{
    /// <summary>M7.5 T073 (AC-26 automated, simulation): swing acceleration and the movement-aware solver.</summary>
    public class SwingSolverTests
    {
        const float G = 9.81f, Dt = 1f / 144f, R = 0.036f;

        static DeliveryTypeData Type(DeliveryTypeData t, float variation = 0f)
        {
            t.variation = variation;
            return t;
        }

        static DeliveryRequest Request(DeliveryTypeData type, float kmh, float line, float length, float movement,
            Handedness hand = Handedness.Right, int seed = 1) => new DeliveryRequest
        {
            label = type.displayName, type = type, speedKmh = kmh, releasePosition = FieldDimensions.DefaultReleasePoint,
            line = line, length = length, movement = movement, handedness = hand, seed = seed,
        };

        static DeliverySolution Solve(DeliveryRequest request, out DeliveryRequest world, out DeliveryMovement movement)
        {
            world = request.ForBatter();
            movement = DeliveryMovement.For(world);
            return DeliverySolver.Solve(world, movement, G, Dt, R);
        }

        static DeliverySolver.SimulatedFlight Fly(DeliveryRequest world, DeliverySolution s) =>
            DeliverySolver.Simulate(world.releasePosition, s.Velocity, s.Plan, G, Dt, R, s.Target);

        [Test]
        public void SwingingBallsLandWithinOneMillimetreAndCurveAsRequested()
        {
            int solved = 0;
            float worstMiss = 0f, worstCurve = 0f, maxAccel = 0f, maxTurn = 0f, maxDisplacement = 0f;
            foreach (var type in new[] { Type(DeliveryTypeData.Inswinger), Type(DeliveryTypeData.Outswinger) })
            foreach (var hand in new[] { Handedness.Right, Handedness.Left })
            foreach (var kmh in new[] { 115f, 120f, 125f, 140f })
            foreach (var movement in new[] { 0f, 0.5f, 1f })
            foreach (var length in new[] { 2f, 4f, 6f, 8f })
            foreach (var line in new[] { -0.1f, 0f, 0.3f })
            {
                var s = Solve(Request(type, kmh, line, length, movement, hand), out var world, out var mv);
                Assert.IsTrue(s.Reachable, $"{world}: {s.Error}");
                Assert.AreEqual(Units.KmhToMs(kmh), s.Velocity.magnitude, 1e-3f, "the requested speed is never changed");
                var flight = Fly(world, s);
                float miss = Vector2.Distance(new Vector2(flight.Bounce.x, flight.Bounce.z), new Vector2(world.line, world.length));
                Assert.LessOrEqual(miss, 0.001f, $"{world}: lands {miss * 1000f:F2} mm from the request");
                Assert.AreEqual(mv.SwingCurve, flight.Curve, 0.001f, $"{world}: curve");
                Assert.AreEqual(flight.Curve, s.PredictedCurve, 1e-5f);
                Assert.AreEqual(flight.Time, s.Plan.PlannedTime, 1e-3f, "the plan's T is the flight's own time");
                worstMiss = Mathf.Max(worstMiss, miss);
                // Size of the movement, for the calibration record: sideways acceleration, change of heading, and the
                // sideways distance from the line the ball was launched on.
                var launchHeading = new Vector3(s.Velocity.x, 0f, s.Velocity.z).normalized;
                var arrivalHeading = new Vector3(flight.ArrivalVelocity.x, 0f, flight.ArrivalVelocity.z).normalized;
                var offLaunchLine = flight.Bounce - world.releasePosition;
                offLaunchLine.y = 0f;
                maxAccel = Mathf.Max(maxAccel, s.Plan.SwingAcceleration);
                maxTurn = Mathf.Max(maxTurn, Vector3.Angle(launchHeading, arrivalHeading));
                maxDisplacement = Mathf.Max(maxDisplacement, Vector3.Cross(launchHeading, offLaunchLine).magnitude);
                worstCurve = Mathf.Max(worstCurve, Mathf.Abs(flight.Curve - mv.SwingCurve));
                solved++;
            }
            Debug.Log($"[CricketVR] T073: {solved} swing solves, worst landing miss {worstMiss * 1000f:F3} mm, worst curve error {worstCurve * 1000f:F3} mm; " +
                      $"largest swing acceleration {maxAccel:F1} m/s^2 ({maxAccel / G:F1} g), heading change {maxTurn:F1} deg, " +
                      $"distance from the launch line {maxDisplacement:F2} m");
            foreach (var p in new[] { 0f, 1.5f, 2f })
            foreach (var curve in new[] { 0.2f, 0.4f, 0.6f })
            {
                var t = Type(DeliveryTypeData.Outswinger);
                t.swingMin = t.swingMax = curve;
                t.swingLateness = p;
                foreach (var kmh in new[] { 115f, 140f })
                {
                    var s = Solve(Request(t, kmh, 0f, 6f, 0f), out var world, out _);
                    var flight = Fly(world, s);
                    var h0 = new Vector3(s.Velocity.x, 0f, s.Velocity.z).normalized;
                    var h1 = new Vector3(flight.ArrivalVelocity.x, 0f, flight.ArrivalVelocity.z).normalized;
                    var off = flight.Bounce - world.releasePosition;
                    off.y = 0f;
                    Debug.Log($"[CricketVR] T073 size: curve {curve:F1} m, lateness {p:F1}, {kmh:F0} km/h: A {s.Plan.SwingAcceleration:F1} m/s^2, " +
                              $"heading change {Vector3.Angle(h0, h1):F1} deg, distance from the launch line {Vector3.Cross(h0, off).magnitude:F2} m, T {s.Plan.PlannedTime:F3} s");
                }
            }
        }

        [Test]
        public void SwingDirectionPerTypeAndHand()
        {
            // Sideways velocity gained between release and the pitch point: inswing toward −X (into a right-hander),
            // outswing toward +X, both mirrored for a left-hander. The ball starts wider and curves in.
            var cases = new[]
            {
                (DeliveryTypeData.Inswinger, Handedness.Right, -1f), (DeliveryTypeData.Inswinger, Handedness.Left, +1f),
                (DeliveryTypeData.Outswinger, Handedness.Right, +1f), (DeliveryTypeData.Outswinger, Handedness.Left, -1f),
            };
            foreach (var (type, hand, expected) in cases)
            {
                var s = Solve(Request(Type(type), 120f, 0f, 6f, 0.5f, hand), out var world, out _);
                var flight = Fly(world, s);
                float gained = flight.ArrivalVelocity.x - s.Velocity.x;
                Assert.AreEqual(expected, Mathf.Sign(gained), $"{type.displayName} {hand}: sideways velocity gained {gained:F3} m/s");
                // Release and the requested line are both on the middle-stump line (x = 0), so the aim shows the start wide.
                Assert.AreEqual(-expected, Mathf.Sign(s.Velocity.x), $"{type.displayName} {hand}: aimed wide of the line first");
            }
        }

        [Test]
        public void LeftHandedSwingIsTheExactMirror()
        {
            foreach (var type in new[] { Type(DeliveryTypeData.Inswinger), Type(DeliveryTypeData.Outswinger) })
            {
                var right = Solve(Request(type, 118f, 0.2f, 5f, 0.7f), out var rw, out _);
                var left = Solve(Request(type, 118f, 0.2f, 5f, 0.7f, Handedness.Left), out var lw, out _);
                Assert.AreEqual(-right.Velocity.x, left.Velocity.x, 1e-4f, type.displayName);
                Assert.AreEqual(right.Velocity.y, left.Velocity.y, 1e-4f);
                Assert.AreEqual(right.Velocity.z, left.Velocity.z, 1e-4f);
                Assert.AreEqual(right.PredictedCurve, left.PredictedCurve, 1e-4f);
                Assert.AreEqual(-right.PredictedBounce.x, left.PredictedBounce.x, 1e-4f);
            }
        }

        [Test]
        public void SolvesAreDeterministic()
        {
            var a = Solve(Request(Type(DeliveryTypeData.Outswinger, 0.15f), 122f, 0.1f, 6.5f, 0.3f, seed: 77), out _, out var ma);
            var b = Solve(Request(Type(DeliveryTypeData.Outswinger, 0.15f), 122f, 0.1f, 6.5f, 0.3f, seed: 77), out _, out var mb);
            Assert.AreEqual(a.Velocity, b.Velocity);
            Assert.AreEqual(a.Plan.SwingAcceleration, b.Plan.SwingAcceleration);
            Assert.AreEqual(a.Plan.PlannedTime, b.Plan.PlannedTime);
            Assert.AreEqual(ma.VarSwing, mb.VarSwing);
        }

        [Test]
        public void NaturalVariationIsAppliedBeforeSolving()
        {
            var nominal = Type(DeliveryTypeData.Inswinger);
            var varied = Type(DeliveryTypeData.Inswinger, 0.15f);
            for (int seed = 1; seed <= 20; seed++)
            {
                var s = Solve(Request(varied, 120f, 0f, 6f, 0.5f, seed: seed), out var world, out var mv);
                Assert.IsTrue(s.Reachable, s.Error);
                var flight = Fly(world, s);
                float d = Mathf.Lerp(nominal.swingMin, nominal.swingMax, 0.5f) * mv.VarSwing;
                Assert.AreEqual(d, flight.Curve, 0.001f, $"seed {seed}: curve = nominal x {mv.VarSwing:F3}");
                Assert.AreEqual(world.length, flight.Bounce.z, 0.001f, $"seed {seed}: still lands where requested");
                Assert.AreEqual(world.line, flight.Bounce.x, 0.001f);
            }
        }

        [Test]
        public void UnreachableRequestsAreRejectedNotAltered()
        {
            var slow = Solve(Request(Type(DeliveryTypeData.Inswinger), 30f, 0f, 6f, 0.5f), out _, out _);
            Assert.IsFalse(slow.Reachable);
            StringAssert.Contains("needs at least", slow.Error);

            var world = Request(Type(DeliveryTypeData.Outswinger), 120f, 0f, 6f, 1f).ForBatter();
            var tooMuch = DeliveryMovement.For(world);
            tooMuch.SwingCurve = 8f; // far outside the allowed range: no aim can do it
            var s = DeliverySolver.Solve(world, tooMuch, G, Dt, R);
            Assert.IsFalse(s.Reachable, "an 8 m curve is rejected");
            Assert.IsFalse(string.IsNullOrEmpty(s.Error));
        }

        [Test]
        public void StockRequestsUseTheUnchangedStockSolve()
        {
            var config = DeliveryConfig.Create("Good length", 80f, 0.1f, 6f);
            var world = DeliveryRequest.FromConfig(config).ForBatter();
            var viaRequest = DeliverySolver.Solve(world, DeliveryMovement.For(world), G, Dt, R);
            var direct = DeliverySolver.Solve(config, G, Dt, R);
            Assert.AreEqual(direct.Velocity, viaRequest.Velocity);
            Assert.AreEqual(direct.PredictedBounce, viaRequest.PredictedBounce);
            Assert.IsTrue(viaRequest.Plan.IsStill);
        }

        [Test]
        public void SwingAccelerationFormulaGivesTheChordDeviation()
        {
            // Small-angle check of the closed form against a direct 1-D integration (no gravity, chord along the flight).
            foreach (var p in new[] { 0f, 1f, 1.5f, 2f })
            {
                const float T = 0.6f, D = 0.4f;
                float a = DeliveryFlightModel.SwingAccelerationFor(D, T, p);
                int n = 60000;
                double dt = T / (double)n, y = 0, v = 0, maxDev = 0;
                var ys = new double[n + 1];
                for (int i = 0; i < n; i++)
                {
                    v += a * System.Math.Pow(i * dt / T, p) * dt;
                    y += v * dt;
                    ys[i + 1] = y;
                }
                for (int i = 0; i <= n; i++)
                    maxDev = System.Math.Max(maxDev, System.Math.Abs(ys[i] - ys[n] * i / n));
                Assert.AreEqual(D, maxDev, 0.002, $"lateness {p}");
            }
        }
    }
}
