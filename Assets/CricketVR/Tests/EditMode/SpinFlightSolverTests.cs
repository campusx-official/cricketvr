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
                    Assert.IsTrue(System.Array.Exists(bowler.Mix, w => w.kind == r.type.kind), $"draw {n}: {r} is in the {bowler.Label} bowler's mix");
                    Assert.That(r.speedKmh, r.flighted ? Is.InRange(r.type.flightedMinKmh, r.type.flightedMaxKmh) : Is.InRange(r.type.minSpeedKmh, r.type.maxSpeedKmh),
                        $"draw {n}: the type's own speed");
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
                int stock = 0;
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
                    if (r.type.kind == bowler.Mix[0].kind) // the stock ball; the off-spinner's variations turn otherwise
                    {
                        pitchSum += mirror * s.Target.x;
                        finishSum += r.line;
                        stock++;
                    }
                }
                Assert.LessOrEqual(worst, 0.001f, "finishes on its line in simulation");
                float turnSide = bowler.Mix[0].kind == DeliveryKind.OffSpin ? -1f : +1f; // batter frame: off-spin turns to leg
                if (hand == Handedness.Right)
                    Debug.Log($"[CricketVR] Net Practice {bowler.Label} {side.Label.ToLowerInvariant()} the wicket: average pitch line " +
                              $"{pitchSum / stock:+0.00;-0.00} m, average finish {finishSum / stock:+0.00;-0.00} m");
                Assert.Less(turnSide * (pitchSum - finishSum) / stock, 0f, $"{bowler.Label} {side.Label} {hand}: pitched on the far side of its finish, turning onto it");
            }
        }

        // ---------- Flighted off-spin (owner request 2026-10-27) ----------

        static DeliveryRequest FlightedOffSpin(float kmh, float launch, float line, float length, Handedness hand, LineReference reference)
        {
            var r = Request(DeliveryTypeData.OffSpin, kmh, line, length, 0.5f, hand);
            r.flighted = true;
            r.launchDegrees = launch;
            r.lineReference = reference;
            return r;
        }

        [Test]
        public void FlightedOffSpinLeavesAtItsLaunchAngleAndStillLandsOnLengthAndLine()
        {
            int solved = 0, clamped = 0;
            float worstMiss = 0f, maxDip = 0f, minDip = float.MaxValue, lowest = float.MaxValue, highest = 0f;
            foreach (var reference in new[] { LineReference.AtBounce, LineReference.AtBatter })
            foreach (var hand in new[] { Handedness.Right, Handedness.Left })
            foreach (var kmh in new[] { 70f, 75f, 80f })
            foreach (var launch in new[] { 4f, 7f, 10f })
            foreach (var length in new[] { 3f, 4.75f, 6.5f })
            foreach (var release in new[] { FieldDimensions.OverTheWicketRelease, FieldDimensions.AroundTheWicketRelease })
            {
                var r = FlightedOffSpin(kmh, launch, 0.05f, length, hand, reference);
                r.releasePosition = release;
                var world = r.ForBatter();
                var mv = DeliveryMovement.For(world);
                var s = DeliverySolver.Solve(world, mv, G, Dt, R);
                Assert.IsTrue(s.Reachable, $"{world}: {s.Error}");
                if (s.Plan.Dip <= 0f)
                    Assert.GreaterOrEqual(s.ElevationDegrees, launch - 0.05f, $"{world}: gravity alone launches it higher");
                else if (s.Plan.Dip >= 10f - 1e-4f)
                    Assert.LessOrEqual(s.ElevationDegrees, launch + 0.05f, $"{world}: as high as the dip limit allows");
                else
                    Assert.AreEqual(launch, s.ElevationDegrees, 0.05f, $"{world}: launch angle");
                if (Mathf.Abs(s.ElevationDegrees - launch) > 0.05f) clamped++;
                Assert.AreEqual(Units.KmhToMs(kmh), s.Velocity.magnitude, 1e-3f, "speed unchanged");
                var flight = Fly(world, s);
                float miss = reference == LineReference.AtBounce
                    ? Vector2.Distance(new Vector2(flight.Bounce.x, flight.Bounce.z), new Vector2(world.line, world.length))
                    : Mathf.Abs(flight.CreaseX - world.line);
                Assert.LessOrEqual(miss, 0.001f, $"{world}: {(reference == LineReference.AtBounce ? "pitch" : "crease line")} miss {miss * 1000f:F2} mm");
                Assert.AreEqual(world.length, flight.Bounce.z, 0.001f, $"{world}: length");
                lowest = Mathf.Min(lowest, s.ElevationDegrees);
                highest = Mathf.Max(highest, s.ElevationDegrees);
                worstMiss = Mathf.Max(worstMiss, miss);
                maxDip = Mathf.Max(maxDip, s.Plan.Dip);
                minDip = Mathf.Min(minDip, s.Plan.Dip);
                solved++;
            }
            Debug.Log($"[CricketVR] Flighted off-spin: {solved} solves (70-80 km/h, launch 4-10 deg, 3-6.5 m, over/around, both line references), " +
                      $"launched {lowest:F1}-{highest:F1} deg ({clamped} clamped to what 0-10 m/s^2 of dip can land), worst miss {worstMiss * 1000f:F2} mm, dip {minDip:F1}-{maxDip:F1} m/s^2");
        }

        [Test]
        public void OffSpinnerFlightsAboutOneBallInFour()
        {
            var types = DeliveryTypeData.All;
            var reachable = DeliveryPlanner.RequestSolverCheck(G, Dt, R);
            var plan = NetBowler.All[1].Plan(FieldDimensions.OverTheWicketRelease);
            var rng = new System.Random(21);
            int flighted = 0, redraws = 0;
            const int draws = 400;
            for (int n = 0; n < draws; n++)
            {
                var r = DeliveryPlanner.DrawRequest(plan, rng, Handedness.Right, types, reachable, out int k);
                redraws += k;
                if (r.flighted)
                {
                    flighted++;
                    Assert.That(r.speedKmh, Is.InRange(70f, 80f), "a flighted ball is slower");
                    Assert.That(r.launchDegrees, Is.InRange(4f, 10f));
                    StringAssert.Contains("(flighted)", r.label);
                }
                else
                {
                    Assert.That(r.speedKmh, Is.InRange(r.type.minSpeedKmh, r.type.maxSpeedKmh));
                }
            }
            Debug.Log($"[CricketVR] Off-spinner: {flighted} of {draws} balls flighted, {redraws} redraws");
            Assert.That(flighted, Is.InRange(draws * 15 / 100, draws * 35 / 100), "about a quarter flighted");
            Assert.AreEqual(0, redraws, "every flighted and flat ball reachable");
            var leg = NetBowler.All[2].Plan();
            var legRng = new System.Random(21);
            for (int n = 0; n < 100; n++)
                Assert.IsFalse(DeliveryPlanner.DrawRequest(leg, legRng, Handedness.Right, types, null, out _).flighted, "leg-spin has no flighted variation yet");
        }

        /// <summary>Owner request 2026-10-27: the off-spinner bowls a doosra 1 ball in 12 and the straighter one 1 in 12.</summary>
        [Test]
        public void OffSpinnerBowlsADoosraAndTheStraighterOneOneBallInTwelveEach()
        {
            var types = DeliveryTypeData.All;
            var reachable = DeliveryPlanner.RequestSolverCheck(G, Dt, R);
            var plan = NetBowler.All[1].Plan(FieldDimensions.OverTheWicketRelease);
            var rng = new System.Random(12);
            var counts = new System.Collections.Generic.Dictionary<DeliveryKind, int>();
            int redraws = 0;
            const int draws = 1200;
            for (int n = 0; n < draws; n++)
            {
                var r = DeliveryPlanner.DrawRequest(plan, rng, Handedness.Right, types, reachable, out int k);
                redraws += k;
                counts[r.type.kind] = (counts.TryGetValue(r.type.kind, out int c) ? c : 0) + 1;
                if (r.type.kind != DeliveryKind.OffSpin)
                    Assert.IsFalse(r.flighted, "only the stock off-break is flighted");
                if (r.type.kind == DeliveryKind.ArmBall)
                    Assert.That(r.speedKmh, Is.InRange(82f, 95f), "the straighter one is a little quicker");
            }
            Debug.Log($"[CricketVR] Off-spinner mix over {draws} balls: off-spin {counts[DeliveryKind.OffSpin]}, doosra {counts[DeliveryKind.Doosra]}, " +
                      $"straighter one {counts[DeliveryKind.ArmBall]}; {redraws} redraws");
            Assert.AreEqual(0, redraws);
            Assert.AreEqual(3, counts.Count, "off-spin, doosra and the straighter one only");
            foreach (var kind in new[] { DeliveryKind.Doosra, DeliveryKind.ArmBall })
                Assert.That(counts[kind], Is.InRange(draws / 12 - 35, draws / 12 + 35), $"{kind}: about 1 in 12");
        }

        [Test]
        public void TheStraighterOneLooksLikeOffSpinButBarelyTurnsAndDriftsAway()
        {
            var t = DeliveryTypeData.ArmBall;
            CollectionAssert.IsEmpty(t.Validate());
            Assert.AreEqual(DeliveryKind.OffSpin, t.looksLike, "disguised as the off-break");
            Assert.LessOrEqual(t.turnMax, 0.5f, "barely turns");
            Assert.Greater(t.minSpeedKmh, DeliveryTypeData.OffSpin.minSpeedKmh, "quicker");
            Assert.Less(t.bounceFactor, DeliveryTypeData.OffSpin.bounceFactor, "skids on lower");
            Assert.Less(t.grip, DeliveryTypeData.OffSpin.grip, "keeps its pace off the pitch");
            var s = Solve(Request(t, 88f, 0.05f, 5f, 0.5f), out var world, out var mv);
            Assert.IsTrue(s.Reachable, s.Error);
            Assert.AreEqual(+1, s.Plan.DriftDirection, "drifts away from a right-hander, like the off-break's drift");
        }
    }
}
