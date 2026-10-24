using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using FD = CricketVR.FieldDimensions;

namespace CricketVR.Tests
{
    /// <summary>
    /// M6.5 ground geometry (REQ-018, REQ-022, T033): the shipped GroundDefinition and FieldConfig assets and the
    /// pure geometry in <see cref="GroundGeometry"/>.
    /// </summary>
    public class GroundGeometryTests
    {
        const string GroundPath = "Assets/CricketVR/Settings/GroundDefinition.asset";
        const string FieldPath = "Assets/CricketVR/Settings/FieldConfig.asset";

        static GroundDefinition Ground()
        {
            var g = AssetDatabase.LoadAssetAtPath<GroundDefinition>(GroundPath);
            Assert.IsNotNull(g, "GroundDefinition asset missing; run CricketVR > Setup > 6. Build or Repair Main Scene");
            return g;
        }

        static FieldConfig Field()
        {
            var f = AssetDatabase.LoadAssetAtPath<FieldConfig>(FieldPath);
            Assert.IsNotNull(f, "FieldConfig asset missing; run the scene builder");
            return f;
        }

        [Test]
        public void PitchCreasesAndStumpsMatchTheLawsConstants()
        {
            var g = Ground();
            Assert.AreEqual(20.12f, g.pitchLength, 1e-4f, "22 yards stump to stump");
            Assert.AreEqual(FD.PitchLength, g.pitchLength, 1e-5f);
            Assert.AreEqual(FD.PitchWidth, g.pitchWidth, 1e-5f);
            Assert.AreEqual(FD.PitchStartZ, g.pitchStartZ, 1e-5f, "strip matches the unchanged pitch collider (GD-5)");
            Assert.AreEqual(FD.PitchEndZ, g.pitchEndZ, 1e-5f);
            Assert.AreEqual(1.22f, g.poppingCreaseOffset, 1e-4f);
            Assert.AreEqual(FD.PoppingCreaseOffset, g.poppingCreaseOffset, 1e-5f);
            Assert.AreEqual(FD.BowlingCreaseHalfLength * 2f, 2.64f, 1e-4f, "bowling crease 2.64 m long");
            Assert.AreEqual(FD.BowlingCreaseHalfLength, g.bowlingCreaseHalfLength, 1e-5f);
            Assert.AreEqual(FD.ReturnCreaseX, g.returnCreaseX, 1e-5f);
            Assert.AreEqual(FD.StumpHeight, g.stumpHeight, 1e-5f);
            Assert.AreEqual(FD.StumpSetWidth, g.stumpSetWidth, 1e-5f);
            Assert.GreaterOrEqual(g.poppingCreaseHalfLength, 1.83f, "popping crease at least 1.83 m either side (GD-4)");
            Assert.GreaterOrEqual(g.returnCreaseBehindPopping, 2.44f);
            Assert.AreEqual(new Vector3(0f, 0f, 10.06f), g.GroundCentre);
        }

        [Test]
        public void PoppingCreasesSitInFrontOfEachSetOfStumps()
        {
            var g = Ground();
            Assert.AreEqual(1.22f, 0f + g.poppingCreaseOffset, 1e-4f, "striker's end");
            Assert.AreEqual(18.90f, g.pitchLength - g.poppingCreaseOffset, 1e-4f, "bowler's end");
            Assert.Less(g.pitchStartZ, -(g.returnCreaseBehindPopping - g.poppingCreaseOffset) + 1e-4f, "return creases fit on the strip");
            Assert.Greater(g.pitchEndZ, g.pitchLength + g.returnCreaseBehindPopping - g.poppingCreaseOffset - 1e-4f);
        }

        [Test]
        public void FieldingCircleIsTwoSemicirclesJoinedByStraightLines()
        {
            var points = new List<Vector3>();
            const float r = 27.43f, far = 20.12f;
            GroundGeometry.FieldingCircle(0f, far, r, 5f, points);
            float perimeter = GroundGeometry.FieldingCirclePerimeter(0f, far, r);
            Assert.AreEqual(2f * far + 2f * Mathf.PI * r, perimeter, 1e-3f);
            Assert.AreEqual(Mathf.RoundToInt(perimeter / 5f), points.Count);

            int onLines = 0, nearArc = 0, farArc = 0;
            foreach (var p in points)
            {
                if (p.z >= 0f && p.z <= far && Mathf.Abs(Mathf.Abs(p.x) - r) < 1e-3f) onLines++;
                else if (p.z < 0f) { Assert.AreEqual(r, new Vector2(p.x, p.z).magnitude, 1e-3f, $"{p} on the striker's semicircle"); nearArc++; }
                else if (p.z > far) { Assert.AreEqual(r, new Vector2(p.x, p.z - far).magnitude, 1e-3f, $"{p} on the bowler's semicircle"); farArc++; }
                else Assert.Fail($"{p} is inside the circle, not on it");
                Assert.AreEqual(0f, p.y);
            }
            Assert.Greater(onLines, 4);
            Assert.Greater(nearArc, 10);
            Assert.Greater(farArc, 10);
            for (int i = 0; i < points.Count; i++)
                Assert.AreEqual(perimeter / points.Count, Vector3.Distance(points[i], points[(i + 1) % points.Count]), 0.1f, "even ~5 m spacing");
        }

