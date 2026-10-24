using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using FD = CricketVR.FieldDimensions;

namespace CricketVR.Tests
{
    /// <summary>
    /// M6.5 ground physics (REQ-019, REQ-020, REQ-021, T042–T045) with real physics: the pitch collider as in the
    /// scene, a 1 m outfield box on the Outfield layer, the stumps, the real BallController (with rolling drag),
    /// GroundBoundaryMonitor and ScoreManager in GroundMode. Time runs 5x faster; the physics step is unchanged.
    /// </summary>
    public class GroundPlayModeTests
    {
        GameObject _root;
        GameConfig _config;
        GroundDefinition _ground;
        BallController _ball;
        ScoreManager _score;
        GroundBoundaryMonitor _monitor;
        BowlingMachine _machine;
        readonly List<Object> _owned = new List<Object>();
        float _timeScale;
        readonly List<DeliveryResult> _results = new List<DeliveryResult>();
        readonly List<int> _leftGround = new List<int>();
        readonly List<(int id, float time)> _leftGroundTimes = new List<(int, float)>();
        readonly List<(int id, BallEndReason reason, float time)> _ends = new List<(int, BallEndReason, float)>();
        readonly List<(int id, Vector3 point, float error)> _bounces = new List<(int, Vector3, float)>();

        [SetUp]
        public void SetUp()
        {
            _results.Clear();
            _leftGround.Clear();
            _leftGroundTimes.Clear();
            _ends.Clear();
            _bounces.Clear();
            _owned.Clear();
            _timeScale = Time.timeScale;
            Time.timeScale = 5f;
            _config = ScriptableObject.CreateInstance<GameConfig>();
            _config.environment = EnvironmentMode.GroundMode;
            _ground = ScriptableObject.CreateInstance<GroundDefinition>();
            _root = new GameObject("GroundTestRig");

            PhysicsMaterial pitch = null;
#if UNITY_EDITOR
            pitch = Object.Instantiate(UnityEditor.AssetDatabase.LoadAssetAtPath<PhysicsMaterial>("Assets/CricketVR/Settings/Physics/PitchSurface.physicMaterial"));
#endif
            Assert.IsNotNull(pitch, "PitchSurface physics material missing; run the scene builder");
            _owned.Add(pitch);
            var grass = new PhysicsMaterial("OutfieldGrassTest")
            {
                bounciness = _ground.outfieldBounciness, dynamicFriction = _ground.outfieldDynamicFriction, staticFriction = _ground.outfieldStaticFriction,
                bounceCombine = PhysicsMaterialCombine.Maximum, frictionCombine = PhysicsMaterialCombine.Maximum,
            };
            _owned.Add(grass);

            // Pitch as the shipped calibration test uses it (top at 0); the outfield 2 mm lower and 1 m thick.
            float length = FD.PitchEndZ - FD.PitchStartZ;
            Box("Pitch", "Pitch", new Vector3(0f, -0.05f, (FD.PitchStartZ + FD.PitchEndZ) * 0.5f), new Vector3(FD.PitchWidth, 0.1f, length)).sharedMaterial = pitch;
            float t = _ground.outfieldColliderThickness, c = _ground.GroundCentre.z;
            float size = 2f * (_ground.boundaryRadiusX + _ground.outfieldColliderBeyondRope);
            Box("Outfield", "Outfield", new Vector3(0f, -0.002f - t * 0.5f, c), new Vector3(size, t, size)).sharedMaterial = grass;
            Box("Stumps", "Stumps", new Vector3(0f, FD.StumpHeight * 0.5f, 0f), new Vector3(FD.StumpSetWidth, FD.StumpHeight, FD.StumpDiameter));

            var ballGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ballGo.SetActive(false);
            ballGo.name = "Ball";
            ballGo.layer = LayerMask.NameToLayer("Ball");
            ballGo.transform.SetParent(_root.transform);
            ballGo.transform.position = new Vector3(0f, 1f, 30f);
            ballGo.AddComponent<Rigidbody>().isKinematic = true;
            _ball = ballGo.AddComponent<BallController>();
            _ball.Configure(_config, null, ballGo.GetComponent<Renderer>());
            ballGo.AddComponent<BallRollingDrag>().coefficient = _ground.rollingResistance;

            var gameplay = new GameObject("Gameplay");
            gameplay.SetActive(false);
            gameplay.transform.SetParent(_root.transform);
            _monitor = gameplay.AddComponent<GroundBoundaryMonitor>();
            _monitor.Configure(_ground, _ball);
            _score = gameplay.AddComponent<ScoreManager>();
            _machine = gameplay.AddComponent<BowlingMachine>();
            _machine.Configure(_config, _ball, null, null, null);
            _score.Configure(_config, _ball, null, _machine);
            _score.ConfigureGround(_monitor);

            ballGo.SetActive(true);
            gameplay.SetActive(true);
            _score.DeliveryFinalized += r => _results.Add(r);
            _monitor.BallLeftGround += (id, p, time) => { _leftGround.Add(id); _leftGroundTimes.Add((id, time)); };
            _ball.DeliveryEnded += (id, reason) => _ends.Add((id, reason, Time.time));
            _machine.BounceMeasured += (id, p, e) => _bounces.Add((id, p, e));
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = _timeScale;
            Object.Destroy(_root);
            Object.Destroy(_config);
            Object.Destroy(_ground);
            foreach (var o in _owned) Object.Destroy(o);
        }

