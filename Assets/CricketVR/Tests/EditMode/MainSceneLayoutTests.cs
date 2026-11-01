using System.Linq;
using NUnit.Framework;
using Unity.XR.CoreUtils;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem.XR;
using FD = CricketVR.FieldDimensions;

namespace CricketVR.Tests
{
    /// <summary>
    /// Checks the built Main scene against plan.md §5 (REQ-002, REQ-015).
    /// Run CricketVR > Setup > 6. Build or Repair Main Scene before these tests.
    /// </summary>
    public class MainSceneLayoutTests
    {
        const string ScenePath = "Assets/CricketVR/Scenes/Main.unity";

        [OneTimeSetUp]
        public void OpenScene()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Physics.SyncTransforms(); // collider bounds come from the physics scene, even in Edit Mode
        }

        static GameObject Find(string path)
        {
            var go = GameObject.Find(path);
            Assert.IsNotNull(go, $"'{path}' missing from {ScenePath}");
            return go;
        }

        [Test]
        public void SceneHasExactlyOneXrOrigin()
        {
            Assert.AreEqual(1, Object.FindObjectsByType<XROrigin>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length);
        }

        [Test]
        public void XrOriginUsesFloorTrackingAndStandsAtStanceFacingLauncher()
        {
            var origin = Object.FindAnyObjectByType<XROrigin>();
            Assert.AreEqual(XROrigin.TrackingOriginMode.Floor, origin.RequestedTrackingOriginMode);
            Assert.That(Vector3.Distance(origin.transform.position, FD.RightHandedStance), Is.LessThan(0.01f));
            Assert.That(Vector3.Angle(origin.transform.forward, Vector3.forward), Is.LessThan(1f));
        }

        /// <summary>The solver aims at the same pitch top the scene has (E-052): a 2 mm gap moved the pitch point ~1 cm.</summary>
        [Test]
        public void PitchColliderTopIsTheHeightTheSolverAimsAt()
        {
            var box = Find("Main/Environment/Pitch/Surface").GetComponent<BoxCollider>();
            Assert.AreEqual(FD.PitchSurfaceHeight, box.bounds.max.y, 1e-4f, "pitch collider top");
            const float r = 0.036f;
            Assert.AreEqual(box.bounds.max.y + r, DeliverySolver.ContactHeight(r), 1e-4f, "solver contact height");
        }

        [Test]
        public void StumpColliderMatchesRegulationSizeAtOrigin()
        {
            var box = Find("Main/Environment/Stumps").GetComponent<BoxCollider>();
            Assert.IsNotNull(box, "Stumps need a BoxCollider for wicket detection");
            var b = box.bounds;
            Assert.AreEqual(0f, b.min.y, 0.001f, "stumps should stand on the ground");
            Assert.AreEqual(FD.StumpHeight, b.size.y, 0.002f);
            Assert.AreEqual(FD.StumpSetWidth, b.size.x, 0.002f);
            Assert.AreEqual(0f, b.center.x, 0.001f);
            Assert.AreEqual(0f, b.center.z, 0.001f);
        }

        [Test]
        public void StumpsAreTwentyPointOneTwoMetresApart()
        {
            var striker = Find("Main/Environment/Stumps").transform.position;
            var nonStriker = Find("Main/Environment/Pitch/NonStrikerStumps").transform.position;
            Assert.AreEqual(20.12f, Vector3.Distance(striker, nonStriker), 0.001f);
        }

        [Test]
        public void PitchSurfaceTopIsAtGroundLevel()
        {
            var surface = Find("Main/Environment/Pitch/Surface").GetComponent<Collider>();
            Assert.That(surface.bounds.max.y, Is.InRange(0f, 0.005f));
            Assert.AreEqual(FD.PitchWidth, surface.bounds.size.x, 0.001f);
        }

        [Test]
        public void LauncherHasNoCollidersSoBallsPassThrough()
        {
            var launcher = Find("Main/Environment/LauncherAnchor");
            Assert.AreEqual(0, launcher.GetComponentsInChildren<Collider>(true).Length);
            Assert.That(Vector3.Distance(launcher.transform.position, FD.DefaultReleasePoint), Is.LessThan(0.001f));
        }

        [Test]
        public void NetWallsEncloseTheLaneOnTheDocumentedPlanes()
        {
            var walls = Find("Main/Environment/NetBoundaryColliders").GetComponentsInChildren<BoxCollider>();
            Assert.AreEqual(4, walls.Length);
            var byName = walls.ToDictionary(w => w.name, w => w.bounds);
            Assert.AreEqual(-FD.NetHalfWidth, byName["LeftWall"].max.x, 0.001f);
            Assert.AreEqual(FD.NetHalfWidth, byName["RightWall"].min.x, 0.001f);
            Assert.AreEqual(FD.NetRearZ, byName["RearWall"].max.z, 0.001f);
            Assert.AreEqual(FD.NetFrontZ, byName["FrontWall"].min.z, 0.001f);
            Assert.AreEqual(FD.NetFrontHeight, byName["FrontWall"].size.y, 0.001f);
        }