        [Test]
        public void FielderPolarPositionsConvertToWorld()
        {
            AssertNear(new Vector3(0f, 0f, 10f), GroundGeometry.FielderPosition(0f, 10f, false), "0 deg = straight down the ground (+Z)");
            AssertNear(new Vector3(10f, 0f, 0f), GroundGeometry.FielderPosition(90f, 10f, false), "+90 = off side (+X) for a right-hander");
            AssertNear(new Vector3(-10f, 0f, 0f), GroundGeometry.FielderPosition(-90f, 10f, false));
            AssertNear(new Vector3(0f, 0f, -12f), GroundGeometry.FielderPosition(180f, 12f, false), "keeper behind the stumps");
            var cover = GroundGeometry.FielderPosition(55f, 28f, false);
            AssertNear(new Vector3(-cover.x, 0f, cover.z), GroundGeometry.FielderPosition(55f, 28f, true), "left-hander mirrors across the pitch");
            Assert.AreEqual(28f, cover.magnitude, 1e-4f);
        }

        [Test]
        public void EveryFielderIsAtLeastThreeMetresInsideTheRopeAtItsOwnAngle()
        {
            var g = Ground();
            var field = Field();
            Assert.IsNotEmpty(field.fielders);
            foreach (var f in field.fielders)
                foreach (bool left in new[] { false, true })
                {
                    var p = GroundGeometry.FielderPosition(f.angleDegrees, f.radius, left);
                    float inside = GroundGeometry.DistanceInsideBoundary(p, g.GroundCentre, g.BoundaryRadii);
                    Assert.GreaterOrEqual(inside, FieldConfig.MinInsideRope,
                        $"{f.name} ({f.angleDegrees} deg, {f.radius} m, {(left ? "left" : "right")}-hander) is {inside:F1} m inside the rope");
                }
        }

        [Test]
        public void BoundaryIsACircleOrAnEllipseAboutTheGroundCentre()
        {
            var circle = new Vector2(65f, 65f);
            for (int deg = 0; deg < 360; deg += 30)
                Assert.AreEqual(65f, GroundGeometry.BoundaryRadiusAtAngle(circle, deg * Mathf.Deg2Rad), 1e-3f);
            var ellipse = new Vector2(60f, 70f);
            Assert.AreEqual(70f, GroundGeometry.BoundaryRadiusAtAngle(ellipse, 0f), 1e-3f, "straight (+Z) uses the Z radius");
            Assert.AreEqual(60f, GroundGeometry.BoundaryRadiusAtAngle(ellipse, Mathf.PI * 0.5f), 1e-3f, "square (+X) uses the X radius");

            var centre = new Vector3(0f, 0f, 10.06f);
            Assert.AreEqual(5f, GroundGeometry.DistanceInsideBoundary(centre + new Vector3(0f, 3f, 60f), centre, circle), 1e-3f, "height is ignored");
            Assert.Less(GroundGeometry.DistanceInsideBoundary(centre + new Vector3(46f, 0f, 46f), centre, circle), 0f, "beyond the rope");
            Assert.AreEqual(65f - 10.06f, GroundGeometry.DistanceInsideBoundary(Vector3.zero, centre, circle), 1e-3f, "the striker's stumps are 10.06 m from the centre");
        }

        [Test]
        public void ValidationRejectsOutOfRangeBoundariesAndFieldersOutsideTheRope()
        {
            var g = ScriptableObject.CreateInstance<GroundDefinition>();
            var field = ScriptableObject.CreateInstance<FieldConfig>();
            try
            {
                CollectionAssert.IsEmpty(g.Validate(field), "defaults are valid");
                g.boundaryRadiusX = 80f;
                Assert.IsNotEmpty(g.Validate());
                g.boundaryRadiusX = 50f;
                Assert.IsNotEmpty(g.Validate());
                g.boundaryRadiusX = 65f;
                field.fielders = new[] { new FielderSpot("Third man (brief)", 145f, 58f) }; // GD-3: outside a 65 m rope
                Assert.IsNotEmpty(g.Validate(field));
            }
            finally
            {
                Object.DestroyImmediate(g);
                Object.DestroyImmediate(field);
            }
        }

        static void AssertNear(Vector3 expected, Vector3 actual, string message = null) =>
            Assert.Less(Vector3.Distance(expected, actual), 1e-3f, $"{message} expected {expected}, got {actual}");
    }

    /// <summary>GroundMode terminal rules (REQ-021, GD-8, T044).</summary>
    public class GroundModeAdjudicatorTests
    {
        DeliveryAdjudicator _a;

        [SetUp]
        public void SetUp()
        {
            _a = new DeliveryAdjudicator(ScoringRules.Default, DeliveryRules.Ground);
            _a.Begin(3, default);
        }

