using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CricketVR.Tests
{
    /// <summary>
    /// Bat mode (AC-34, PlayMode): the real BatController with stand-in controller transforms (no XR device): the bat on
    /// the strapped controller, and the fit (frozen bat, ghost controller moved, saved or cancelled).
    /// </summary>
    public class BatModePlayModeTests
    {
        static readonly string[] PrefKeys =
        {
            "CricketVR.BatMode", "CricketVR.Mount.v1.fitted",
            "CricketVR.Mount.v1.px", "CricketVR.Mount.v1.py", "CricketVR.Mount.v1.pz",
            "CricketVR.Mount.v1.qx", "CricketVR.Mount.v1.qy", "CricketVR.Mount.v1.qz", "CricketVR.Mount.v1.qw",
            "CricketVR.Strap.v1.tilt", "CricketVR.Strap.v1.face", "CricketVR.Strap.v1.lean",
            "CricketVR.Strap.v1.slide", "CricketVR.Strap.v1.spin", "CricketVR.Strap.v1.flipped",
        };

        GameObject _root;
        Transform _right, _left;
        BatController _bat;
        Transform _visual, _ghost;
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
            _visual = visual;
            _ghost = new GameObject("FitGhost").transform;
            _ghost.SetParent(visual);
            var proxyGo = new GameObject("BatPhysicsProxy");
            proxyGo.transform.SetParent(batGo.transform);
            _proxy = proxyGo.AddComponent<Rigidbody>();
            var box = proxyGo.AddComponent<BoxCollider>();
            _bat = batGo.AddComponent<BatController>();
            _bat.Configure(_config, _left, _right, visual, null, _proxy, box, requireTracked: false);
            _bat.ConfigureFit(_ghost);
            batGo.SetActive(true);
        }

        [TearDown]
        public void TearDown()
        {
            Object.Destroy(_root);
            Object.Destroy(_config);
            ClearPrefs();
        }

        Vector3 WorldToe() => _bat.GripPose.position + _bat.GripPose.rotation * G.Toe;

        /// <summary>The controller's pose in the live bat frame (where the game thinks it is taped).</summary>
        Pose ControllerInBat()
        {
            var inv = Quaternion.Inverse(_bat.GripPose.rotation);
            return new Pose(inv * (_right.position - _bat.GripPose.position), inv * _right.rotation);
        }

        static readonly Pose Display = new Pose(new Vector3(0.6f, 1.25f, 0.65f), Quaternion.Euler(0f, 200f, 0f));

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
        public IEnumerator FitFreezesTheBatAndSavesWhereTheGhostWasMoved()
        {
            _bat.SetMode(BatMode.Strapped);
            _right.SetPositionAndRotation(new Vector3(0.3f, 0.9f, 0.8f), Quaternion.Euler(20f, 40f, -10f));
            yield return null;
            var before = ControllerInBat();

            _bat.BeginFit(Display);
            Assert.IsTrue(_bat.Fitting);
            Assert.IsTrue(_ghost.gameObject.activeSelf, "ghost shown");
            Assert.Less(Vector3.Distance(_visual.position, Display.position), 1e-5f, "bat frozen at the display pose");
            Assert.Less(Vector3.Distance(_ghost.localPosition, before.position), 1e-4f, "ghost starts where the controller is on the bat");
            Assert.Less(Quaternion.Angle(_ghost.localRotation, before.rotation), 0.05f);

            _right.SetPositionAndRotation(new Vector3(-0.5f, 1.4f, 0.2f), Quaternion.Euler(-70f, 10f, 30f));
            yield return null;
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            Assert.Less(Vector3.Distance(_visual.position, Display.position), 1e-5f, "the frozen bat ignores the controller");
            Assert.IsFalse(_proxy.GetComponent<BoxCollider>().enabled, "no contacts while fitting");

            _bat.FitMove(3);
            Assert.AreEqual(before.position.y + 0.03f, _ghost.localPosition.y, 1e-4f, "B moves it 3 cm up the bat");
            var placed = _ghost.localPosition;
            _bat.FitTurn(2);
            _bat.FitTilt(-1);
            Assert.Less(Vector3.Distance(_ghost.localPosition, placed), 1e-5f, "turns and tilts are in place");
            Assert.AreEqual(Mathf.Sqrt(10f * 10f + 5f * 5f), Quaternion.Angle(_ghost.localRotation, before.rotation), 0.3f, "10° turn and 5° tilt");
            var fitted = _bat.FitController;

            _bat.EndFit(true);
            Assert.IsFalse(_bat.Fitting);
            Assert.IsFalse(_ghost.gameObject.activeSelf);
            Assert.AreEqual(_right, _visual.parent, "the bat follows the controller again");
            Assert.IsTrue(_bat.MountFitted);
            yield return null;
            var now = ControllerInBat();
            Assert.Less(Vector3.Distance(now.position, fitted.position), 1e-4f, "saved: the controller sits where the ghost was put");
            Assert.Less(Quaternion.Angle(now.rotation, fitted.rotation), 0.05f);
            Assert.AreEqual(1, PlayerPrefs.GetInt("CricketVR.Mount.v1.fitted"), "remembered");
        }

        [UnityTest]
        public IEnumerator FitMovesAcrossAndOffTheBladeAndRollsInPlace()
        {
            _bat.SetMode(BatMode.Strapped);
            _right.SetPositionAndRotation(new Vector3(0f, 1f, 0.5f), Quaternion.identity);
            yield return null;
            Assert.IsFalse(_bat.HitsWithControllerSide, "controller on the back");
            _bat.BeginFit(Display);
            var start = _bat.FitController;

            _bat.FitMove(0, across: 2);
            // Shown with the back (bat −Z) toward the player, the player's right is bat +X.
            Assert.AreEqual(start.position.x + 0.02f, _bat.FitController.position.x, 1e-5f, "across, to the player's right");
            _bat.FitMove(0, outward: -3);
            Assert.AreEqual(start.position.z + 0.03f, _bat.FitController.position.z, 1e-5f, "onto the blade: toward bat +Z from the back");
            _bat.FitMove(0, outward: -100);
            Assert.AreEqual(BatMount.MaxOut, _bat.FitController.position.z, 1e-5f, "kept within reach");

            var before = _bat.FitController;
            _bat.FitRoll(3);
            Assert.Less(Vector3.Distance(_bat.FitController.position, before.position), 1e-6f, "rolls in place");
            var axisBefore = before.rotation * Vector3.forward;
            var axisAfter = _bat.FitController.rotation * Vector3.forward;
            Assert.Less(Vector3.Angle(axisBefore, axisAfter), 0.05f, "about its own length");
            Assert.AreEqual(15f, Quaternion.Angle(before.rotation, _bat.FitController.rotation), 0.1f);

            var fitted = _bat.FitController;
            _bat.EndFit(true);
            yield return null;
            var now = ControllerInBat();
            Assert.Less(Vector3.Distance(now.position, fitted.position), 1e-4f);
            Assert.Less(Quaternion.Angle(now.rotation, fitted.rotation), 0.05f);
        }

        [UnityTest]
        public IEnumerator CancelledFitChangesNothing()
        {
            _bat.SetMode(BatMode.Strapped);
            var mount = _bat.Mount;
            _bat.BeginFit(Display);
            _bat.FitMove(5);
            _bat.FitTurn(7);
            _bat.FitRoll(2);
            _bat.EndFit(false);
            yield return null;
            Assert.IsFalse(_bat.MountFitted);
            Assert.AreEqual(mount.position, _bat.Mount.position);
            Assert.AreEqual(mount.rotation, _bat.Mount.rotation);
            Assert.AreEqual(_right, _visual.parent);
        }

        [UnityTest]
        public IEnumerator FineTuningSitsOnTopOfTheMountAndResets()
        {
            _bat.SetMode(BatMode.Strapped);
            _bat.SetMount(BatMount.Inverse(new Pose(new Vector3(-0.025f, -0.16f, -0.05f),
                Quaternion.LookRotation(Vector3.down, Vector3.back) * Quaternion.Euler(-5f, 8f, -4f))));
            _right.SetPositionAndRotation(new Vector3(0f, 1f, 0.5f), Quaternion.identity);
            yield return null;
            var fitted = _bat.GripPose;

            _bat.NudgeAlignment(2, 0);
            yield return null;
            Assert.AreEqual(10f, Quaternion.Angle(_bat.GripPose.rotation, fitted.rotation), 0.1f, "two 5° tilt steps");

            _bat.FlipFace();
            _bat.ResetAlignment();
            yield return null;
            Assert.IsTrue(_bat.HitsWithControllerSide, "reset keeps the face swap");
            Assert.Less(Vector3.Distance(WorldToe(), fitted.position + fitted.rotation * G.Toe), 1e-4f, "the swap keeps the toe");
            Assert.Less(Vector3.Distance(_bat.GripPose.position, fitted.position), 1e-4f);
        }
    }
}