        Collider Box(string name, string layer, Vector3 centre, Vector3 size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.layer = LayerMask.NameToLayer(layer);
            go.transform.SetParent(_root.transform);
            go.transform.position = centre;
            go.transform.localScale = size;
            return go.GetComponent<Collider>();
        }

        int Launch(Vector3 position, Vector3 velocity)
        {
            int id = _ball.ActiveDeliveryId + 1;
            _ball.Launch(id, position, velocity);
            return id;
        }

        int Hit(Vector3 position, Vector3 velocity)
        {
            int id = Launch(position, velocity);
            _score.RecordBatContact(new BatContactReport { DeliveryId = id, Point = position, BallVelocityOut = velocity });
            return id;
        }

        IEnumerator WaitUntil(System.Func<bool> condition, float maxGameSeconds)
        {
            float until = Time.time + maxGameSeconds;
            while (Time.time < until && !condition())
                yield return new WaitForFixedUpdate();
        }

        DeliveryResult OnlyResult(int id)
        {
            var mine = _results.FindAll(r => r.DeliveryId == id);
            Assert.AreEqual(1, mine.Count, $"delivery {id} must finalize exactly once");
            return mine[0];
        }

        static float Horizontal(Vector3 v) => new Vector2(v.x, v.z).magnitude;

        // ---------- Outfield physics ----------

        [UnityTest]
        public IEnumerator FortyFiveMetrePerSecondBallNeverTunnelsThroughTheOutfield()
        {
            float r = _config.delivery.ballRadius;
            for (int i = 0; i < 10; i++)
            {
                float angle = -5f - 80f * i / 9f; // -5 .. -85 degrees: down toward +X
                float rad = angle * Mathf.Deg2Rad;
                var v = new Vector3(Mathf.Cos(rad), Mathf.Sin(rad), 0f) * 45f;
                Launch(new Vector3(8f, 1.5f, 35f), v);
                float lowest = float.PositiveInfinity;
                for (int k = 0; k < 60 && _ball.InFlight; k++)
                {
                    yield return new WaitForFixedUpdate();
                    lowest = Mathf.Min(lowest, _ball.Body.position.y);
                }
                // Tunnelling = passing through, or ending up under, the outfield. PhysX CCD can let a 45 m/s ball dip
                // into the 1 m-thick box for one 7 ms step before pushing it out (measured up to ~18 cm); that is
                // logged as evidence and bounded well inside the box, but it is not tunnelling.
                const float top = -0.002f;
                float bottom = top - _ground.outfieldColliderThickness;
                Debug.Log($"[CricketVR] M6.5 outfield {angle:F0} deg at 45 m/s: deepest centre {lowest:F3} m (dip {(top + r - lowest) * 100f:F1} cm), end height {_ball.Body.position.y:F3} m");
                Assert.Greater(lowest, bottom + 0.5f, $"{angle:F0} deg: the ball went {top - lowest:F3} m into the outfield");
                Assert.Greater(_ball.Body.position.y, top + r - 0.005f, $"{angle:F0} deg: the ball ended under the outfield");
                _ball.Cancel();
            }
        }

