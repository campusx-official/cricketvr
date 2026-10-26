using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CricketVR.Tests
{
    /// <summary>
    /// M7.5 T074 (AC-26 PlayMode): swinging deliveries in real PhysX with BallFlight, using the shipped DeliveryType
    /// assets and pitch material. Variation 0 except where a test is about variation. 140 km/h waits for the cap
    /// test (T075).
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
            ground.transform.position = new Vector3(0f, -0.5f, 10f);
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

        IEnumerator BowlAndWaitForPitch(DeliveryRequest request, System.Action<int> done)
        {
            yield return new WaitForFixedUpdate();
            int id = _machine.Bowl(request);
            Assert.AreNotEqual(0, id, _machine.LastError);
            float until = Time.time + 5f;
            while (!_pitches.ContainsKey(id) && Time.time < until)
                yield return new WaitForFixedUpdate();
            Assert.IsTrue(_pitches.ContainsKey(id), $"{request}: never pitched");
            done(id);
            _ball.Cancel();
        }

        [UnityTest]
        public IEnumerator SwingingBallsPitchOnTargetAndCurveAsSolved()
        {
            var rng = new System.Random(74);
            float worstPitch = 0f, worstCurve = 0f, minCurve = float.MaxValue, maxCurve = 0f;
            int runs = 0;
            foreach (var kind in new[] { DeliveryKind.Inswinger, DeliveryKind.Outswinger })
            foreach (var kmh in new[] { 115f, 125f })
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
                    yield return BowlAndWaitForPitch(request, x => id = x);

                    var world = request.ForBatter();
                    var pitch = _pitches[id].Point;
                    float pitchError = Vector2.Distance(new Vector2(pitch.x, pitch.z), new Vector2(world.line, world.length));
                    float curve = _flight.MeasuredCurve, predicted = _machine.LastSolution.PredictedCurve;
                    Assert.LessOrEqual(pitchError, BowlingPlayModeTests.StockTolerance, $"{request}: pitch {pitchError * 100f:F2} cm off");
                    Assert.AreEqual(predicted, curve, 0.01f, $"{request}: measured curve vs the solver's");
                    Assert.That(curve, Is.InRange(type.swingMin - 0.01f, type.swingMax + 0.01f), $"{request}: curve in the type's range");

                    // Swing direction: sideways velocity gained between release and the pitch, mirrored for a left-hander.
                    float expected = type.swingDirection * (hand == Handedness.Right ? 1f : -1f);
                    float gained = _velocityAtGround[id].x - _machine.LastSolution.Velocity.x;
                    Assert.AreEqual(expected, Mathf.Sign(gained), $"{request}: swung the wrong way ({gained:F2} m/s)");

                    worstPitch = Mathf.Max(worstPitch, pitchError);
                    worstCurve = Mathf.Max(worstCurve, Mathf.Abs(curve - predicted));
                    minCurve = Mathf.Min(minCurve, curve);
                    maxCurve = Mathf.Max(maxCurve, curve);
                    runs++;
                }
            }
            Debug.Log($"[CricketVR] T074 swing PlayMode: {runs} balls, worst pitch error {worstPitch * 100f:F2} cm, " +
                      $"worst measured-vs-solved curve {worstCurve * 1000f:F1} mm, curves {minCurve:F3}-{maxCurve:F3} m");
        }

        [UnityTest]
        public IEnumerator SameRequestSameBallIncludingVariation()
        {
            foreach (var variation in new[] { 0f, 0.15f })
            {
                var request = Request(Shipped(DeliveryKind.Outswinger, variation), 120f, 0.15f, 5.5f, 0.6f, Handedness.Right, seed: 4242);
                int a = 0, b = 0;
                yield return BowlAndWaitForPitch(request, x => a = x);
                float curveA = _flight.MeasuredCurve, varA = _machine.LastMovement.VarSwing;
                yield return BowlAndWaitForPitch(request, x => b = x);
                Assert.AreEqual(_pitches[a].Point.x, _pitches[b].Point.x, 1e-5f, $"variation {variation}: same pitch x");
                Assert.AreEqual(_pitches[a].Point.z, _pitches[b].Point.z, 1e-5f, $"variation {variation}: same pitch z");
                Assert.AreEqual(curveA, _flight.MeasuredCurve, 1e-5f, $"variation {variation}: same curve");
                Assert.AreEqual(varA, _machine.LastMovement.VarSwing, $"variation {variation}: same recorded factor");
                if (variation > 0f)
                    Assert.AreNotEqual(1f, varA, "the seed varied the swing");
            }
        }

        [UnityTest]
        public IEnumerator StockWithBallFlightIsUnchanged()
        {
            // The flight component adds no force to a Stock ball: it pitches exactly as the solver predicts, as before.
            foreach (var delivery in _config.delivery.debugPresets)
            {
                if (delivery.speedKmh < 40f) continue;
                int id = 0;
                yield return BowlAndWaitForPitch(DeliveryRequest.FromConfig(delivery), x => id = x);
                var p = _pitches[id].Point;
                Assert.AreEqual(delivery.length, p.z, BowlingPlayModeTests.StockTolerance, delivery.label);
                Assert.AreEqual(delivery.line, p.x, BowlingPlayModeTests.StockTolerance, delivery.label);
                Assert.Less(_flight.MeasuredCurve, 0.001f, $"{delivery.label}: a Stock ball doesn't curve");
            }
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
            Debug.Log($"[CricketVR] T074 arrival line: pitched on {request.line:+0.00} m, at the crease {_flight.ArrivalLine:+0.000;-0.000} m");
            Assert.That(_flight.ArrivalLine, Is.InRange(-1f, 1f));
            _ball.Cancel();
        }
    }
}
