using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CricketVR.Tests
{
    /// <summary>
    /// Bat mode (AC-34, PlayMode): the real BatController and BatMountCalibrator with stand-in controller transforms
    /// (no XR device). The calibration is fed simulated controller poses of a crookedly taped controller.
    /// </summary>
    public class BatModePlayModeTests
    {
        static readonly string[] PrefKeys =
        {
            "CricketVR.BatMode", "CricketVR.Mount.v1.calibrated",
            "CricketVR.Mount.v1.px", "CricketVR.Mount.v1.py", "CricketVR.Mount.v1.pz",
            "CricketVR.Mount.v1.qx", "CricketVR.Mount.v1.qy", "CricketVR.Mount.v1.qz", "CricketVR.Mount.v1.qw",
            "CricketVR.Strap.v1.tilt", "CricketVR.Strap.v1.face", "CricketVR.Strap.v1.lean",
            "CricketVR.Strap.v1.slide", "CricketVR.Strap.v1.spin", "CricketVR.Strap.v1.flipped",
        };

        GameObject _root;
        Transform _right, _left;
        BatController _bat;
        BatMountCalibrator _calibrator;
        Rigidbody _proxy;
        GameConfig _config;
        BatGeometry G => _config.bat.geometry;

        static void ClearPrefs()
        {
            foreach (var k in PrefKeys) PlayerPrefs.DeleteKey(k);
            PlayerPrefs.Save();
        }

        [SetUp]
        public void SetUp()
        {
            ClearPrefs();
            _config = ScriptableObject.CreateInstance<GameConfig>();
            _root = new GameObject("TestBatModeRig");
            _right = new GameObject("Right Controller").transform;
            _left = new GameObject("Left Controller").transform;
            _right.SetParent(_root.transform);
            _left.SetParent(_root.transform);

            var batGo = new GameObject("Bat");
            batGo.SetActive(false);
            batGo.transform.SetParent(_root.transform);
            var visual = new GameObject("BatVisual").transform;
            visual.SetParent(batGo.transform);
            var proxyGo = new GameObject("BatPhysicsProxy");
            proxyGo.transform.SetParent(batGo.transform);
            _proxy = proxyGo.AddComponent<Rigidbody>();
            var box = proxyGo.AddComponent<BoxCollider>();
            _bat = batGo.AddComponent<BatController>();
            _bat.Configure(_config, _left, _right, visual, null, _proxy, box, requireTracked: false);
            _calibrator = batGo.AddComponent<BatMountCalibrator>();
            _calibrator.Configure(_config, _bat);
            _calibrator.requireTrackedState = false;
            batGo.SetActive(true);
        }

        [TearDown]
        public void TearDown()
        {
            Object.Destroy(_root);
            Object.Destroy(_config);
            ClearPrefs();
        }

        /// <summary>Controller taped crooked and off-centre on the back.</summary>
        static Pose TrueMount() => BatMount.Inverse(new Pose(new Vector3(-0.025f, -0.16f, -0.05f),
            Quaternion.LookRotation(Vector3.down, Vector3.back) * Quaternion.Euler(-5f, 8f, -4f)));

        Vector3 HandleTop => new Vector3(0f, G.handleLength - G.gripToBlade, 0f);

        /// <summary>Feeds the calibrator the controller poses of the real bat rocking about one of its points on the floor.</summary>
        void Rock(Pose mount, Vector3 batPoint, Vector3 floor, float degrees, float yawDegrees = 60f)
        {
            var local = mount.position + mount.rotation * batPoint;
            for (int i = 0; i < 240; i++)
            {
                float t = i / 240f * Mathf.PI * 4f;
                var r = Quaternion.Euler(degrees * Mathf.Sin(t), yawDegrees * Mathf.Sin(t * 0.3f), degrees * Mathf.Cos(t * 1.3f));
                _calibrator.AddSample(new Pose(floor - r * local, r));
            }
        }

        /// <summary>Feeds poses of the bat lying flat on the floor, controller side up.</summary>
        void LayFlat(Pose mount)
        {
            // Rotation taking the controller-side normal (bat −Z, in the controller frame) to world up.
            var r = Quaternion.FromToRotation(mount.rotation * Vector3.back, Vector3.up);
            var p = new Vector3(0.2f, 0.07f, 0.9f);
            for (int i = 0; i < 100; i++)
                _calibrator.AddSample(new Pose(p, Quaternion.Euler(0f, i * 0.01f, 0f) * r));
        }

        Vector3 WorldToe() => _bat.GripPose.position + _bat.GripPose.rotation * G.Toe;

        [UnityTest]
        public IEnumerator StrappedBatFollowsTheMountOnTheStrappedController()
        {
            _bat.SetMode(BatMode.Strapped);
            Assert.AreEqual(BatHand.Right, _bat.Hand);
            _right.SetPositionAndRotation(new Vector3(0.3f, 0.9f, 0.8f), Quaternion.Euler(20f, 40f, -10f));
            yield return null;
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            var mount = _bat.Mount;
            Assert.Less(Vector3.Distance(_bat.GripPose.position, _right.TransformPoint(mount.position)), 1e-4f);
            Assert.Less(Quaternion.Angle(_bat.GripPose.rotation, _right.rotation * mount.rotation), 0.01f);
            var blade = _bat.GripPose.position + _bat.GripPose.rotation * G.BladeCenter;
            Assert.Less(Vector3.Distance(_proxy.position, blade), 0.002f, "the physics blade follows the strapped bat");
            Assert.IsFalse(_bat.HitsWithControllerSide, "controller on the back: hit with the other side");

            _bat.SetBatHand(BatHand.Left);
            Assert.AreEqual(BatHand.Right, _bat.Hand, "the bat can't leave the strapped controller");
            Assert.AreEqual((int)BatMode.Strapped, PlayerPrefs.GetInt("CricketVR.BatMode"), "remembered");

            _bat.SetMode(BatMode.InHand);
            yield return null;
            var a = _bat.Alignment;
            var hand = BatFrame.GripRotation(BatHand.Right, a.tilt, a.faceTurn, a.lean, a.faceFlipped, a.spin);
            Assert.Less(Quaternion.Angle(_bat.GripPose.rotation, _right.rotation * hand), 0.01f, "back to the in-hand grip");
        }

        [UnityTest]
        public IEnumerator CalibrationPutsTheVirtualToeOnTheRealToe()
        {
            _bat.SetMode(BatMode.Strapped);
            Assert.IsFalse(_bat.MountCalibrated);
            var truth = TrueMount();

            _calibrator.Begin();
            Assert.AreEqual(BatMountCalibrator.Step.ToeReady, _calibrator.Current);
            _calibrator.Advance();
            Rock(truth, G.Toe, new Vector3(0.1f, 0f, 0.6f), 30f);
            _calibrator.FinishStep();
            Assert.AreEqual(BatMountCalibrator.Step.TopReady, _calibrator.Current, _calibrator.Instructions);
            _calibrator.Advance();
            Rock(truth, HandleTop, new Vector3(-0.2f, 0f, 0.7f), 30f);
            _calibrator.FinishStep();
            Assert.AreEqual(BatMountCalibrator.Step.FlatReady, _calibrator.Current, _calibrator.Instructions);
            _calibrator.Advance();
            LayFlat(truth);
            _calibrator.FinishStep();
            Assert.AreEqual(BatMountCalibrator.Step.Idle, _calibrator.Current);
            Assert.IsTrue(_bat.MountCalibrated, _calibrator.LastResult);

            // Put the real bat anywhere: its toe (by the true mount) and the virtual toe coincide.
            _right.SetPositionAndRotation(new Vector3(0.4f, 1.1f, 0.5f), Quaternion.Euler(-60f, 25f, 70f));
            yield return null;
            var realToe = _right.TransformPoint(truth.position + truth.rotation * G.Toe);
            Assert.Less(Vector3.Distance(WorldToe(), realToe), 0.005f, "virtual toe on the real toe");
            var realFace = _right.rotation * (truth.rotation * Vector3.forward);
            Assert.Less(Vector3.Angle(_bat.GripPose.rotation * Vector3.forward, realFace), 1.5f, "hitting face");
        }

        [UnityTest]
        public IEnumerator FailedOrCancelledCalibrationChangesNothing()
        {
            _bat.SetMode(BatMode.Strapped);
            var before = _bat.Mount;
            _calibrator.Begin();
            _calibrator.Advance();
            Rock(TrueMount(), G.Toe, Vector3.zero, 5f, 5f); // barely rocked
            _calibrator.FinishStep();
            Assert.AreEqual(BatMountCalibrator.Step.ToeReady, _calibrator.Current, "the step is repeated");
            StringAssert.Contains("Rock it further", _calibrator.Instructions);

            _calibrator.Advance();
            Rock(TrueMount(), G.Toe, new Vector3(0f, 0.3f, 0f), 30f); // held 30 cm off the floor
            _calibrator.FinishStep();
            Assert.AreEqual(BatMountCalibrator.Step.ToeReady, _calibrator.Current);
            StringAssert.Contains("floor", _calibrator.Instructions);

            _calibrator.Cancel();
            Assert.IsFalse(_calibrator.Active);
            Assert.IsFalse(_bat.MountCalibrated);
            Assert.AreEqual(before.position, _bat.Mount.position);
            Assert.AreEqual(before.rotation, _bat.Mount.rotation);
            yield return null;
        }

        [UnityTest]
        public IEnumerator FineTuningSitsOnTopOfTheMountAndResets()
        {
            _bat.SetMode(BatMode.Strapped);
            _bat.SetMount(TrueMount());
            _right.SetPositionAndRotation(new Vector3(0f, 1f, 0.5f), Quaternion.identity);
            yield return null;
            var calibrated = _bat.GripPose;

            _bat.NudgeAlignment(2, 0);
            yield return null;
            Assert.AreEqual(10f, Quaternion.Angle(_bat.GripPose.rotation, calibrated.rotation), 0.1f, "two 5° tilt steps");

            _bat.FlipFace();
            _bat.ResetAlignment();
            yield return null;
            Assert.IsTrue(_bat.HitsWithControllerSide, "reset keeps the face swap");
            Assert.Less(Vector3.Distance(WorldToe(), calibrated.position + calibrated.rotation * G.Toe), 1e-4f, "the swap keeps the toe");
            Assert.Less(Vector3.Distance(_bat.GripPose.position, calibrated.position), 1e-4f);
        }
    }
}