        [UnityTest]
        public IEnumerator RollingBallStopsOnTheOutfield()
        {
            float r = _config.delivery.ballRadius;
            var start = new Vector3(10f, r, 25f);
            Launch(start, new Vector3(0f, 0f, 5f));
            yield return WaitUntil(() => !_ball.InFlight || _ball.Body.linearVelocity.magnitude < 0.05f, 9f);
            float travelled = Horizontal(_ball.Body.position - start);
            Assert.Less(_ball.Body.linearVelocity.magnitude, 0.2f, "rolling drag brought the ball to rest");
            // Sliding friction first turns ~2/7 of the speed into spin, then drag 0.08 g stops it: about 3-12 m.
            Assert.That(travelled, Is.InRange(2f, 20f), $"stopped after {travelled:F1} m");
        }

        [UnityTest]
        public IEnumerator RollingOnThePitchIsUntouchedByTheDrag()
        {
            float r = _config.delivery.ballRadius;
            Launch(new Vector3(0f, r, 1.5f), new Vector3(0f, 0f, 5f));
            yield return WaitUntil(() => false, 1.0f); // sliding turns into rolling
            float early = Horizontal(_ball.Body.linearVelocity);
            yield return WaitUntil(() => false, 1.0f);
            float late = Horizontal(_ball.Body.linearVelocity);
            Assert.Less(_ball.Body.position.z, FD.PitchEndZ - 1f, "still on the pitch");
            Assert.GreaterOrEqual(late, early * 0.97f, $"pitch rolling speed {early:F2} -> {late:F2} m/s (the outfield would lose ~0.8 m/s)");
        }

        /// <summary>
        /// The outfield must not move the delivery bounce. Each preset is bowled with the outfield collider off, then
        /// on; the bounce points must agree within 1 cm. (The absolute error to the target, about 6 cm on the
        /// default delivery since M4, is PhysX contact timing and stays covered by the AC-04 tests' 10 cm limit.)
        /// </summary>
        [UnityTest]
        public IEnumerator OutfieldDoesNotMoveTheDeliveryBounce()
        {
            var outfield = _root.transform.Find("Outfield").gameObject;
            foreach (var delivery in _config.delivery.debugPresets)
            {
                if (delivery.speedKmh < 40f) continue; // the deliberately unreachable preset
                var points = new Vector3[2];
                for (int pass = 0; pass < 2; pass++)
                {
                    outfield.SetActive(pass == 1);
                    yield return new WaitForFixedUpdate();
                    int id = _machine.Bowl(delivery);
                    Assert.AreNotEqual(0, id, _machine.LastError);
                    yield return WaitUntil(() => _bounces.Exists(b => b.id == id), 3f);
                    Assert.IsTrue(_bounces.Exists(b => b.id == id), $"{delivery.label} bounced");
                    points[pass] = _bounces.Find(b => b.id == id).point;
                    _ball.Cancel();
                }
                float shift = Vector2.Distance(new Vector2(points[0].x, points[0].z), new Vector2(points[1].x, points[1].z));
                Debug.Log($"[CricketVR] M6.5 calibration {delivery.label}: bounce {points[1].z:F3} m, shift with outfield {shift * 100f:F2} cm, error {_bounces[_bounces.Count - 1].error * 100f:F1} cm");
                Assert.Less(shift, 0.01f, $"{delivery.label}: the outfield moved the bounce by {shift * 100f:F2} cm");
            }
        }

        // ---------- Leaving the ground ----------

