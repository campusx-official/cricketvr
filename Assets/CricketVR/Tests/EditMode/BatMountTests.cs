using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace CricketVR.Tests
{
    /// <summary>Bat mode (AC-34, EditMode): the strapped-bat mount and its calibration maths (plan.md §21).</summary>
    public class BatMountTests
    {
        static readonly BatGeometry G = BatGeometry.Default;
        static Vector3 HandleTop => new Vector3(0f, G.handleLength - G.gripToBlade, 0f);

        /// <summary>A controller taped slightly crooked, off-centre on the back, as on the owner's bat.</summary>
        static Pose CrookedMount()
        {
            var controllerInBat = new Pose(new Vector3(0.03f, -0.15f, -0.05f),
                Quaternion.LookRotation(Vector3.down, Vector3.back) * Quaternion.Euler(4f, -9f, 6f));
            return BatMount.Inverse(controllerInBat);
        }

        static Vector3 InController(Pose mount, Vector3 batPoint) => mount.position + mount.rotation * batPoint;

        /// <summary>Controller poses while the bat rocks about <paramref name="batPoint"/> held at a world point on the floor.</summary>
        static List<Pose> Rock(Pose mount, Vector3 batPoint, Vector3 world, float degrees, float noise, int seed)
        {
            var rng = new System.Random(seed);
            float N() => ((float)rng.NextDouble() - 0.5f) * 2f * noise;
            var local = InController(mount, batPoint);
            var samples = new List<Pose>();
            for (int i = 0; i < 200; i++)
            {
                float t = i / 200f * Mathf.PI * 4f;
                var r = Quaternion.Euler(degrees * Mathf.Sin(t), 90f * Mathf.Sin(t * 0.37f), degrees * Mathf.Cos(t * 1.3f));
                samples.Add(new Pose(world - r * local + new Vector3(N(), N(), N()), r));
            }
            return samples;
        }

        [Test]
        public void DefaultMountPutsTheBladeBelowTheControllerAndTheFaceAwayFromIt()
        {
            var back = BatMount.DefaultPose(G, controllerOnFace: false);
            var toe = InController(back, G.Toe);
            Assert.Greater(toe.z, 0.4f, "toe runs out past the controller's buttons end (grip +Z)");
            Assert.Less(Mathf.Abs(toe.x), 0.01f);
            var faceNormal = back.rotation * Vector3.forward;
            Assert.Less(Vector3.Angle(faceNormal, Vector3.down), 1f, "controller on the back: the hitting face looks away from its buttons");
            Assert.Less(InController(back, Vector3.zero).y, 0f, "the bat's centre is below the controller");

            var face = BatMount.DefaultPose(G, controllerOnFace: true);
            Assert.Less(Vector3.Angle(face.rotation * Vector3.forward, Vector3.up), 1f);
            Assert.Less(Quaternion.Angle(face.rotation, BatFrame.BaseRotation(BatHand.Right)), 0.01f, "on the face: the in-hand base frame");
        }

        [Test]
        public void PivotFindsThePointThatStayedStill()
        {
            var mount = CrookedMount();
            var floor = new Vector3(0.4f, 0.004f, 1.1f);
            var samples = Rock(mount, G.Toe, floor, 30f, 0.002f, 1);
            Assert.IsTrue(BatMount.SolvePivot(samples, out var local, out var world, out float rms));
            Assert.Less(Vector3.Distance(local, InController(mount, G.Toe)), 0.003f, "toe in the controller frame");
            Assert.Less(Vector3.Distance(world, floor), 0.003f, "toe on the floor");
            Assert.Less(rms, 0.005f);
        }

        [Test]
        public void PivotNeedsRocking()
        {
            var still = Rock(CrookedMount(), G.Toe, Vector3.zero, 3f, 0f, 2);
            for (int i = 0; i < still.Count; i++) still[i] = new Pose(still[i].position, Quaternion.Euler(0f, i * 0.05f, 0f));
            Assert.IsFalse(BatMount.SolvePivot(still, out _, out _, out _), "a bat held still fixes no point");
            Assert.IsFalse(BatMount.SolvePivot(new List<Pose>(), out _, out _, out _));
        }

        [Test]
        public void PivotReportsAMovingEnd()
        {
            var samples = Rock(CrookedMount(), G.Toe, Vector3.zero, 30f, 0f, 3);
            for (int i = 0; i < samples.Count; i++) // the toe slides 8 cm across the floor
                samples[i] = new Pose(samples[i].position + Vector3.right * (0.08f * i / samples.Count), samples[i].rotation);
            Assert.IsTrue(BatMount.SolvePivot(samples, out _, out _, out float rms));
            Assert.Greater(rms, BatMount.MaxPivotRms, "a sliding toe fails the quality check");
        }

        [Test]
        public void CalibrationRecoversACrookedMount()
        {
            var mount = CrookedMount();
            Assert.IsTrue(BatMount.SolvePivot(Rock(mount, G.Toe, new Vector3(0f, 0f, 1f), 30f, 0.002f, 4), out var toe, out _, out _));
            Assert.IsTrue(BatMount.SolvePivot(Rock(mount, HandleTop, new Vector3(0.5f, 0f, 1f), 30f, 0.002f, 5), out var top, out _, out _));
            // Flat on the floor, controller side up: up is the back's normal, tipped 2° by the spine.
            var up = Quaternion.AngleAxis(2f, mount.rotation * Vector3.up) * (mount.rotation * Vector3.back);

            Assert.IsTrue(BatMount.TryFromCalibration(toe, top, up, false, G, out var measured, out var problem), problem);
            Assert.Less(Vector3.Distance(InController(measured, G.Toe), InController(mount, G.Toe)), 0.005f, "virtual toe on the real toe");
            Assert.Less(Vector3.Distance(InController(measured, G.BladeCenter), InController(mount, G.BladeCenter)), 0.005f, "blade centre");
            Assert.Less(Vector3.Angle(measured.rotation * Vector3.up, mount.rotation * Vector3.up), 1f, "along the bat");
            Assert.Less(Vector3.Angle(measured.rotation * Vector3.forward, mount.rotation * Vector3.forward), 2.5f, "hitting face");
        }

        [Test]
        public void CalibrationRejectsBadMeasurements()
        {
            var mount = CrookedMount();
            var toe = InController(mount, G.Toe);
            var top = InController(mount, HandleTop);
            var up = mount.rotation * Vector3.back;
            Assert.IsTrue(BatMount.TryFromCalibration(toe, top, up, false, G, out _, out _));

            var shortTop = toe + (top - toe) * 0.7f;
            Assert.IsFalse(BatMount.TryFromCalibration(toe, shortTop, up, false, G, out _, out var length));
            StringAssert.Contains("apart", length);

            var tilted = Quaternion.AngleAxis(40f, mount.rotation * Vector3.right) * up;
            Assert.IsFalse(BatMount.TryFromCalibration(toe, top, tilted, false, G, out _, out var flat));
            StringAssert.Contains("flat", flat);
        }

        [Test]
        public void ControllerSideDecidesTheHittingFace()
        {
            var mount = CrookedMount();
            var toe = InController(mount, G.Toe);
            var top = InController(mount, HandleTop);
            var controllerSideUp = mount.rotation * Vector3.back;
            BatMount.TryFromCalibration(toe, top, controllerSideUp, false, G, out var onBack, out _);
            BatMount.TryFromCalibration(toe, top, controllerSideUp, true, G, out var onFace, out _);
            Assert.Less(Vector3.Angle(onBack.rotation * Vector3.forward, -controllerSideUp), 1f, "on the back: hits with the other side");
            Assert.Less(Vector3.Angle(onFace.rotation * Vector3.forward, controllerSideUp), 1f);
            Assert.Less(Vector3.Distance(InController(onBack, G.Toe), InController(onFace, G.Toe)), 1e-4f, "same toe either way");
        }
    }
}
