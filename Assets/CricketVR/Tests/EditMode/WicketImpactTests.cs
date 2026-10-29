using NUnit.Framework;
using UnityEngine;
using FD = CricketVR.FieldDimensions;

namespace CricketVR.Tests
{
    /// <summary>T084 (AC-32, EditMode): the wicket reaction's numbers from the impact (plan.md §20).</summary>
    public class WicketImpactTests
    {
        const float R = 0.036f;
        // Velocities at the stumps: a ball loses pace off the pitch (140 km/h released arrives at about 117 km/h).
        static readonly Vector3 Yorker140 = new Vector3(0f, 1.5f, -32.5f);
        static readonly Vector3 OffBreak80 = new Vector3(0.4f, 1.5f, -16.8f);

        static Vector3 At(float x, float y = 0.3f) => new Vector3(x, y, FD.StumpDiameter * 0.5f + R);

        [Test]
        public void ImpactPointPicksTheStruckStumps()
        {
            float off = WicketImpact.StumpX(0), between = off * 0.5f;
            var middle = WicketImpact.Compute(At(0f), Yorker140, R, 1);
            Assert.IsTrue(middle.Stumps[1].Struck);
            Assert.IsFalse(middle.Stumps[0].Struck || middle.Stumps[2].Struck, "middle only");
            Assert.AreEqual(1f, middle.Stumps[1].Squareness, 1e-5f);

            var plusX = WicketImpact.Compute(At(off), Yorker140, R, 1);
            Assert.IsTrue(plusX.Stumps[0].Struck && !plusX.Stumps[1].Struck && !plusX.Stumps[2].Struck, "+X stump only");

            var both = WicketImpact.Compute(At(-between), Yorker140, R, 1);
            Assert.IsTrue(both.Stumps[1].Struck && both.Stumps[2].Struck && !both.Stumps[0].Struck, "between middle and −X: both");
            Assert.AreEqual(both.Stumps[1].Squareness, both.Stumps[2].Squareness, 1e-4f, "halfway: equally square");

            // A graze on the outside of the +X stump: it barely moves.
            var graze = WicketImpact.Compute(At(off + WicketImpact.Reach(R) - 0.003f), Yorker140, R, 1);
            Assert.IsTrue(graze.Stumps[0].Struck);
            Assert.Less(graze.Stumps[0].Degrees, 5f, "barely moves");
            Assert.IsTrue(graze.Bails[0].Dislodged, "but the bail on it comes off");
            Assert.IsFalse(graze.Bails[1].Dislodged, "the other bail stays");
        }

        [Test]
        public void StumpNamesFollowTheBatter()
        {
            Assert.AreEqual(StumpName.Off, WicketImpact.NameOf(0, Handedness.Right), "+X is off for a right-hander");
            Assert.AreEqual(StumpName.Leg, WicketImpact.NameOf(2, Handedness.Right));
            Assert.AreEqual(StumpName.Leg, WicketImpact.NameOf(0, Handedness.Left), "+X is leg for a left-hander");
            Assert.AreEqual(StumpName.Off, WicketImpact.NameOf(2, Handedness.Left));
            Assert.AreEqual(StumpName.Middle, WicketImpact.NameOf(1, Handedness.Left));
            // The mirrored impact gives the mirrored reaction: same stump by the batter's name, mirrored bails.
            var right = WicketImpact.Compute(At(0.09f), Yorker140, R, 5);
            var left = WicketImpact.Compute(At(-0.09f), Vector3.Scale(Yorker140, new Vector3(-1f, 1f, 1f)), R, 5);
            Assert.AreEqual(right.Stumps[0].Degrees, left.Stumps[2].Degrees, 1e-4f, "off stump knocked back the same for either hand");
            Assert.AreEqual(right.Bails[0].Dislodged, left.Bails[1].Dislodged);
        }