        [UnityTest]
        public IEnumerator BallOverTheRopeRaisesBallLeftGroundOnceThenHides()
        {
            var c = _ground.GroundCentre;
            // A gentle lob over the rope that lands on the outfield apron (so the safety despawn doesn't fire first).
            int id = Hit(new Vector3(0f, 1.2f, c.z + _ground.boundaryRadiusZ - 2f), new Vector3(0f, 5f, 5f));
            yield return WaitUntil(() => _leftGround.Contains(id), 3f);
            Assert.AreEqual(1, _leftGround.FindAll(x => x == id).Count, "raised once");
            Assert.IsTrue(_ball.InFlight, "still visible right after crossing the rope");
            yield return WaitUntil(() => !_ball.InFlight, 3f);
            Assert.IsFalse(_ball.InFlight, "hidden after the hold");
            // Event times, not coroutine polling: at 5x time scale a coroutine resumes only once per rendered frame.
            float raisedAt = _leftGroundTimes.Find(e => e.id == id).time;
            var end = _ends.Find(e => e.id == id);
            Assert.AreEqual(BallEndReason.Finalized, end.reason);
            Assert.AreEqual(_ground.leftGroundHideDelay, end.time - raisedAt, 0.02f, "hidden after the 1.5 s hold");
            Assert.AreEqual(1, _leftGround.FindAll(x => x == id).Count, "still once");
            var result = OnlyResult(id);
            Assert.AreEqual(DeliveryOutcome.ScoringPending, result.Outcome);
            Assert.AreEqual(DeliveryEnd.LeftGround, result.End);
        }

        [UnityTest]
        public IEnumerator SafetyDespawnBelowTheOutfieldOrFarAway()
        {
            int below = Hit(new Vector3(3f, -2f, 30f), Vector3.zero);
            yield return WaitUntil(() => !_ball.InFlight, 0.5f);
            Assert.IsFalse(_ball.InFlight, "despawned at once below the outfield");
            Assert.AreEqual(1, _leftGround.FindAll(x => x == below).Count);
            Assert.AreEqual(DeliveryOutcome.ScoringPending, OnlyResult(below).Outcome);

            int far = Launch(new Vector3(0f, 30f, _ground.GroundCentre.z + 160f), new Vector3(0f, 0f, 10f));
            yield return WaitUntil(() => !_ball.InFlight, 0.5f);
            Assert.IsFalse(_ball.InFlight, "despawned at once beyond 150 m");
            Assert.AreEqual(1, _leftGround.FindAll(x => x == far).Count);
            Assert.AreEqual(DeliveryOutcome.Miss, OnlyResult(far).Outcome, "unhit");
        }

        // ---------- GroundMode results ----------

        [UnityTest]
        public IEnumerator UnhitWideBallPastTheStumpsIsAMiss()
        {
            int id = Launch(new Vector3(1.6f, 0.6f, 4f), new Vector3(0f, 0.5f, -20f));
            yield return WaitUntil(() => _results.Exists(r => r.DeliveryId == id), 2f);
            var result = OnlyResult(id);
            Assert.AreEqual(DeliveryOutcome.Miss, result.Outcome);
            Assert.AreEqual(DeliveryEnd.PassedStumps, result.End);
            Assert.Less(_ball.Body.position.z, ScoreManager.MissBehindStumpsZ + 0.5f);
        }

        [UnityTest]
        public IEnumerator EdgeBehindTheWicketIsPendingNotAMiss()
        {
            int id = Hit(new Vector3(0.4f, 0.8f, 1.2f), new Vector3(3f, 6f, -30f));
            yield return WaitUntil(() => _results.Exists(r => r.DeliveryId == id), 8f);
            var result = OnlyResult(id);
            Assert.AreEqual(DeliveryOutcome.ScoringPending, result.Outcome);
            Assert.IsFalse(result.Miss);
            Assert.AreNotEqual(DeliveryEnd.PassedStumps, result.End);
        }

        [UnityTest]
        public IEnumerator UnhitBallThatStopsBeforeTheStumpsIsAMiss()
        {
            int id = Launch(new Vector3(0.6f, 0.3f, 6f), Vector3.zero);
            yield return WaitUntil(() => _results.Exists(r => r.DeliveryId == id), 6f);
            var result = OnlyResult(id);
            Assert.AreEqual(DeliveryOutcome.Miss, result.Outcome);
            Assert.AreEqual(DeliveryEnd.Settled, result.End);
        }

        [UnityTest]
        public IEnumerator BallAtTheStumpsIsBowledInGroundMode()
        {
            int id = Launch(new Vector3(0f, 0.4f, 3f), new Vector3(0f, 0f, -15f));
            yield return WaitUntil(() => _results.Exists(r => r.DeliveryId == id), 2f);
            Assert.AreEqual(DeliveryOutcome.Bowled, OnlyResult(id).Outcome);
        }
    }
}
