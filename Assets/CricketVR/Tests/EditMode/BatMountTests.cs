using NUnit.Framework;
using UnityEngine;

namespace CricketVR.Tests
{
    /// <summary>Bat mode (AC-34, EditMode): the strapped-bat mount maths (plan.md §21).</summary>
    public class BatMountTests
    {
        static readonly BatGeometry G = BatGeometry.Default;

        static Vector3 InController(Pose mount, Vector3 batPoint) => mount.position + mount.rotation * batPoint;

        [Test]
        public void DefaultMountPutsTheBladeBelowTheControllerAndTheFaceAwayFromIt()
        {
            var back = BatMount.DefaultPose(G, controllerOnFace: false);
            var toe = InController(back, G.Toe);
            Assert.Greater(toe.z, 0.4f, "toe runs out past the controller's buttons end (grip +Z)");
            Assert.Less(Mathf.Abs(toe.x), 0.01f);
            Assert.Less(Vector3.Angle(back.rotation * Vector3.forward, Vector3.down), 1f, "controller on the back: the hitting face looks away from its buttons");
            Assert.Less(InController(back, Vector3.zero).y, 0f, "the bat's centre is below the controller");

            var face = BatMount.DefaultPose(G, controllerOnFace: true);
            Assert.Less(Vector3.Angle(face.rotation * Vector3.forward, Vector3.up), 1f);
            Assert.Less(Quaternion.Angle(face.rotation, BatFrame.BaseRotation(BatHand.Right)), 0.01f, "on the face: the in-hand base frame");
        }

        [Test]
        public void InverseUndoesAPose()
        {
            var p = new Pose(new Vector3(0.1f, -0.2f, 0.03f), Quaternion.Euler(10f, 70f, -30f));
            var q = BatMount.Inverse(p);
            var point = new Vector3(0.3f, 0.5f, -0.4f);
            var there = p.position + p.rotation * point;
            Assert.Less(Vector3.Distance(q.position + q.rotation * there, point), 1e-5f);
        }

        [Test]
        public void TheControllerCanBeFittedFromTheToeToTheHandleTop()
        {
            Assert.AreEqual(G.Toe.y, BatMount.ClampAlong(G, -5f), 1e-6f);
            Assert.AreEqual(G.handleLength - G.gripToBlade, BatMount.ClampAlong(G, 5f), 1e-6f);
            Assert.AreEqual(-0.2f, BatMount.ClampAlong(G, -0.2f), 1e-6f);
        }
    }
}
