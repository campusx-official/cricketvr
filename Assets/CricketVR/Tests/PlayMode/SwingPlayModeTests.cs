using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CricketVR.Tests
{
    /// <summary>
    /// M7.5 T074 (AC-26 PlayMode): swinging deliveries in real PhysX with BallFlight, using the shipped DeliveryType
    /// assets and pitch material. Variation 0 except where a test is about variation. Swing = the ball's distance at
    /// the striker's popping crease from the line it was released along (owner decision 2026-10-27).
    /// </summary>
    public class SwingPlayModeTests
    {
        GameObject _root;
        GameConfig _config;
        BallController _ball;
        BallFlight _flight;
        BowlingMachine _machine;
        PhysicsMaterial _pitchMaterial;
        float _timeScale;
        readonly Dictionary<int, PitchMeasurement> _pitches = new Dictionary<int, PitchMeasurement>();
        readonly Dictionary<int, Vector3> _velocityAtGround = new Dictionary<int, Vector3>();

        [SetUp]
        public void SetUp()
        {
            _pitches.Clear();
            _velocityAtGround.Clear();
            _timeScale = Time.timeScale;
            Time.timeScale = 5f;
            _config = ScriptableObject.CreateInstance<GameConfig>();
            _root = new GameObject("SwingTestRig");
#if UNITY_EDITOR
            _pitchMaterial = Object.Instantiate(UnityEditor.AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(
                "Assets/CricketVR/Settings/Physics/PitchSurface.physicMaterial"));
#endif
            Assert.IsNotNull(_pitchMaterial, "PitchSurface physics material missing; run the scene builder");
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.layer = LayerMask.NameToLayer("Pitch");
            ground.transform.SetParent(_root.transform);
            ground.transform.localScale = new Vector3(10f, 1f, 40f);
            ground.transform.position = new Vector3(0f, FieldDimensions.PitchSurfaceHeight - 0.5f, 10f); // top at the real pitch height
            ground.GetComponent<Collider>().sharedMaterial = _pitchMaterial;

            var ballGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ballGo.SetActive(false);
            ballGo.layer = LayerMask.NameToLayer("Ball");
            ballGo.transform.SetParent(_root.transform);
            ballGo.transform.position = FieldDimensions.DefaultReleasePoint;
            ballGo.AddComponent<Rigidbody>().isKinematic = true;
            _ball = ballGo.AddComponent<BallController>();
            _ball.Configure(_config, null, ballGo.GetComponent<Renderer>());
            _flight = ballGo.AddComponent<BallFlight>();

            var machineGo = new GameObject("BowlingMachine");
            machineGo.SetActive(false);
            machineGo.transform.SetParent(_root.transform);
            _machine = machineGo.AddComponent<BowlingMachine>();
            _machine.Configure(_config, _ball, null, null, null);

            ballGo.SetActive(true);
            machineGo.SetActive(true);
            _ball.Pitched += (id, p, t) => _pitches[id] = p;
            _ball.FirstBounce += (id, p, t) => _velocityAtGround[id] = _ball.Body.linearVelocity;
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = _timeScale;
            Object.Destroy(_root);
            Object.Destroy(_config);
            Object.Destroy(_pitchMaterial);
        }

        static DeliveryTypeData Shipped(DeliveryKind kind, float variation = 0f)
        {
#if UNITY_EDITOR
            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<DeliveryType>($"Assets/CricketVR/Settings/DeliveryTypes/{kind}.asset");
            Assert.IsNotNull(asset, $"{kind} asset missing; run the scene builder");
            var data = asset.data;
#else
            var data = default(DeliveryTypeData);
#endif
            data.variation = variation;
            return data;
        }

        static DeliveryRequest Request(DeliveryTypeData type, float kmh, float line, float length, float movement, Handedness hand, int seed = 1) => new DeliveryRequest
        {
            label = type.displayName, type = type, speedKmh = kmh, releasePosition = FieldDimensions.DefaultReleasePoint,
            line = line, length = length, movement = movement, handedness = hand, seed = seed,
        };

        IEnumerator BowlAndWaitForPitch(DeliveryRequest request, System.Action<int> done, bool untilCrease = false)
        {
            yield return new WaitForFixedUpdate();
            int id = _machine.Bowl(request);
            Assert.AreNotEqual(0, id, _machine.LastError);
            float until = Time.time + 5f;
            while ((!_pitches.ContainsKey(id) || untilCrease && float.IsNaN(_flight.ArrivalLine)) && Time.time < until)
                yield return new WaitForFixedUpdate();
            Assert.IsTrue(_pitches.ContainsKey(id), $"{request}: never pitched");
            if (untilCrease)
                Assert.IsFalse(float.IsNaN(_flight.ArrivalLine), $"{request}: never reached the crease");
            done(id);
            _ball.Cancel();
        }

        [UnityTest]
        public IEnumerator SwingingBallsPitchOnTargetAndSwingAsSolved() => SwingMatrix(new[] { 115f, 125f }, 74);

        /// <summary>AC-29 (T075): the same matrix at 140 km/h, part of the test that decides the pace cap.</summary>
        [UnityTest]
        public IEnumerator SwingingBallsAt140KmhPitchOnTargetAndSwingAsSolved() => SwingMatrix(new[] { 140f }, 140);

        /// <summary>Measured swing matches the solver's within this (m): PhysX's bounce keeps the ball's heading.</summary>
        const float SwingTolerance = 0.01f;

        IEnumerator SwingMatrix(float[] speeds, int seed)
        {
            var rng = new System.Random(seed);
            float worstPitch = 0f, worstSwing = 0f, worstCurve = 0f, minSwing = float.MaxValue, maxSwing = 0f;
            int runs = 0;
            foreach (var kind in new[] { DeliveryKind.Inswinger, DeliveryKind.Outswinger })
            foreach (var kmh in speeds)
            {
                var type = Shipped(kind);
                for (int i = 0; i < 10; i++)
                {
                    var hand = i % 2 == 0 ? Handedness.Right : Handedness.Left;
                    float line = -0.1f + 0.4f * (float)rng.NextDouble();
                    float length = 3f + 5f * (float)rng.NextDouble();
                    float movement = (float)rng.NextDouble();
                    var request = Request(type, kmh, line, length, movement, hand);
                    int id = 0;
                    yield return BowlAndWaitForPitch(request, x => id = x, untilCrease: true);

                    var world = request.ForBatter();
                    var pitch = _pitches[id].Point;
                    float pitchError = Vector2.Distance(new Vector2(pitch.x, pitch.z), new Vector2(world.line, world.length));
                    float swing = _flight.MeasuredSwing, predicted = _machine.LastSolution.PredictedSwing;
                    float curve = _flight.MeasuredCurve;
                    Assert.LessOrEqual(pitchError, BowlingPlayModeTests.StockTolerance, $"{request}: pitch {pitchError * 100f:F2} cm off");
                    Assert.AreEqual(predicted, swing, SwingTolerance, $"{request}: measured swing vs the solver's");
                    Assert.That(swing, Is.InRange(type.swingMin - SwingTolerance, type.swingMax + SwingTolerance), $"{request}: swing in the type's range");
                    Assert.AreEqual(_machine.LastSolution.PredictedCurve, curve, 0.01f, $"{request}: measured curve vs the solver's");

                    // Swing direction: sideways velocity gained between release and the pitch, mirrored for a left-hander.
                    float expected = type.swingDirection * (hand == Handedness.Right ? 1f : -1f);
                    float gained = _velocityAtGround[id].x - _machine.LastSolution.Velocity.x;
                    Assert.AreEqual(expected, Mathf.Sign(gained), $"{request}: swung the wrong way ({gained:F2} m/s)");

                    worstPitch = Mathf.Max(worstPitch, pitchError);
                    worstSwing = Mathf.Max(worstSwing, Mathf.Abs(swing - predicted));
                    worstCurve = Mathf.Max(worstCurve, Mathf.Abs(curve - _machine.LastSolution.PredictedCurve));
                    minSwing = Mathf.Min(minSwing, swing);
                    maxSwing = Mathf.Max(maxSwing, swing);
                    runs++;
                }
            }
            Debug.Log($"[CricketVR] T074 swing PlayMode {string.Join("/", speeds)} km/h: {runs} balls, worst pitch error {worstPitch * 100f:F2} cm, " +
                      $"worst measured-vs-solved swing {worstSwing * 1000f:F1} mm (curve {worstCurve * 1000f:F1} mm), swings {minSwing:F3}-{maxSwing:F3} m");
        }

        [UnityTest]
        public IEnumerator SameRequestSameBallIncludingVariation()
        {
            foreach (var variation in new[] { 0f, 0.15f })
            {
                var request = Request(Shipped(DeliveryKind.Outswinger, variation), 120f, 0.15f, 5.5f, 0.6f, Handedness.Right, seed: 4242);
                int a = 0, b = 0;
                yield return BowlAndWaitForPitch(request, x => a = x, untilCrease: true);
                float curveA = _flight.MeasuredCurve, swingA = _flight.MeasuredSwing, varA = _machine.LastMovement.VarSwing;
                yield return BowlAndWaitForPitch(request, x => b = x, untilCrease: true);
                Assert.AreEqual(_pitches[a].Point.x, _pitches[b].Point.x, 1e-5f, $"variation {variation}: same pitch x");
                Assert.AreEqual(_pitches[a].Point.z, _pitches[b].Point.z, 1e-5f, $"variation {variation}: same pitch z");
                Assert.AreEqual(curveA, _flight.MeasuredCurve, 1e-5f, $"variation {variation}: same curve");
                Assert.AreEqual(swingA, _flight.MeasuredSwing, 1e-5f, $"variation {variation}: same swing");
                Assert.AreEqual(varA, _machine.LastMovement.VarSwing, $"variation {variation}: same recorded factor");
                if (variation > 0f)
                    Assert.AreNotEqual(1f, varA, "the seed varied the swing");
            }
        }

        /// <summary>
        /// T077: spin types' drift and dip in PhysX (turn, bounce and grip zeroed: the turn at the bounce is T078). The
        /// ball pitches on target, and its drift (pitch point off the release line) is the solved drift, opposite to
        /// the type's turn.
        /// </summary>
        [UnityTest]
        public IEnumerator SpinningBallsDriftAndDipAsSolved()
        {
            float worstPitch = 0f, worstDrift = 0f;
            int balls = 0;
            foreach (var kind in new[] { DeliveryKind.OffSpin, DeliveryKind.LegSpin, DeliveryKind.Doosra, DeliveryKind.Googly })
            foreach (var hand in new[] { Handedness.Right, Handedness.Left })
            foreach (var kmh in new[] { 75f, 95f })
            foreach (var length in new[] { 3f, 6f, 9f })
            {
                var type = Shipped(kind);
                type.turnMin = type.turnMax = type.bounceFactor = type.grip = 0f;
                var request = Request(type, kmh, 0.1f, length, 1f, hand);
                int id = 0;
                yield return BowlAndWaitForPitch(request, x => id = x);
                var world = request.ForBatter();
                var solution = _machine.LastSolution;
                var p = _pitches[id].Point;
                float pitchError = Vector2.Distance(new Vector2(p.x, p.z), new Vector2(world.line, world.length));
                var release = world.releasePosition;
                var v = solution.Velocity;
                float drift = p.x - (release.x + v.x * (p.z - release.z) / v.z);
                Assert.LessOrEqual(pitchError, BowlingPlayModeTests.StockTolerance, $"{request}: pitch error");
                Assert.AreEqual(-world.type.turnDirection, (int)Mathf.Sign(drift), $"{request}: drift {drift:F3} m opposite to the turn");
                Assert.AreEqual(solution.PredictedDrift, Mathf.Abs(drift), 0.005f, $"{request}: measured drift");
                Assert.AreEqual(world.type.dip, solution.Plan.Dip, $"{request}: dip applied");
                worstPitch = Mathf.Max(worstPitch, pitchError);
                worstDrift = Mathf.Max(worstDrift, Mathf.Abs(Mathf.Abs(drift) - solution.PredictedDrift));
                balls++;
            }
            Debug.Log($"[CricketVR] T077 spin PlayMode (drift and dip, no turn): {balls} balls, worst pitch error {worstPitch * 100f:F2} cm, " +
                      $"worst measured-vs-solved drift {worstDrift * 1000f:F1} mm");
        }

        /// <summary>
        /// T078 (AC-27 PlayMode): the turn at PhysX's bounce. 10 runs × 4 spin types × both hands, shipped types with
        /// their natural variation: the heading change is the planned turn and in the type's range, toward the type's
        /// direction; the pitch point is the same as the same ball without a turn; the googly bounces higher than the
        /// leg-break.
        /// </summary>
        [UnityTest]
        public IEnumerator SpinningBallsTurnAtTheBounce()
        {
            var rng = new System.Random(78);
            float worstTurn = 0f, worstPitchShift = 0f, minTurn = float.MaxValue, maxTurn = 0f;
            int balls = 0;
            foreach (var kind in new[] { DeliveryKind.OffSpin, DeliveryKind.LegSpin, DeliveryKind.Doosra, DeliveryKind.Googly })
            foreach (var hand in new[] { Handedness.Right, Handedness.Left })
            for (int run = 0; run < 10; run++)
            {
                var type = Shipped(kind, 0.15f);
                float kmh = Mathf.Round(Mathf.Lerp(type.minSpeedKmh, type.maxSpeedKmh, (float)rng.NextDouble()));
                float length = Mathf.Lerp(4f, 8f, (float)rng.NextDouble());
                float line = Mathf.Lerp(-0.1f, 0.3f, (float)rng.NextDouble());
                var request = Request(type, kmh, line, length, (float)rng.NextDouble(), hand, seed: rng.Next());
                var spun = new SpinRun();
                yield return BowlSpin(request, spun);
                var world = request.ForBatter();
                var planned = _machine.LastMovement;

                // Same ball without the turn: the turn must not move the pitch point.
                var still = request;
                still.type.turnMin = still.type.turnMax = still.type.bounceFactor = still.type.grip = 0f;
                var plain = new SpinRun();
                yield return BowlSpin(still, plain);
                float shift = Vector3.Distance(spun.Pitch, plain.Pitch);
                Assert.LessOrEqual(shift, 1e-4f, $"{request}: pitch moved {shift * 1000f:F2} mm by the turn");

                float turn = Vector3.Angle(Flat(spun.Arrival), Flat(spun.AfterTurn));
                int side = (int)Mathf.Sign(Flat(spun.AfterTurn).normalized.x - Flat(spun.Arrival).normalized.x);
                Assert.AreEqual(planned.TurnDegrees, turn, 0.2f, $"{request}: turned {turn:F2} deg (planned {planned.TurnDegrees:F2})");
                Assert.AreEqual(planned.TurnDegrees, Mathf.Abs(spun.Applied), 1e-4f);
                Assert.That(turn, Is.InRange(world.type.turnMin * 0.85f - 0.2f, world.type.turnMax * 1.15f + 0.2f), $"{request}: turn in the type's range");
                Assert.AreEqual(world.type.turnDirection, side, $"{request}: turns toward {world.type.turnDirection}");
                Assert.Less(Vector3.Angle(Flat(spun.Later), Flat(spun.AfterTurn)), 0.2f, $"{request}: keeps the turned heading in the air");
                worstTurn = Mathf.Max(worstTurn, Mathf.Abs(turn - planned.TurnDegrees));
                worstPitchShift = Mathf.Max(worstPitchShift, shift);
                minTurn = Mathf.Min(minTurn, turn);
                maxTurn = Mathf.Max(maxTurn, turn);
                balls++;
            }
            Debug.Log($"[CricketVR] T078 turn PlayMode: {balls} balls, turns {minTurn:F2}-{maxTurn:F2} deg, worst measured-vs-planned " +
                      $"{worstTurn:F3} deg, pitch shift by the turn {worstPitchShift * 1000f:F3} mm");

            // Googly vs leg-break at the same speed, line, length and movement, no variation.
            var leg = new SpinRun();
            yield return BowlSpin(Request(Shipped(DeliveryKind.LegSpin), 82f, 0.1f, 6f, 0.5f, Handedness.Right), leg);
            var googly = new SpinRun();
            yield return BowlSpin(Request(Shipped(DeliveryKind.Googly), 82f, 0.1f, 6f, 0.5f, Handedness.Right), googly);
            Debug.Log($"[CricketVR] T078 bounce at 82 km/h, 6 m: leg-spin peak {leg.PeakAfterBounce:F3} m (v_y {leg.AfterTurn.y:F2}), " +
                      $"googly {googly.PeakAfterBounce:F3} m (v_y {googly.AfterTurn.y:F2})");
            Assert.Greater(googly.PeakAfterBounce, leg.PeakAfterBounce, "the googly bounces higher than the leg-break");
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        class SpinRun
        {
            public Vector3 Pitch, Arrival, AfterTurn, Later;
            public float Applied, PeakAfterBounce;
        }

        /// <summary>Bowls one ball and follows it to just past the bounce: arrival velocity (last step before it met
        /// the ground), the turn, the velocity 10 steps later, and the peak height of the rebound.</summary>
        IEnumerator BowlSpin(DeliveryRequest request, SpinRun run)
        {
            yield return new WaitForFixedUpdate();
            int id = _machine.Bowl(request);
            Assert.AreNotEqual(0, id, _machine.LastError);
            float until = Time.time + 5f;
            int after = -1;
            bool turns = _machine.LastSolution.Plan.ActsAtBounce;
            while (Time.time < until)
            {
                yield return new WaitForFixedUpdate();
                var v = _ball.Body.linearVelocity;
                if (!_ball.TouchedGround)
                {
                    run.Arrival = v;
                    continue;
                }
                if (turns && float.IsNaN(_flight.AppliedTurn))
                    continue;
                if (after < 0)
                {
                    after = 0;
                    run.AfterTurn = turns ? _flight.VelocityAfterTurn : v;
                    run.Applied = turns ? _flight.AppliedTurn : 0f;
                }
                run.PeakAfterBounce = Mathf.Max(run.PeakAfterBounce, _ball.Body.position.y);
                if (++after == 10)
                    run.Later = v;
                if (after >= 10 && _pitches.ContainsKey(id) && v.y <= 0f)
                    break; // past the rebound's peak
            }
            Assert.IsTrue(_pitches.ContainsKey(id), $"{request}: never pitched");
            Assert.GreaterOrEqual(after, 10, $"{request}: no rebound followed");
            run.Pitch = _pitches[id].Point;
            _ball.Cancel();
        }

        [UnityTest]
        public IEnumerator StockWithBallFlightIsUnchanged()
        {
            // The flight component adds no force to a Stock ball: it pitches exactly as the solver predicts, as before.
            foreach (var delivery in _config.delivery.debugPresets)
            {
                if (delivery.speedKmh < 40f) continue;
                int id = 0;
                yield return BowlAndWaitForPitch(DeliveryRequest.FromConfig(delivery), x => id = x, untilCrease: true);
                var p = _pitches[id].Point;
                Assert.AreEqual(delivery.length, p.z, BowlingPlayModeTests.StockTolerance, delivery.label);
                Assert.AreEqual(delivery.line, p.x, BowlingPlayModeTests.StockTolerance, delivery.label);
                Assert.Less(_flight.MeasuredCurve, 0.001f, $"{delivery.label}: a Stock ball doesn't curve");
                Assert.Less(_flight.MeasuredSwing, 0.005f, $"{delivery.label}: nor swing off its release line");
            }
        }

        /// <summary>Owner decision 2026-10-27, in PhysX: the biggest inswing on off stump stays about 0.5 m off its release line.</summary>
        [UnityTest]
        public IEnumerator InswingerPitchedOnOffStumpFinishesWithinHalfAMetreOfItsReleaseLine()
        {
            float worst = 0f;
            foreach (var hand in new[] { Handedness.Right, Handedness.Left })
            foreach (var kmh in new[] { 115f, 140f })
            foreach (var length in new[] { 3f, 6f, 8f })
            {
                var request = Request(Shipped(DeliveryKind.Inswinger, variation: 0.15f), kmh, DeliveryPlanner.OffStumpLine, length, 1f, hand, seed: 11);
                int id = 0;
                yield return BowlAndWaitForPitch(request, x => id = x, untilCrease: true);
                float swing = _flight.MeasuredSwing;
                Assert.LessOrEqual(swing, DeliveryTypeData.MaxSwing + SwingTolerance, $"{request}: {swing:F3} m off the release line at the crease");
                worst = Mathf.Max(worst, swing);
            }
            Debug.Log($"[CricketVR] Swing cap PlayMode: inswinger on off stump, movement 1: largest measured swing {worst:F3} m");
        }

        [UnityTest]
        public IEnumerator ArrivalLineIsMeasuredAtThePoppingCrease()
        {
            var request = Request(Shipped(DeliveryKind.Inswinger), 120f, 0.2f, 6f, 0.5f, Handedness.Right);
            yield return new WaitForFixedUpdate();
            int id = _machine.Bowl(request);
            Assert.AreNotEqual(0, id, _machine.LastError);
            float until = Time.time + 3f;
            while (float.IsNaN(_flight.ArrivalLine) && Time.time < until)
                yield return new WaitForFixedUpdate();
            Assert.IsFalse(float.IsNaN(_flight.ArrivalLine), "reached the crease");
            Debug.Log($"[CricketVR] T074 arrival line: pitched on {request.line:+0.00} m, at the crease {_flight.ArrivalLine:+0.000;-0.000} m, " +
                      $"swing {_flight.MeasuredSwing:F3} m (planned {_machine.LastSolution.PredictedSwing:F3} m)");
            Assert.That(_flight.ArrivalLine, Is.InRange(-1f, 1f));
            _ball.Cancel();
        }
    }
}