        [Test]
        public void KnockBackAndBailSpeedGrowWithSpeedAndSquareness()
        {
            var fast = WicketImpact.Compute(At(0f), Yorker140, R, 2);
            var slow = WicketImpact.Compute(At(0f), OffBreak80, R, 2);
            Assert.AreEqual(WicketImpact.MaxKnockDegrees, fast.Stumps[1].Degrees, 0.5f, "140 km/h square: about 35 degrees");
            Assert.That(slow.Stumps[1].Degrees, Is.InRange(18f, 22f), "80 km/h square: about 20 degrees");
            Assert.Greater(fast.Bails[0].Velocity.magnitude, slow.Bails[0].Velocity.magnitude * 1.5f, "bails fly faster off a fast ball");
            var square = WicketImpact.Compute(At(WicketImpact.StumpX(0)), Yorker140, R, 2);
            var thin = WicketImpact.Compute(At(WicketImpact.StumpX(0) + 0.04f), Yorker140, R, 2);
            Assert.Greater(square.Stumps[0].Degrees, thin.Stumps[0].Degrees * 2f, "square hits knock back more");
            for (int i = 0; i < 3; i++)
                Assert.LessOrEqual(fast.Stumps[i].Degrees, WicketImpact.MaxKnockDegrees + 1e-4f, "never more than 35 degrees");
            Assert.Greater(fast.Loudness, slow.Loudness);
            Assert.Less(fast.Stumps[1].Direction.z, -0.99f, "knocked back the way the ball travels");
            Assert.Greater(Vector3.Dot(fast.Bails[0].Velocity, Vector3.up), 0f, "bails pop up");
            Assert.Less(fast.Bails[0].Velocity.z, 0f, "and away behind the stumps");
            Assert.Less(fast.BallVelocityAfter.magnitude, Yorker140.magnitude * 0.4f, "the ball carries on, slowed");
            Assert.Greater(fast.BallVelocityAfter.magnitude, Yorker140.magnitude * 0.3f);
            Assert.Less(fast.BallVelocityAfter.z, 0f, "past the stumps");
        }

        [Test]
        public void SameSeedSameReaction()
        {
            var a = WicketImpact.Compute(At(0.03f, 0.6f), OffBreak80, R, 77);
            var b = WicketImpact.Compute(At(0.03f, 0.6f), OffBreak80, R, 77);
            var c = WicketImpact.Compute(At(0.03f, 0.6f), OffBreak80, R, 78);
            for (int i = 0; i < 2; i++)
            {
                Assert.AreEqual(a.Bails[i].Velocity, b.Bails[i].Velocity);
                Assert.AreEqual(a.Bails[i].AngularVelocity, b.Bails[i].AngularVelocity);
            }
            for (int i = 0; i < 3; i++)
                Assert.AreEqual(a.Stumps[i].Degrees, b.Stumps[i].Degrees);
            Assert.AreEqual(a.BallVelocityAfter, b.BallVelocityAfter);
            Assert.AreNotEqual(a.Bails[0].Velocity, c.Bails[0].Velocity, "another seed varies the bails");
            Assert.AreEqual(a.Stumps[1].Degrees, c.Stumps[1].Degrees, "but not the stumps (they follow the impact only)");
        }

        [Test]
        public void BallCentreIsTakenAtTheStumpsFace()
        {
            var p = new Vector3(0.05f, 0.4f, 0.3f);
            var v = new Vector3(1f, -2f, -30f);
            var c = WicketImpact.BallCentreAtStumps(p, v, 0.02f, R);
            Assert.AreEqual(FD.StumpDiameter * 0.5f + R, c.z, 1e-3f, "front face plus a radius");
            Assert.AreEqual(p.x + v.x * (p.z - c.z) / 30f, c.x, 1e-5f);
            var inside = WicketImpact.BallCentreAtStumps(new Vector3(0f, 0.4f, 0.04f), v, 1f / 144f, R);
            Assert.AreEqual(0.04f, inside.z, 1e-6f, "already at the face: no extrapolation backwards");
        }
    }
}
