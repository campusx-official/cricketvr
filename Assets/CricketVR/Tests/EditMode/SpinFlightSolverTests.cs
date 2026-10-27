using NUnit.Framework;
using UnityEngine;

namespace CricketVR.Tests
{
    /// <summary>
    /// M7.5 T077/T078 (AC-27 automated, simulation): drift and dip before the bounce, the solver that lands a spinning
    /// ball on the requested point, and the turn at the bounce (applied after PhysX's rebound, Option A). The flight
    /// tests zero the turn, bounce and grip factors, which act only after the pitch point.
    /// </summary>
    public class SpinFlightSolverTests
    {
        const float G = 9.81f, Dt = 1f / 144f, R = 0.036f;

        static readonly DeliveryTypeData[] SpinTypes =
            { DeliveryTypeData.OffSpin, DeliveryTypeData.LegSpin, DeliveryTypeData.Doosra, DeliveryTypeData.Googly };

        static DeliveryRequest Request(DeliveryTypeData type, float kmh, float line, float length, float movement,
            Handedness hand = Handedness.Right)
        {
            type.variation = 0f;
            return new DeliveryRequest
            {
                label = type.displayName, type = type, speedKmh = kmh, releasePosition = FieldDimensions.DefaultReleasePoint,
                line = line, length = length, movement = movement, handedness = hand, seed = 1,
            };
        }

        /// <summary>The type's flight before the bounce only: drift and dip, no turn (T078).</summary>
        static DeliveryMovement BeforeTheBounce(in DeliveryRequest world)
        {
            var m = DeliveryMovement.For(world);
            m.TurnDegrees = m.BounceFactor = m.Grip = 0f;
            return m;
        }

        static DeliverySolution Solve(DeliveryRequest request, out DeliveryRequest world, out DeliveryMovement movement)
        {
            world = request.ForBatter();
            movement = BeforeTheBounce(world);
            return DeliverySolver.Solve(world, movement, G, Dt, R);
        }

        static DeliverySolver.SimulatedFlight Fly(DeliveryRequest world, DeliverySolution s) =>
            DeliverySolver.Simulate(world.releasePosition, s.Velocity, s.Plan, G, Dt, s.Target.y, s.Target);

        [Test]
        public void SpinSpeedsSeventyFiveToNinetyFiveReachLengthsThreeToNine()
        {
            int solved = 0;
            float worstMiss = 0f, worstDrift = 0f, maxDriftAccel = 0f;
            foreach (var type in SpinTypes)
            foreach (var hand in new[] { Handedness.Right, Handedness.Left })
            foreach (var kmh in new[] { 75f, 80f, 85f, 90f, 95f })
            foreach (var movement in new[] { 0f, 1f })
            foreach (var length in new[] { 3f, 4.5f, 6f, 7.5f, 9f })
            foreach (var line in new[] { -0.2f, 0f, 0.3f })
            {
                var s = Solve(Request(type, kmh, line, length, movement, hand), out var world, out var mv);
                Assert.IsTrue(s.Reachable, $"{world}: {s.Error}");
                Assert.AreEqual(Units.KmhToMs(kmh), s.Velocity.magnitude, 1e-3f, "the requested speed is never changed");
                var flight = Fly(world, s);
                float miss = Vector2.Distance(new Vector2(flight.Bounce.x, flight.Bounce.z), new Vector2(world.line, world.length));
                Assert.LessOrEqual(miss, 0.001f, $"{world}: lands {miss * 1000f:F2} mm from the request");
                Assert.AreEqual(mv.Drift, Mathf.Abs(flight.Drift), 0.001f, $"{world}: drift by the pitch point");
                Assert.AreEqual(Mathf.Abs(flight.Drift), s.PredictedDrift, 1e-5f);
                Assert.AreEqual(mv.Dip, s.Plan.Dip, "dip is applied as the type sets it");
                worstMiss = Mathf.Max(worstMiss, miss);
                worstDrift = Mathf.Max(worstDrift, Mathf.Abs(Mathf.Abs(flight.Drift) - mv.Drift));
                maxDriftAccel = Mathf.Max(maxDriftAccel, s.Plan.DriftAcceleration);
                solved++;
            }
            Debug.Log($"[CricketVR] T077: {solved} spin solves (75-95 km/h, 3-9 m), worst landing miss {worstMiss * 1000f:F3} mm, " +
                      $"worst drift error {worstDrift * 1000f:F3} mm, largest drift acceleration {maxDriftAccel:F2} m/s^2");
        }

        [Test]
        public void DriftGoesOppositeToTheTurnForEitherHand()
        {
            foreach (var type in SpinTypes)
            foreach (var hand in new[] { Handedness.Right, Handedness.Left })
            {
                var s = Solve(Request(type, 85f, 0f, 6f, 1f, hand), out var world, out var mv);
                Assert.IsTrue(s.Reachable, $"{world}: {s.Error}");
                var flight = Fly(world, s);
                Assert.AreEqual(-world.type.turnDirection, s.Plan.DriftDirection, $"{world}: drift direction");
                Assert.AreEqual(-world.type.turnDirection, (int)Mathf.Sign(flight.Drift), $"{world}: drifted {flight.Drift:F3} m");
                int rightHanded = Request(type, 85f, 0f, 6f, 1f).ForBatter().type.turnDirection;
                Assert.AreEqual(hand == Handedness.Right ? rightHanded : -rightHanded, world.type.turnDirection,
                    "a left-hander sees the mirror image");
            }
        }

