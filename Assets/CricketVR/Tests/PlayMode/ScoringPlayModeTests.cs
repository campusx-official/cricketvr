using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using FD = CricketVR.FieldDimensions;

namespace CricketVR.Tests
{
    /// <summary>
    /// AC-06 with real physics: ground, stumps and the four tagged net walls built to the shipped dimensions, the
    /// real BallController and ScoreManager. Bat contacts are reported directly (the resolver itself is covered by
    /// the AC-05 matrix), so each case controls exactly where the ball goes after the "hit".
    /// Time runs 5x faster; the physics step size is unchanged.
    /// </summary>
    public class ScoringPlayModeTests
    {
        GameObject _root;
        GameConfig _config;
        BallController _ball;
        ScoreManager _score;
        PhysicsMaterial _pitchMaterial;
        float _timeScale;
        readonly List<DeliveryResult> _results = new List<DeliveryResult>();
        readonly List<(int id, BallEndReason reason)> _ends = new List<(int, BallEndReason)>();

        [SetUp]
        public void SetUp()
        {
            _results.Clear();
            _ends.Clear();
            _timeScale = Time.timeScale;
            Time.timeScale = 5f;
            _config = ScriptableObject.CreateInstance<GameConfig>();
            _root = new GameObject("ScoringTestRig");

#if UNITY_EDITOR
            _pitchMaterial = Object.Instantiate(UnityEditor.AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(
                "Assets/CricketVR/Settings/Physics/PitchSurface.physicMaterial"));
#endif
            Assert.IsNotNull(_pitchMaterial, "PitchSurface physics material missing; run the scene builder");
            var ground = Box("Ground", "Pitch", new Vector3(0f, FD.PitchSurfaceHeight - 0.5f, 10f), new Vector3(20f, 1f, 50f));
            ground.GetComponent<Collider>().sharedMaterial = _pitchMaterial;
            Box("Stumps", "Stumps", new Vector3(0f, FD.StumpHeight * 0.5f, 0f), new Vector3(FD.StumpSetWidth, FD.StumpHeight, FD.StumpDiameter));

            // Same placement as PracticeNetBuilder: inner faces on the net planes.
            float t = FD.NetWallThickness, length = FD.NetFrontZ - FD.NetRearZ, midZ = (FD.NetRearZ + FD.NetFrontZ) * 0.5f, width = FD.NetHalfWidth * 2f;
            Wall(WallSide.Left, new Vector3(-FD.NetHalfWidth - t / 2f, FD.NetSideHeight / 2f, midZ), new Vector3(t, FD.NetSideHeight, length + 2f * t));
            Wall(WallSide.Right, new Vector3(FD.NetHalfWidth + t / 2f, FD.NetSideHeight / 2f, midZ), new Vector3(t, FD.NetSideHeight, length + 2f * t));
            Wall(WallSide.Rear, new Vector3(0f, FD.NetRearHeight / 2f, FD.NetRearZ - t / 2f), new Vector3(width, FD.NetRearHeight, t));
            Wall(WallSide.Front, new Vector3(0f, FD.NetFrontHeight / 2f, FD.NetFrontZ + t / 2f), new Vector3(width, FD.NetFrontHeight, t));

            var ballGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ballGo.SetActive(false);
            ballGo.name = "Ball";
            ballGo.layer = LayerMask.NameToLayer("Ball");
            ballGo.transform.SetParent(_root.transform);
            ballGo.transform.position = new Vector3(0f, 1f, 10f);
            ballGo.AddComponent<Rigidbody>().isKinematic = true;
            _ball = ballGo.AddComponent<BallController>();
            _ball.Configure(_config, null, ballGo.GetComponent<Renderer>());

            var scoreGo = new GameObject("ScoreManager");
            scoreGo.SetActive(false);
            scoreGo.transform.SetParent(_root.transform);
            _score = scoreGo.AddComponent<ScoreManager>();
            _score.Configure(_config, _ball, null, null);

            ballGo.SetActive(true);
            scoreGo.SetActive(true);
            _score.DeliveryFinalized += r => _results.Add(r);
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

        GameObject Box(string name, string layer, Vector3 centre, Vector3 size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.layer = LayerMask.NameToLayer(layer);
            go.transform.SetParent(_root.transform);
            go.transform.position = centre;
            go.transform.localScale = size;
            return go;
        }

        void Wall(WallSide side, Vector3 centre, Vector3 size) =>
            Box(side + "Wall", "Boundary", centre, size).AddComponent<NetWall>().side = side;

        int Launch(Vector3 position, Vector3 velocity)
        {
            int id = _ball.ActiveDeliveryId + 1;
            _ball.Launch(id, position, velocity);
            return id;
        }

        /// <summary>Launches from <paramref name="batPoint"/> as if just struck there with <paramref name="exitVelocity"/>.</summary>
        int Hit(Vector3 batPoint, Vector3 exitVelocity)
        {
            int id = Launch(batPoint, exitVelocity);
            _score.RecordBatContact(new BatContactReport { DeliveryId = id, Point = batPoint, BallVelocityOut = exitVelocity });
            return id;
        }

        IEnumerator WaitForResult(int id, float maxGameSeconds = 12f)
        {
            float until = Time.time + maxGameSeconds;
            while (Time.time < until && !_results.Exists(r => r.DeliveryId == id))
                yield return new WaitForFixedUpdate();
            Assert.IsTrue(_results.Exists(r => r.DeliveryId == id), $"delivery {id} never finalized");
        }

        IEnumerator WaitForBallToPark(float maxGameSeconds = 4f)
        {
            float until = Time.time + maxGameSeconds;
            while (Time.time < until && _ball.InFlight)
                yield return new WaitForFixedUpdate();
        }

        DeliveryResult ResultFor(int id)
        {
            var mine = _results.FindAll(r => r.DeliveryId == id);
            Assert.AreEqual(1, mine.Count, $"delivery {id} must finalize exactly once");
            return mine[0];
        }

        static Vector3 AimAtFrontNet(Vector3 from, float height, float horizontalSpeed)
        {
            float time = (FD.NetFrontZ - 0.036f - from.z) / horizontalSpeed;
            float vy = (height - from.y + 0.5f * -Physics.gravity.y * time * time) / time;
            return new Vector3(0f, vy, horizontalSpeed);
        }

        // ---------- Unhit deliveries ----------

        [UnityTest]
        public IEnumerator UnhitBallAtTheStumpsIsBowledOnce()
        {
            int id = Launch(new Vector3(0f, 0.4f, 3f), new Vector3(0f, 0f, -15f));
            yield return WaitForResult(id);
            yield return WaitForBallToPark();
            var r = ResultFor(id);
            Assert.AreEqual(DeliveryOutcome.Bowled, r.Outcome);
            Assert.IsFalse(r.BatContact);
            Assert.AreEqual(1, _results.Count, "nothing after the stump strike added a result");
            Assert.Contains((id, BallEndReason.Finalized), _ends);
        }

        [UnityTest]
        public IEnumerator UnhitBallIntoTheBackNetIsAMiss()
        {
            int id = Launch(new Vector3(0.6f, 0.5f, 3f), new Vector3(0f, 1f, -15f));
            yield return WaitForResult(id);
            var r = ResultFor(id);
            Assert.AreEqual(DeliveryOutcome.Miss, r.Outcome);
            Assert.AreEqual(DeliveryEnd.NetHit, r.End);
            Assert.AreEqual(ScoringZone.RearNet, r.Zone);
            Assert.AreEqual(CarryState.NotHit, r.Carry);
        }

        [UnityTest]
        public IEnumerator TimeoutWithoutContactIsAMiss()
        {
            _config.delivery.ballTimeout = 0.5f;
            int id = Launch(new Vector3(0f, 1f, 10f), new Vector3(0f, 8f, 0f));
            yield return WaitForResult(id, 3f);
            var r = ResultFor(id);
            Assert.AreEqual(DeliveryEnd.Timeout, r.End);
            Assert.AreEqual(DeliveryOutcome.Miss, r.Outcome);
        }

        // ---------- Hits ----------

        [UnityTest]
        public IEnumerator FrontNetHitsAreLabelledByHeight()
        {
            var cases = new[] { (0.5f, ScoringZone.FrontLow), (2f, ScoringZone.FrontMiddle), (4.5f, ScoringZone.FrontUpper) };
            var from = new Vector3(0f, 1f, 2f);
            foreach (var (height, zone) in cases)
            {
                int id = Hit(from, AimAtFrontNet(from, height, 25f));
                yield return WaitForResult(id);
                var r = ResultFor(id);
                Assert.AreEqual(zone, r.Zone, $"aimed at {height} m");
                Assert.AreEqual(DeliveryOutcome.Hit, r.Outcome, "no runs in Net Practice (REQ-009 amended)");
                Assert.AreEqual(CarryState.NetFirst, r.Carry, "reached the net on the full");
                yield return WaitForBallToPark();
            }
        }

        [UnityTest]
        public IEnumerator LandingBeforeTheNetGivesTheMeasuredCarry()
        {
            var from = new Vector3(0f, 1f, 2f);
            var v = new Vector3(0f, 2f, 10f);
            int id = Hit(from, v);
            yield return WaitForResult(id);
            var r = ResultFor(id);
            // Ballistic flight until the ball's centre is one radius above the ground.
            float g = -Physics.gravity.y, drop = from.y - _config.delivery.ballRadius;
            float t = (v.y + Mathf.Sqrt(v.y * v.y + 2f * g * drop)) / g;
            Assert.AreEqual(CarryState.Measured, r.Carry);
            Assert.AreEqual(v.z * t, r.CarryMetres, 0.15f);
            Assert.IsTrue(r.BatContact);
            StringAssert.StartsWith("Measured carry", r.CarryLabel);
        }

        [UnityTest]
        public IEnumerator ClearingTheFrontNetIsLabelled()
        {
            int id = Hit(new Vector3(0f, 1f, 2f), new Vector3(0f, 12f, 22f));
            yield return WaitForResult(id);
            var r = ResultFor(id);
            Assert.AreEqual(DeliveryEnd.LeftEnclosure, r.End);
            Assert.AreEqual(ScoringZone.OverFront, r.Zone);
            Assert.AreEqual(DeliveryOutcome.Hit, r.Outcome);
            Assert.AreEqual(CarryState.NoLanding, r.Carry);
        }

        [UnityTest]
        public IEnumerator HitIntoASideNetIsLabelled()
        {
            int id = Hit(new Vector3(0f, 1f, 2f), new Vector3(8f, 2f, 6f));
            yield return WaitForResult(id);
            var r = ResultFor(id);
            Assert.AreEqual(ScoringZone.SideNet, r.Zone);
            Assert.AreEqual(DeliveryOutcome.Hit, r.Outcome);
        }

        [UnityTest]
        public IEnumerator ContactThenBowledHasBothFlags()
        {
            int id = Hit(new Vector3(0f, 0.5f, 1.5f), new Vector3(0f, 0f, -8f));
            yield return WaitForResult(id);
            var r = ResultFor(id);
            Assert.AreEqual(DeliveryOutcome.Bowled, r.Outcome);
            Assert.IsTrue(r.BatContact && r.Bowled);
        }

        [UnityTest]
        public IEnumerator HitThatSettlesInsideTheNetIsAHitWithNoZone()
        {
            int id = Hit(new Vector3(0.5f, 0.5f, 5f), new Vector3(0f, -1f, 0f));
            yield return WaitForResult(id);
            var r = ResultFor(id);
            Assert.AreEqual(DeliveryEnd.Settled, r.End);
            Assert.AreEqual(DeliveryOutcome.Hit, r.Outcome);
            Assert.AreEqual(ScoringZone.None, r.Zone);
        }

        // ---------- Lifecycle ----------

        [UnityTest]
        public IEnumerator RelaunchBeforeAResultLeavesNoRecordForTheFirstBall()
        {
            int first = Launch(new Vector3(0.6f, 1f, 15f), new Vector3(0f, 0f, -10f));
            for (int i = 0; i < 20; i++) yield return new WaitForFixedUpdate();
            int second = Launch(new Vector3(0.6f, 0.5f, 3f), new Vector3(0f, 1f, -15f));
            yield return WaitForResult(second);
            Assert.IsFalse(_results.Exists(r => r.DeliveryId == first), "the cancelled ball has no result");
            Assert.AreEqual(1, _results.Count);
        }

        [UnityTest]
        public IEnumerator DecidedBallIsDeadThenParks()
        {
            var from = new Vector3(0f, 1f, 2f);
            int id = Hit(from, AimAtFrontNet(from, 2f, 25f));
            yield return WaitForResult(id);
            Assert.IsTrue(_ball.InFlight, "still visible during the linger");
            Assert.IsFalse(_ball.IsLive);
            Assert.IsFalse(_ball.ApplyBatHit(id, _ball.Body.position, Vector3.back * 20f), "a dead ball can't be struck");
            yield return WaitForBallToPark();
            Assert.IsFalse(_ball.InFlight);
            Assert.Contains((id, BallEndReason.Finalized), _ends);
            Assert.AreEqual(1, _results.Count);
        }
    }
}
