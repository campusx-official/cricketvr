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

        // ---------- T079: arrival-line requests (D-1) ----------

        static readonly DeliveryTypeData[] AllMoving =
        {
            DeliveryTypeData.Stock, DeliveryTypeData.Inswinger, DeliveryTypeData.Outswinger, DeliveryTypeData.OffSpin,
            DeliveryTypeData.LegSpin, DeliveryTypeData.Doosra, DeliveryTypeData.Googly,
        };

        static DeliveryRequest AtBatter(DeliveryTypeData type, float kmh, float line, float length, float movement, Handedness hand)
        {
            var r = Request(type, kmh, line, length, movement, hand);
            r.lineReference = LineReference.AtBatter;
            return r;
        }

        [Test]
        public void ArrivalLineRequestsReachTheirCreaseLineInSimulation()
        {
            int solved = 0;
            float worst = 0f, widest = 0f;
            foreach (var type in AllMoving)
            foreach (var hand in new[] { Handedness.Right, Handedness.Left })
            foreach (var length in new[] { 1f, 3f, 6f, 8f })
            foreach (var line in new[] { -0.1f, 0.1f, 0.3f })
            foreach (var movement in new[] { 0f, 1f })
            {
                float kmh = type.UsesPlanSpeed ? 90f : Mathf.Round(0.5f * (type.minSpeedKmh + type.maxSpeedKmh));
                var world = AtBatter(type, kmh, line, length, movement, hand).ForBatter();
                var s = DeliverySolver.Solve(world, DeliveryMovement.For(world), G, Dt, R);
                Assert.IsTrue(s.Reachable, $"{world}: {s.Error}");
                var flight = Fly(world, s);
                float miss = Mathf.Abs(flight.CreaseX - world.line);
                Assert.LessOrEqual(miss, 0.001f, $"{world}: finishes {miss * 1000f:F2} mm off its line");
                Assert.AreEqual(flight.CreaseX, s.PredictedArrivalLine, 1e-5f);
                // Stock keeps its own unchanged solve, which lands within 5 mm along the ground (RefineTolerance).
                Assert.AreEqual(world.length, flight.Bounce.z, type.IsStill ? 0.005f : 0.001f, $"{world}: still pitches at its length");
                worst = Mathf.Max(worst, miss);
                widest = Mathf.Max(widest, Mathf.Abs(s.Target.x - world.line));
                solved++;
            }
            Debug.Log($"[CricketVR] T079: {solved} arrival-line solves (7 types, lengths 1-8 m), worst crease miss {worst * 1000f:F3} mm, " +
                      $"largest pitch-to-crease line difference {widest:F2} m");
        }

        [Test]
        public void OffSpinnerPitchedOutsideOffTurnsInToFinishOnMiddle()
        {
            // Right-hander, off-spin turns toward −X (leg): asking to finish on middle pitches it outside off.
            var world = AtBatter(DeliveryTypeData.OffSpin, 85f, 0f, 6f, 1f, Handedness.Right).ForBatter();
            var s = DeliverySolver.Solve(world, DeliveryMovement.For(world), G, Dt, R);
            Assert.IsTrue(s.Reachable, s.Error);
            Assert.Greater(s.Target.x, 0.1f, $"pitches at {s.Target.x:+0.00} m, outside off");
            Assert.AreEqual(0f, s.PredictedArrivalLine, 0.001f);
        }

        [Test]
        public void BounceLineRequestsHaveNoArrivalLineAndAreUnchanged()
        {
            var world = Request(DeliveryTypeData.LegSpin, 85f, 0.1f, 6f, 0.5f).ForBatter();
            var s = DeliverySolver.Solve(world, DeliveryMovement.For(world), G, Dt, R);
            Assert.IsTrue(float.IsNaN(s.PredictedArrivalLine));
            Assert.AreEqual(0.1f, s.Target.x, 1e-6f);
        }

        [Test]
        public void SpinCheckSequenceIsReachableAndInOrder()
        {
            var types = DeliveryTypeData.All;
            for (int i = 0; i < 12; i++)
            foreach (var hand in new[] { Handedness.Right, Handedness.Left })
            {
                var request = DeliveryPlanner.SpinCheckRequest(i, hand, types);
                var world = request.ForBatter();
                var mv = DeliveryMovement.For(world);
                var s = DeliverySolver.Solve(world, mv, G, Dt, R);
                Assert.IsTrue(s.Reachable, $"{world}: {s.Error}");
                Assert.AreEqual(DeliveryPlanner.SpinCheckTypes[i % 4], request.type.kind, $"ball {i + 1}");
                Assert.AreEqual(DeliveryPlanner.SpinCheckLengths[i / 4], request.length, $"ball {i + 1}");
                Assert.AreEqual(1f, mv.VarTurn, "no natural variation");
                Assert.Greater(s.Plan.TurnDegrees, 0f, $"ball {i + 1} turns");
            }
        }

        [Test]
        public void NetPracticeSpinBowlersDrawOnlyTheirTypeAndAreAlwaysReachable()
        {
            var types = DeliveryTypeData.All;
            var reachable = DeliveryPlanner.RequestSolverCheck(G, Dt, R);
            Assert.AreEqual("BowlerPace", NetBowler.All[0].Button);
            Assert.IsTrue(NetBowler.All[0].Plan().StockOnly, "Pace keeps the Stock-only plan");
            foreach (var (bowler, kind) in new[] { (NetBowler.All[1], DeliveryKind.OffSpin), (NetBowler.All[2], DeliveryKind.LegSpin) })
            foreach (var hand in new[] { Handedness.Right, Handedness.Left })
            {
                var plan = bowler.Plan();
                var rng = new System.Random(7);
                int redraws = 0, full = 0, good = 0, shortBalls = 0;
                const int draws = 400;
                for (int n = 0; n < draws; n++)
                {
                    var r = DeliveryPlanner.DrawRequest(plan, rng, hand, types, reachable, out int k);
                    redraws += k;
                    Assert.AreEqual(kind, r.type.kind, $"draw {n}: {r}");
                    Assert.That(r.speedKmh, Is.InRange(r.type.minSpeedKmh, r.type.maxSpeedKmh), $"draw {n}: the type's own speed");
                    Assert.That(r.length, Is.InRange(2.5f, 7.5f), $"draw {n}: spin lengths");
                    if (r.length < NetBowler.SpinGoodLengthMin) { full++; StringAssert.Contains("Full", r.label); }
                    else if (r.length < NetBowler.SpinGoodLengthMax) { good++; StringAssert.Contains("Good length", r.label); }
                    else { shortBalls++; StringAssert.Contains("Short", r.label); }
                }
                Assert.AreEqual(0, redraws, $"{kind} {hand}: every draw reachable");
                // Triangle 2.5..4.75..7.5: about 20% full, 64% good length, 16% short.
                Assert.Greater(good, draws * 55 / 100, $"{kind}: mostly good length ({full}/{good}/{shortBalls})");
                Assert.Greater(full, draws / 10, $"{kind}: some full balls");
                Assert.Greater(shortBalls, draws / 12, $"{kind}: some short balls");
                if (hand == Handedness.Right)
                    Debug.Log($"[CricketVR] Net Practice {bowler.Label}: {draws} draws, full {full}, good length {good}, short {shortBalls}");
            }
        }

        /// <summary>
        /// Owner feedback 2026-10-27: off-spin pitched on middle turned down the leg side. Spinners now aim at where the
        /// ball reaches the crease, released over or around the wicket; every draw is reachable and finishes on its line.
        /// </summary>
        [Test]
        public void NetPracticeSpinnersFinishOnTheirLineFromOverAndAroundTheWicket()
        {
            var types = DeliveryTypeData.All;
            var reachable = DeliveryPlanner.RequestSolverCheck(G, Dt, R);
            foreach (var side in WicketSide.All)
            foreach (var bowler in new[] { NetBowler.All[1], NetBowler.All[2] })
            foreach (var hand in new[] { Handedness.Right, Handedness.Left })
            {
                var plan = bowler.Plan(side.Release);
                var rng = new System.Random(11);
                float pitchSum = 0f, finishSum = 0f, worst = 0f;
                const int draws = 150;
                for (int n = 0; n < draws; n++)
                {
                    var r = DeliveryPlanner.DrawRequest(plan, rng, hand, types, reachable, out int redraws);
                    Assert.AreEqual(0, redraws, $"{side.Label} {bowler.Label} {hand} draw {n}: reachable");
                    Assert.AreEqual(LineReference.AtBatter, r.lineReference);
                    Assert.AreEqual(side.Release, r.releasePosition, "released from the chosen side");
                    Assert.That(r.line, Is.InRange(bowler.MinFinishLine, bowler.MaxFinishLine));
                    var world = r.ForBatter();
                    var s = DeliverySolver.Solve(world, DeliveryMovement.For(world), G, Dt, R);
                    Assert.IsTrue(s.Reachable, s.Error);
                    worst = Mathf.Max(worst, Mathf.Abs(s.PredictedArrivalLine - world.line));
                    float mirror = hand == Handedness.Left ? -1f : 1f; // back to the batter's frame
                    pitchSum += mirror * s.Target.x;
                    finishSum += r.line;
                }
                Assert.LessOrEqual(worst, 0.001f, "finishes on its line in simulation");
                float turnSide = bowler.Mix[0].kind == DeliveryKind.OffSpin ? -1f : +1f; // batter frame: off-spin turns to leg
                if (hand == Handedness.Right)
                    Debug.Log($"[CricketVR] Net Practice {bowler.Label} {side.Label.ToLowerInvariant()} the wicket: average pitch line " +
                              $"{pitchSum / draws:+0.00;-0.00} m, average finish {finishSum / draws:+0.00;-0.00} m");
                Assert.Less(turnSide * (pitchSum - finishSum) / draws, 0f, $"{bowler.Label} {side.Label} {hand}: pitched on the far side of its finish, turning onto it");
            }
        }
    }
}