        [Test]
        public void GooglyDipsMost()
        {
            // Same speed, line and length: the more a ball dips, the higher it must be launched and the steeper it
            // arrives (so it bounces higher off PhysX's pitch).
            float Arrival(DeliveryTypeData type, out float elevation)
            {
                var s = Solve(Request(type, 85f, 0f, 6f, 0.5f), out var world, out _);
                Assert.IsTrue(s.Reachable, $"{world}: {s.Error}");
                elevation = s.ElevationDegrees;
                var v = Fly(world, s).ArrivalVelocity;
                return Mathf.Atan2(-v.y, new Vector2(v.x, v.z).magnitude) * Mathf.Rad2Deg;
            }

            var stock = DeliverySolver.Solve(DeliveryConfig.Create("stock", 85f, 0f, 6f), G, Dt, R);
            var sv = Fly(Request(DeliveryTypeData.Stock, 85f, 0f, 6f, 0f), stock).ArrivalVelocity;
            float stockArrival = Mathf.Atan2(-sv.y, new Vector2(sv.x, sv.z).magnitude) * Mathf.Rad2Deg;
            float googly = Arrival(DeliveryTypeData.Googly, out float googlyUp);
            Debug.Log($"[CricketVR] T077 dip at 85 km/h, 6 m: Stock arrives {stockArrival:F2} deg (launch {stock.ElevationDegrees:F2}), " +
                      $"Googly {googly:F2} deg (launch {googlyUp:F2})");
            foreach (var type in new[] { DeliveryTypeData.OffSpin, DeliveryTypeData.LegSpin, DeliveryTypeData.Doosra })
            {
                float other = Arrival(type, out float otherUp);
                Debug.Log($"[CricketVR] T077 dip: {type.displayName} arrives {other:F2} deg (launch {otherUp:F2})");
                Assert.Greater(googly, other, $"googly arrives steeper than {type.displayName}");
                Assert.Greater(googlyUp, otherUp, $"googly is launched higher than {type.displayName}");
                Assert.Greater(other, stockArrival, $"{type.displayName} dips more than Stock");
            }
        }

        [Test]
        public void DriftAccelerationFormulaGivesTheDriftAtThePitchPoint()
        {
            // a = 2d / T²: with no other movement the stepped flight drifts d to within the step error, before rescale.
            foreach (var d in new[] { 0.05f, 0.12f })
            foreach (var T in new[] { 0.6f, 0.9f })
            {
                float a = DeliveryFlightModel.DriftAccelerationFor(d, T);
                Assert.AreEqual(d, 0.5 * a * T * T, 1e-6, $"d {d} m, T {T} s");
            }
            Assert.AreEqual(0f, DeliveryFlightModel.DriftAccelerationFor(0f, 0.8f));
        }

        [Test]
        public void TheTurnRidesInThePlanAndLeavesTheFlightToThePitchUnchanged()
        {
            foreach (var type in SpinTypes)
            foreach (var hand in new[] { Handedness.Right, Handedness.Left })
            {
                var world = Request(type, 85f, 0.1f, 6f, 0.5f, hand).ForBatter();
                var full = DeliveryMovement.For(world);
                var s = DeliverySolver.Solve(world, full, G, Dt, R);
                Assert.IsTrue(s.Reachable, $"{world}: {s.Error}");
                Assert.AreEqual(world.type.turnDirection, s.Plan.TurnDirection, $"{world}: turn direction");
                Assert.AreEqual(full.TurnDegrees, s.Plan.TurnDegrees, 1e-6f);
                Assert.AreEqual(full.BounceFactor, s.Plan.BounceFactor);
                Assert.AreEqual(full.Grip, s.Plan.Grip);
                var before = Solve(Request(type, 85f, 0.1f, 6f, 0.5f, hand), out _, out _);
                Assert.AreEqual(before.Velocity, s.Velocity, $"{world}: the turn doesn't change the launch");
            }
        }

        [Test]
        public void TurnAtBounceTurnsTheHeadingTowardItsDirectionAndAppliesGripAndBounce()
        {
            var plan = new FlightPlan { TurnDirection = +1, TurnDegrees = 6f, BounceFactor = 0.1f, Grip = 0.05f };
            foreach (var v in new[] { new Vector3(0.2f, 4f, -20f), new Vector3(-0.1f, 3f, 15f) })
            foreach (int dir in new[] { -1, +1 })
            {
                plan.TurnDirection = dir;
                var after = DeliveryFlightModel.TurnAtBounce(v, plan);
                var h0 = new Vector3(v.x, 0f, v.z);
                var h1 = new Vector3(after.x, 0f, after.z);
                Assert.AreEqual(6f, Vector3.Angle(h0, h1), 1e-3f, $"{v} dir {dir}: heading change");
                Assert.AreEqual(dir, (int)Mathf.Sign(h1.normalized.x - h0.normalized.x), $"{v} dir {dir}: turns toward {dir}");
                Assert.AreEqual(h0.magnitude * 0.95f, h1.magnitude, 1e-4f, "grip");
                Assert.AreEqual(v.y * 1.1f, after.y, 1e-5f, "bounce factor");
            }
            Assert.AreEqual(new Vector3(1f, 2f, -3f), DeliveryFlightModel.TurnAtBounce(new Vector3(1f, 2f, -3f), default), "no turn: unchanged");
        }

        [Test]
        public void SwingPlansAreUnchangedByTheDriftAndDipTerms()
        {
            var plan = new FlightPlan { SwingDirection = -1, SwingAcceleration = 12f, SwingLateness = 1.5f, PlannedTime = 0.4f };
            var v = new Vector3(0.3f, -2f, -33f);
            var a = DeliveryFlightModel.Acceleration(0.3f, v, plan);
            var side = new Vector3(-v.z, 0f, v.x).normalized; // toward +X for a ball travelling −Z
            Assert.AreEqual(0f, a.y);
            Assert.AreEqual(-12f * Mathf.Pow(0.75f, 1.5f), Vector3.Dot(a, side), 1e-4f);
        }
    }
}