        /// <summary>
        /// REQ-015 / AC-02: nothing visible may sit between the batter's eyes and the release point.
        /// Uses a range of eye heights because real players differ.
        /// </summary>
        [TestCase(1.4f)]
        [TestCase(1.6f)]
        [TestCase(1.9f)]
        public void SightLineFromStanceToReleaseIsUnobstructed(float eyeHeight)
        {
            var eye = FD.RightHandedStance + Vector3.up * eyeHeight;
            var release = FD.DefaultReleasePoint;
            var launcher = Find("Main/Environment/LauncherAnchor").transform;
            var player = Find("Main/Player").transform;
            var gameplay = Find("Main/Gameplay").transform; // the ball is meant to fly down this line

            var ray = new Ray(eye, (release - eye).normalized);
            float length = Vector3.Distance(eye, release) - 0.05f; // stop just short of the launcher mouth

            var blockers = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None)
                .Where(r => r.enabled && r.gameObject.activeInHierarchy)
                .Where(r => !r.transform.IsChildOf(launcher) && !r.transform.IsChildOf(player) && !r.transform.IsChildOf(gameplay))
                .Where(r => r.bounds.IntersectRay(ray, out float d) && d < length)
                .Select(r => r.name)
                .ToArray();

            CollectionAssert.IsEmpty(blockers, "objects in the delivery sight line");
        }

        [Test]
        public void ScoreboardIsReadableDistanceAndWellOffTheSightLine()
        {
            var board = Find("Main/UI/LiveScoreboard").transform.position;
            var eye = FD.RightHandedStance + Vector3.up * 1.6f;
            float distance = Vector3.Distance(eye, board);
            Assert.That(distance, Is.InRange(1.5f, 3.5f));

            var toRelease = FD.DefaultReleasePoint - eye;
            Assert.That(Vector3.Angle(toRelease, board - eye), Is.GreaterThan(30f), "scoreboard too close to the delivery line");
            Assert.That(Mathf.Abs(board.x), Is.LessThan(FD.NetHalfWidth), "scoreboard should stay inside the net");
        }
    
        /// <summary>
        /// Regression (E-009): the bat must follow the grip pose (in line with the handle), not the aim pose
        /// that the Starter Assets controllers use.
        /// </summary>
        [Test]
        public void BatFollowsGripPoseTrackersNotAimPose()
        {
            var bat = Object.FindAnyObjectByType<BatController>();
            Assert.IsNotNull(bat, "BatController missing");
            var so = new UnityEditor.SerializedObject(bat);
            foreach (var field in new[] { "leftController", "rightController" })
            {
                var t = so.FindProperty(field).objectReferenceValue as Transform;
                Assert.IsNotNull(t, field);
                var driver = t.GetComponent<TrackedPoseDriver>();
                Assert.IsNotNull(driver, $"{field} ({t.name}) needs its own grip-pose TrackedPoseDriver");
                StringAssert.EndsWith("/devicePosition", driver.positionInput.action.bindings[0].path);
                StringAssert.EndsWith("/deviceRotation", driver.rotationInput.action.bindings[0].path);
            }
        }
    
        /// <summary>
        /// The realistic blade's flat hitting face must coincide with the physics proxy's face (bat frame z = +thickness/2),
        /// and its outline must match the proxy's width and length, so the ball meets the bat where the player sees it.
        /// </summary>
        [Test]
        public void VisibleBladeFaceMatchesPhysicsProxyFace()
        {
            var config = UnityEditor.AssetDatabase.LoadAssetAtPath<GameConfig>("Assets/CricketVR/Settings/GameConfig.asset");
            var g = config.bat.geometry;
            var blade = Find("Main/Player/Bat/BatVisual/Blade").GetComponent<MeshFilter>().sharedMesh;
            Assert.IsNotNull(blade);
            var b = blade.bounds;
            Assert.AreEqual(g.bladeThickness * 0.5f, b.max.z, 0.0005f, "hitting face plane");
            Assert.AreEqual(g.bladeWidth, b.size.x, 0.001f, "blade width");
            Assert.AreEqual(-g.gripToBlade - g.bladeLength, b.min.y, 0.001f, "toe");
            Assert.That(b.size.z, Is.InRange(0.055f, 0.067f), "deepest point within the Laws' 67 mm");
        }
    }
}
