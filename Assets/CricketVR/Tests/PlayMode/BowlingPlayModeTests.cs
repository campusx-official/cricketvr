using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CricketVR.Tests
{
    /// <summary>
    /// AC-04 with real physics: a pitch, the ball and the bowling machine built in code.
    /// Time runs 5x faster; the physics step size is unchanged, so results match real time.
    /// </summary>
    public class BowlingPlayModeTests
    {
        GameObject _root;
        GameConfig _config;
        BallController _ball;
        BowlingMachine _machine;
        PhysicsMaterial _pitchMaterial;
        float _timeScale;

        /// <summary>Where each ball pitched (T070 measurement).</summary>
        readonly List<(int id, Vector3 point)> _bounces = new List<(int, Vector3)>();
        /// <summary>The PhysX first-contact report (the measurement before T070), kept for comparison.</summary>
        readonly List<(int id, Vector3 point)> _contacts = new List<(int, Vector3)>();

        /// <summary>Stock bounce accuracy, measured where the ball pitched: 10 cm before T070, tightened to 1 cm once
        /// every preset passed it (worst 0.21 cm, E-040).</summary>
        public const float StockTolerance = 0.01f;
        readonly List<(int id, BallEndReason reason)> _ends = new List<(int, BallEndReason)>();

        [SetUp]
        public void SetUp()
        {
            _bounces.Clear(); // NUnit reuses the fixture instance across tests
            _contacts.Clear();
            _ends.Clear();
            _timeScale = Time.timeScale;
            Time.timeScale = 5f;
            _config = ScriptableObject.CreateInstance<GameConfig>();
            _root = new GameObject("BowlingTestRig");

#if UNITY_EDITOR
            // Test the material that ships, so retuning it is caught by these tests.
            _pitchMaterial = Object.Instantiate(UnityEditor.AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(
                "Assets/CricketVR/Settings/Physics/PitchSurface.physicMaterial"));
#endif
            Assert.IsNotNull(_pitchMaterial, "PitchSurface physics material missing; run the scene builder");
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Ground";
            ground.layer = LayerMask.NameToLayer("Pitch");
            ground.transform.SetParent(_root.transform);
            ground.transform.localScale = new Vector3(10f, 1f, 40f);
            ground.transform.position = new Vector3(0f, -0.5f, 10f);
            ground.GetComponent<Collider>().sharedMaterial = _pitchMaterial;

            var ballGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ballGo.SetActive(false);
            ballGo.name = "Ball";
            ballGo.layer = LayerMask.NameToLayer("Ball");
            ballGo.transform.SetParent(_root.transform);
            ballGo.transform.position = FieldDimensions.DefaultReleasePoint;
            ballGo.AddComponent<Rigidbody>().isKinematic = true;
            _ball = ballGo.AddComponent<BallController>();
            _ball.Configure(_config, null, ballGo.GetComponent<Renderer>());

            var machineGo = new GameObject("BowlingMachine");
            machineGo.SetActive(false);
            machineGo.transform.SetParent(_root.transform);
            _machine = machineGo.AddComponent<BowlingMachine>();
            _machine.Configure(_config, _ball, null, null, null);

            ballGo.SetActive(true);
            machineGo.SetActive(true);
            _ball.Pitched += (id, p, t) => _bounces.Add((id, p));
            _ball.FirstBounce += (id, p, t) => _contacts.Add((id, p));
            _ball.DeliveryEnded += (id, r) => _ends.Add((id, r));
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = _timeScale;
            Object.Destroy(_root);
            Object.Destroy(_config);
            Object.Destroy(_pitchMaterial);
        }

        IEnumerator BowlAndWaitForBounce(DeliveryConfig delivery)
        {
            int count = _bounces.Count;
            int id = _machine.Bowl(delivery);
            Assert.AreNotEqual(0, id, _machine.LastError);
            float start = Time.time;
            while (_bounces.Count == count && Time.time - start < 5f)
                yield return new WaitForFixedUpdate();
            Assert.Greater(_bounces.Count, count, "ball never bounced");
            Assert.AreEqual(id, _bounces[_bounces.Count - 1].id);
            _ball.Cancel();
        }

        [UnityTest]
        public IEnumerator TenIdenticalDeliveriesBounceWithinTolerance()
        {
            var delivery = _config.delivery.defaultDelivery;
            for (int i = 0; i < 10; i++)
                yield return BowlAndWaitForBounce(delivery);

            var first = _bounces[0].point;
            float maxSpread = 0f, maxError = 0f;
            foreach (var (_, p) in _bounces)
            {
                maxSpread = Mathf.Max(maxSpread, Vector2.Distance(new Vector2(p.x, p.z), new Vector2(first.x, first.z)));
                maxError = Mathf.Max(maxError, Vector2.Distance(new Vector2(p.x, p.z), new Vector2(delivery.line, delivery.length)));
            }
            Debug.Log($"[CricketVR] AC-04 PlayMode: 10 deliveries {delivery}; max spread from first {maxSpread * 100f:F2} cm, max error from target {maxError * 100f:F2} cm");
            Assert.LessOrEqual(maxSpread, 0.25f, "AC-04: within 0.25 m of the first bounce");
            Assert.LessOrEqual(maxError, StockTolerance, "bounce should land near the requested point");
        }

        [UnityTest]
        public IEnumerator LengthAndLineMoveTheMeasuredBounce()
        {
            var lengths = new[] { 3.5f, 6f, 9f };
            foreach (var len in lengths)
                yield return BowlAndWaitForBounce(DeliveryConfig.Create("len", 80f, 0f, len));
            yield return BowlAndWaitForBounce(DeliveryConfig.Create("line", 80f, 0.25f, 6f));

            for (int i = 0; i < _bounces.Count; i++)
                Debug.Log($"[CricketVR] Length/line test bounce {i}: ({_bounces[i].point.x:F3}, {_bounces[i].point.y:F3}, {_bounces[i].point.z:F3})");
            for (int i = 0; i < lengths.Length; i++)
                Assert.AreEqual(lengths[i], _bounces[i].point.z, StockTolerance, $"length {lengths[i]}");
            Assert.AreEqual(0.25f, _bounces[3].point.x, Mathf.Min(0.05f, StockTolerance), "line");
        }

        /// <summary>
        /// T070 Stock calibration: every reachable debug preset, where it pitched vs the requested point, logged next
        /// to the old measurement (the PhysX contact report) for validation.md.
        /// </summary>
        [UnityTest]
        public IEnumerator StockPresetsPitchOnTheRequestedPoint()
        {
            float worst = 0f;
            foreach (var delivery in _config.delivery.debugPresets)
            {
                if (delivery.speedKmh < 40f) continue; // the deliberately unreachable preset
                yield return BowlAndWaitForBounce(delivery);
                int id = _bounces[_bounces.Count - 1].id;
                var pitched = _bounces[_bounces.Count - 1].point;
                var contact = _contacts.Find(c => c.id == id).point;
                float error = Error(pitched, delivery), oldError = Error(contact, delivery);
                worst = Mathf.Max(worst, error);
                Debug.Log($"[CricketVR] T070 {delivery}: pitched ({pitched.x:F4}, {pitched.y:F4}, {pitched.z:F4}) error {error * 100f:F2} cm; " +
                          $"contact report ({contact.x:F4}, {contact.z:F4}) error {oldError * 100f:F2} cm");
                Assert.LessOrEqual(error, StockTolerance, delivery.label);
                Assert.Less(error, oldError, $"{delivery.label}: the pitch point should be nearer the request than the contact report");
            }
            Debug.Log($"[CricketVR] T070 worst Stock pitch error {worst * 100f:F2} cm");
        }

        static float Error(Vector3 p, DeliveryConfig d) =>
            Vector2.Distance(new Vector2(p.x, p.z), new Vector2(d.line, d.length));

        [UnityTest]
        public IEnumerator ReleaseSpeedMatchesRequestedKmh()
        {
            _machine.Bowl(DeliveryConfig.Create("speed", 100f, 0f, 6f));
            // Before any physics step, the velocity is the launch velocity.
            Assert.AreEqual(100f / 3.6f, _ball.Body.linearVelocity.magnitude, 0.01f);
            yield return null;
        }

        [UnityTest]
        public IEnumerator RelaunchCancelsThePreviousDeliverySoOnlyOneBallIsActive()
        {
            int first = _machine.Bowl(_config.delivery.defaultDelivery);
            yield return new WaitForFixedUpdate();
            int second = _machine.Bowl(_config.delivery.defaultDelivery);

            Assert.AreNotEqual(first, second);
            CollectionAssert.Contains(_ends, (first, BallEndReason.Cancelled));
            Assert.AreEqual(second, _ball.ActiveDeliveryId);
            Assert.AreEqual(1, Object.FindObjectsByType<BallController>(FindObjectsSortMode.None).Length);

            float start = Time.time;
            while (_bounces.Count == 0 && Time.time - start < 5f)
                yield return new WaitForFixedUpdate();
            Assert.AreEqual(1, _bounces.Count);
            Assert.AreEqual(second, _bounces[0].id, "no events for the cancelled delivery");
        }

        [UnityTest]
        public IEnumerator UnreachableDeliveryIsRejectedAndNoBallLaunches()
        {
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Delivery rejected"));
            int id = _machine.Bowl(DeliveryConfig.Create("too slow", 30f, 0f, 6f));
            Assert.AreEqual(0, id);
            Assert.IsFalse(_ball.InFlight);
            StringAssert.Contains("needs at least", _machine.LastError);
            yield return null;
        }

        /// <summary>
        /// Owner feedback (E-018): a short ball must rise high and a full ball stay low. Height of the ball centre
        /// when it reaches the batting crease (z = 1.2 m), 80 km/h.
        /// </summary>
        [UnityTest]
        public IEnumerator ShorterLengthsArriveHigherAtTheBatter()
        {
            var heights = new Dictionary<float, float>();
            foreach (var length in new[] { 3.5f, 6f, 9f })
            {
                _machine.Bowl(DeliveryConfig.Create("h", 80f, 0f, length));
                var previous = _ball.Body.position;
                float height = float.NaN;
                for (float start = Time.time; Time.time - start < 3f && float.IsNaN(height);)
                {
                    yield return new WaitForFixedUpdate();
                    var p = _ball.Body.position;
                    if (previous.z > 1.2f && p.z <= 1.2f)
                        height = Mathf.Lerp(previous.y, p.y, (previous.z - 1.2f) / (previous.z - p.z));
                    previous = p;
                }
                _ball.Cancel();
                heights[length] = height;
            }
            Debug.Log($"[CricketVR] Height at crease, 80 km/h: full {heights[3.5f]:F2} m, good {heights[6f]:F2} m, short {heights[9f]:F2} m");
            Assert.That(heights[3.5f], Is.LessThanOrEqualTo(0.6f), "full ball: knee height or below");
            Assert.That(heights[6f], Is.InRange(0.7f, 1.05f), "good length: thigh to hip");
            Assert.That(heights[9f], Is.GreaterThanOrEqualTo(1.1f), "short ball: rises to chest height");
            Assert.Less(heights[3.5f], heights[6f]);
            Assert.Less(heights[6f], heights[9f]);
        }

        [UnityTest]
        public IEnumerator BallTimesOutAndParksWhenNothingStopsIt()
        {
            _config.delivery.ballTimeout = 1f;
            int id = _machine.Bowl(_config.delivery.defaultDelivery);
            float start = Time.time;
            while (_ends.Count == 0 && Time.time - start < 3f)
                yield return new WaitForFixedUpdate();
            Assert.AreEqual((id, BallEndReason.Timeout), _ends[0]);
            Assert.IsFalse(_ball.InFlight);
            Assert.IsTrue(_ball.Body.isKinematic, "parked");
        }
    }
}
