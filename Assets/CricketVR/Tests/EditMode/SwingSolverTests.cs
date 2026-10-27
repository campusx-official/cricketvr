using NUnit.Framework;
using UnityEngine;

namespace CricketVR.Tests
{
    /// <summary>
    /// M7.5 T073 (AC-26 automated, simulation): swing acceleration and the movement-aware solver. Swing = the ball's
    /// distance at the striker's popping crease from the line it was released along (owner decision 2026-10-27).
    /// </summary>
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
            DeliverySolver.Simulate(world.releasePosition, s.Velocity, s.Plan, G, Dt, s.Target.y, s.Target);

        [Test]
        public void SwingingBallsLandWithinOneMillimetreAndSwingAsRequested()
        {
            int solved = 0;
            float worstMiss = 0f, worstSwing = 0f, maxAccel = 0f, maxTurn = 0f, maxCurve = 0f;
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
                Assert.AreEqual(mv.Swing, Mathf.Abs(flight.Swing), 0.001f, $"{world}: swing at the crease");
                Assert.AreEqual(Mathf.Abs(flight.Swing), s.PredictedSwing, 1e-5f);
                Assert.AreEqual(flight.Curve, s.PredictedCurve, 1e-5f);
                Assert.AreEqual(flight.Time, s.Plan.PlannedTime, 1e-3f, "the plan's T is the flight's own time");
                worstMiss = Mathf.Max(worstMiss, miss);
                // Size of the movement, for the calibration record: sideways acceleration, change of heading, curve.
                var launchHeading = new Vector3(s.Velocity.x, 0f, s.Velocity.z).normalized;
                var arrivalHeading = new Vector3(flight.ArrivalVelocity.x, 0f, flight.ArrivalVelocity.z).normalized;
                maxAccel = Mathf.Max(maxAccel, s.Plan.SwingAcceleration);
                maxTurn = Mathf.Max(maxTurn, Vector3.Angle(launchHeading, arrivalHeading));
                maxCurve = Mathf.Max(maxCurve, flight.Curve);
                worstSwing = Mathf.Max(worstSwing, Mathf.Abs(Mathf.Abs(flight.Swing) - mv.Swing));
                solved++;
            }
            Debug.Log($"[CricketVR] T073: {solved} swing solves, worst landing miss {worstMiss * 1000f:F3} mm, worst swing error {worstSwing * 1000f:F3} mm; " +
                      $"largest swing acceleration {maxAccel:F1} m/s^2 ({maxAccel / G:F1} g), heading change {maxTurn:F1} deg, curve {maxCurve:F2} m");
            foreach (var p in new[] { 0f, 1.5f, 2f })
            foreach (var swing in new[] { 0.1f, 0.3f, 0.5f })
            {
                var t = Type(DeliveryTypeData.Outswinger);
                t.swingMin = t.swingMax = swing;
                t.swingLateness = p;
                foreach (var kmh in new[] { 115f, 140f })
                {
                    var s = Solve(Request(t, kmh, 0f, 6f, 0f), out var world, out _);
                    var flight = Fly(world, s);
                    var h0 = new Vector3(s.Velocity.x, 0f, s.Velocity.z).normalized;
                    var h1 = new Vector3(flight.ArrivalVelocity.x, 0f, flight.ArrivalVelocity.z).normalized;
                    Debug.Log($"[CricketVR] T073 size: swing {swing:F1} m, lateness {p:F1}, {kmh:F0} km/h: A {s.Plan.SwingAcceleration:F1} m/s^2, " +
                              $"heading change {Vector3.Angle(h0, h1):F1} deg, curve {flight.Curve:F3} m, T {s.Plan.PlannedTime:F3} s");
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
                Assert.AreEqual(right.PredictedSwing, left.PredictedSwing, 1e-4f);
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
                Assert.AreEqual(d, Mathf.Abs(flight.Swing), 0.001f, $"seed {seed}: swing = nominal x {mv.VarSwing:F3}");
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
            tooMuch.Swing = 8f; // far outside the allowed range: no aim can do it
            var s = DeliverySolver.Solve(world, tooMuch, G, Dt, R);
            Assert.IsFalse(s.Reachable, "an 8 m swing is rejected");
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
        public void SwingAccelerationFormulaGivesTheSwingAtTheCrease()
        {
            // Small-angle check of the closed form against a direct 1-D integration (no gravity): sideways distance
            // from the release line at the pitch point, then straight on for the pitch-to-crease time.
            foreach (var p in new[] { 0f, 1f, 1.5f, 2f })
            foreach (var tau in new[] { 0.15f, -0.01f })
            {
                const float T = 0.6f, S = 0.4f;
                float a = DeliveryFlightModel.SwingAccelerationFor(S, T, p, tau);
                int n = 60000;
                double dt = T / (double)n, y = 0, v = 0;
                for (int i = 0; i < n; i++)
                {
                    v += a * System.Math.Pow(i * dt / T, p) * dt;
                    y += v * dt;
                }
                Assert.AreEqual(S, y + v * tau, 0.002, $"lateness {p}, pitch to crease {tau} s");
            }
        }

        // ---------- Owner decision 2026-10-27: swing size 0.1-0.5 m, never over 0.5 m ----------

        [Test]
        public void InswingerPitchedOnOffStumpFinishesWithinHalfAMetreOfItsReleaseLine()
        {
            // The largest swing the game can produce: movement 1 with the most generous variation factors there are.
            float worst = 0f;
            foreach (var variation in new[] { 0f, 0.15f, 0.5f })
            foreach (var hand in new[] { Handedness.Right, Handedness.Left })
            foreach (var kmh in new[] { 115f, 120f, 130f, 140f })
            foreach (var length in new[] { 2f, 4f, 6f, 8f })
            for (int seed = 1; seed <= (variation > 0f ? 10 : 1); seed++)
            {
                var request = Request(Type(DeliveryTypeData.Inswinger, variation), kmh, DeliveryPlanner.OffStumpLine, length, 1f, hand, seed);
                var s = Solve(request, out var world, out var mv);
                Assert.IsTrue(s.Reachable, $"{world}: {s.Error}");
                var flight = Fly(world, s);
                float swing = Mathf.Abs(flight.Swing);
                Assert.LessOrEqual(mv.Swing, DeliveryTypeData.MaxSwing, "the cap applies after variation");
                Assert.LessOrEqual(swing, DeliveryTypeData.MaxSwing + 0.001f, $"{world}: {swing:F3} m off the release line at the crease");
                Assert.AreEqual(hand == Handedness.Right ? -1f : 1f, Mathf.Sign(flight.Swing), $"{world}: swings in toward the pads");
                worst = Mathf.Max(worst, swing);
            }
            Debug.Log($"[CricketVR] Swing cap: inswinger on off stump, movement 1, variation to 0.5: largest swing {worst:F4} m");
        }

        [Test]
        public void SwingTypesRangeFromTenToFiftyCentimetres()
        {
            foreach (var type in new[] { DeliveryTypeData.Inswinger, DeliveryTypeData.Outswinger })
            {
                Assert.AreEqual(0.1f, type.swingMin, type.displayName);
                Assert.AreEqual(0.5f, type.swingMax, type.displayName);
            }
            var tooBig = DeliveryTypeData.Outswinger;
            tooBig.swingMax = 0.6f;
            Assert.IsTrue(tooBig.Validate().Exists(e => e.Contains("swingMax")), "over 0.5 m needs the (future) exaggerated setting");
        }

        [Test]
        public void SwingCheckSequenceIsReachableAndAlternates()
        {
            var types = DeliveryTypeData.All;
            for (int i = 0; i < 12; i++)
            foreach (var hand in new[] { Handedness.Right, Handedness.Left })
            {
                var request = DeliveryPlanner.SwingCheckRequest(i, hand, types);
                var s = Solve(request, out var world, out var mv);
                Assert.IsTrue(s.Reachable, $"{world}: {s.Error}");
                Assert.AreEqual(i % 2 == 0 ? DeliveryKind.Inswinger : DeliveryKind.Outswinger, request.type.kind, $"ball {i + 1}");
                Assert.AreEqual(DeliveryPlanner.SwingCheckSizes[i % 6 / 2], mv.Swing, 1e-5f, $"ball {i + 1}: exact size, no variation");
                Assert.AreEqual(i < 6 ? 120f : DeliveryPlanSettings.DefaultPaceCapKmh, request.speedKmh, $"ball {i + 1}");
                Assert.AreEqual(mv.Swing, Mathf.Abs(Fly(world, s).Swing), 0.001f);
            }
        }

        // ---------- Seam visual (T074; AC-28 rate limit) ----------

        [Test]
        public void VisualSpinNeverExceedsTwentyFiveDegreesPerFrameAt72Hz()
        {
            foreach (var t in DeliveryTypeData.All)
                Assert.LessOrEqual(BallSpinVisual.DegreesThisFrame(t.visualRevPerSecond, 1f / 72f), BallSpinVisual.MaxDegreesPerFrame, t.displayName);
            Assert.AreEqual(25f, BallSpinVisual.DegreesThisFrame(5f, 1f / 72f), 1e-4f, "5 rev/s is exactly the cap");
            Assert.AreEqual(25f, BallSpinVisual.DegreesThisFrame(30f, 1f / 72f), "a real 30 rev/s is shown at the cap");
            Assert.AreEqual(15f, BallSpinVisual.DegreesThisFrame(3f, 1f / 72f), 1e-4f, "pace 3 rev/s");
            Assert.AreEqual(25f, BallSpinVisual.DegreesThisFrame(3f, 0.5f), "a long frame is still capped");
        }

        [Test]
        public void PaceSeamIsUprightWithBackspinAndAngledTowardFineLegOrSlip()
        {
            var travel = new Vector3(0.02f, -0.1f, -1f);
            foreach (var swing in new[] { -1, 0, 1 })
            {
                var axis = BallSpinVisual.PaceSpinAxis(travel, swing);
                Assert.AreEqual(1f, axis.magnitude, 1e-4f);
                Assert.AreEqual(0f, axis.y, 1e-5f, "upright seam: horizontal spin axis");
                var top = Quaternion.AngleAxis(5f, axis) * Vector3.up;
                Assert.Greater(top.z - Vector3.up.z, 0f, $"swing {swing}: the top turns back toward the bowler (backspin)");
                // The seam plane's horizontal direction is perpendicular to the axis; it leans toward x = swing.
                var seam = Vector3.Cross(axis, Vector3.up);
                if (seam.z > 0f) seam = -seam; // the end pointing down the pitch
                float lean = seam.x - travel.normalized.x;
                if (swing == 0) Assert.AreEqual(0f, lean, 0.01f, "Stock seam straight");
                else Assert.AreEqual(swing, Mathf.Sign(lean), $"swing {swing}: seam toward {(swing < 0 ? "fine leg" : "slip")}");
            }
        }
    }
}