        void Hit() => _a.RecordBatContact(3, new ShotData { ContactPoint = new Vector3(0f, 0.6f, 1.2f), ExitVelocity = new Vector3(2f, 1f, -20f) });

        DeliveryResult Step(params BallEvent[] events)
        {
            Assert.IsTrue(_a.ProcessStep(3, new List<BallEvent>(events), out var r));
            return r;
        }

        [Test]
        public void UnhitBallPastTheStumpsIsAMissWhateverItsWidth()
        {
            var r = Step(BallEvent.PassedStumps(new Vector3(1.6f, 0.4f, -1.05f)));
            Assert.AreEqual(DeliveryOutcome.Miss, r.Outcome);
            Assert.AreEqual(DeliveryEnd.PassedStumps, r.End);
            Assert.IsFalse(r.BatContact);
        }

        [Test]
        public void HitBallBehindTheWicketIsNeverAMiss()
        {
            Hit();
            Assert.IsFalse(_a.ProcessStep(3, new List<BallEvent> { BallEvent.PassedStumps(new Vector3(0.4f, 0.8f, -1.1f)) }, out _),
                "an edge going behind the wicket does not end the delivery");
            var r = Step(BallEvent.LeftGround(new Vector3(20f, 1f, -50f)));
            Assert.AreEqual(DeliveryOutcome.ScoringPending, r.Outcome);
            Assert.AreEqual(0, r.Runs);
            Assert.AreEqual("Hit, practice scoring pending", r.Summary);
            Assert.IsFalse(r.Miss);
        }

        [Test]
        public void SettleOrTimeoutIsPendingWhenHitAndAMissWhenNot()
        {
            Hit();
            Assert.IsTrue(_a.EndWithoutTerminal(3, DeliveryEnd.Settled, out var hit));
            Assert.AreEqual(DeliveryOutcome.ScoringPending, hit.Outcome);
            _a.Begin(4, default);
            Assert.IsTrue(_a.EndWithoutTerminal(4, DeliveryEnd.Timeout, out var unhit));
            Assert.AreEqual(DeliveryOutcome.Miss, unhit.Outcome, "an unhit ball that stops before the stumps is a miss");
        }

        [Test]
        public void StumpsBeatEverythingInTheSameStep()
        {
            var r = Step(BallEvent.LeftGround(Vector3.zero), BallEvent.PassedStumps(Vector3.zero), BallEvent.Stumps(Vector3.zero));
            Assert.AreEqual(DeliveryOutcome.Bowled, r.Outcome);
            _a.Begin(4, default);
            _a.RecordBatContact(4, default);
            Assert.IsTrue(_a.ProcessStep(4, new List<BallEvent> { BallEvent.Stumps(Vector3.zero) }, out var playedOn));
            Assert.AreEqual(DeliveryOutcome.Bowled, playedOn.Outcome);
            Assert.IsTrue(playedOn.BatContact && playedOn.Bowled);
        }

        [Test]
        public void NetEventsDoNothingInGroundMode()
        {
            Hit();
            Assert.IsFalse(_a.ProcessStep(3, new List<BallEvent> { BallEvent.Net(WallSide.Front, new Vector3(0f, 2f, 24f)), BallEvent.Exit(WallSide.Front, new Vector3(0f, 7f, 25f)) }, out _));
            Assert.IsTrue(_a.IsOpen);
        }

        [Test]
        public void GroundDeliveryFinalizesExactlyOnce()
        {
            Step(BallEvent.PassedStumps(new Vector3(0.3f, 0.5f, -1.2f)));
            Assert.IsFalse(_a.ProcessStep(3, new List<BallEvent> { BallEvent.LeftGround(Vector3.zero) }, out _));
            Assert.IsFalse(_a.EndWithoutTerminal(3, DeliveryEnd.Settled, out _));
        }

        [Test]
        public void CarryIsMeasuredOnTheOutfieldToo()
        {
            Hit();
            _a.ProcessStep(3, new List<BallEvent> { BallEvent.Ground(new Vector3(8f, 0f, 7.2f)) }, out _); // (8, 6) from the bat point
            var r = Step(BallEvent.LeftGround(new Vector3(40f, 0.2f, 60f)));
            Assert.AreEqual(CarryState.Measured, r.Carry);
            Assert.AreEqual(10f, r.CarryMetres, 1e-4f);
        }

        [Test]
        public void TotalsCountPendingHits()
        {
            Hit();
            var results = new List<DeliveryResult> { Step(BallEvent.LeftGround(Vector3.zero)) };
            _a.Begin(4, default);
            _a.ProcessStep(4, new List<BallEvent> { BallEvent.PassedStumps(Vector3.zero) }, out var miss);
            results.Add(miss);
            var t = SessionTotals.From(results);
            Assert.AreEqual(1, t.ScoringPending);
            Assert.AreEqual(1, t.Misses);
            Assert.AreEqual(1, t.BatContacts);
            StringAssert.Contains("pending", t.ToString());
        }
    }
}
