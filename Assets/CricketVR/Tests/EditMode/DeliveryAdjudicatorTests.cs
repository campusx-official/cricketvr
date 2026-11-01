using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace CricketVR.Tests
{
    /// <summary>AC-06 rules (REQ-008, REQ-009, REQ-010): zones, terminal priority, finalize-once, stale events, carry.</summary>
    public class DeliveryAdjudicatorTests
    {
        static readonly ScoringRules Rules = ScoringRules.Default;
        static readonly Vector3 BatPoint = new Vector3(0f, 0.6f, 1.2f);

        DeliveryAdjudicator _a;

        [SetUp]
        public void SetUp()
        {
            _a = new DeliveryAdjudicator(Rules);
            _a.Begin(7, default);
        }

        static List<BallEvent> Step(params BallEvent[] events) => new List<BallEvent>(events);

        void Hit(int id = 7) => _a.RecordBatContact(id, new ShotData { ContactPoint = BatPoint, ExitVelocity = new Vector3(0f, 5f, 25f) });

        DeliveryResult Finalize(params BallEvent[] events)
        {
            Assert.IsTrue(_a.ProcessStep(7, Step(events), out var r), "expected a terminal event");
            return r;
        }

        // ---------- Zones ----------

        [TestCase(0.0f, ScoringZone.FrontLow)]
        [TestCase(0.999f, ScoringZone.FrontLow)]
        [TestCase(1.0f, ScoringZone.FrontMiddle)]
        [TestCase(2.999f, ScoringZone.FrontMiddle)]
        [TestCase(3.0f, ScoringZone.FrontUpper)]
        [TestCase(5.9f, ScoringZone.FrontUpper)]
        public void FrontNetZoneBoundsAreHalfOpen(float height, ScoringZone expected)
        {
            Assert.AreEqual(expected, PracticeScoring.NetZone(WallSide.Front, height, Rules));
        }

        /// <summary>Net zones only label where a hit went (REQ-009 amended 2026-10-26: no runs).</summary>
        [Test]
        public void SideAndRearNetsHaveTheirOwnZones()
        {
            Assert.AreEqual(ScoringZone.SideNet, PracticeScoring.NetZone(WallSide.Left, 3.5f, Rules));
            Assert.AreEqual(ScoringZone.SideNet, PracticeScoring.NetZone(WallSide.Right, 0.2f, Rules));
            Assert.AreEqual(ScoringZone.RearNet, PracticeScoring.NetZone(WallSide.Rear, 2f, Rules));
        }

        [Test]
        public void ExitSideNeedsTheMarginAndPrefersFrontAtCorners()
        {
            Assert.IsNull(PracticeScoring.ExitSide(new Vector3(0f, 5f, 24.2f), 0.3f), "inside the margin");
            Assert.AreEqual(WallSide.Front, PracticeScoring.ExitSide(new Vector3(0f, 7f, 24.4f), 0.3f));
            Assert.AreEqual(WallSide.Right, PracticeScoring.ExitSide(new Vector3(2.2f, 4.5f, 10f), 0.3f));
            Assert.AreEqual(WallSide.Left, PracticeScoring.ExitSide(new Vector3(-2.2f, 4.5f, 10f), 0.3f));
            Assert.AreEqual(WallSide.Rear, PracticeScoring.ExitSide(new Vector3(0f, 4.5f, -3.4f), 0.3f));
            Assert.AreEqual(WallSide.Front, PracticeScoring.ExitSide(new Vector3(2.5f, 7f, 24.5f), 0.3f), "corner");
        }

        // ---------- Outcomes ----------

        [Test]
        public void HitIntoTheFrontNetIsAHitLabelledWithItsZone()
        {
            Hit();
            var r = Finalize(BallEvent.Net(WallSide.Front, new Vector3(0f, 2f, 24f)));
            Assert.AreEqual(DeliveryOutcome.Hit, r.Outcome);
            Assert.AreEqual(ScoringZone.FrontMiddle, r.Zone);
            Assert.AreEqual("Hit, front net, middle", r.Summary);
            Assert.IsTrue(r.BatContact);
            Assert.IsFalse(r.Bowled);
            Assert.IsFalse(r.Miss);
        }

        [Test]
        public void UnhitBallReachingAnyZoneIsAMiss()
        {
            foreach (var e in new[]
                     {
                         BallEvent.Net(WallSide.Front, new Vector3(0f, 4f, 24f)), BallEvent.Net(WallSide.Rear, new Vector3(0f, 1f, -3f)),
                         BallEvent.Net(WallSide.Left, new Vector3(-1.8f, 1f, 5f)), BallEvent.Exit(WallSide.Front, new Vector3(0f, 7f, 24.5f)),
                     })
            {
                _a.Begin(7, default);
                var r = Finalize(e);
                Assert.AreEqual(DeliveryOutcome.Miss, r.Outcome, e.Kind + " " + e.Wall);
                Assert.AreEqual(CarryState.NotHit, r.Carry);
            }
        }

        [Test]
        public void BowledWithoutContactIsNotAMiss()
        {
            var r = Finalize(BallEvent.Stumps(new Vector3(0f, 0.3f, 0f)));
            Assert.AreEqual(DeliveryOutcome.Bowled, r.Outcome);
            Assert.IsTrue(r.Bowled);
            Assert.IsFalse(r.BatContact);
            Assert.IsFalse(r.Miss);
        }

        [Test]
        public void ContactThenBowledHasBothFlags()
        {
            Hit();
            _a.ProcessStep(7, Step(BallEvent.Ground(new Vector3(0f, 0f, 0.8f))), out _);
            var r = Finalize(BallEvent.Stumps(new Vector3(0f, 0.2f, 0f)));
            Assert.AreEqual(DeliveryOutcome.Bowled, r.Outcome);
            Assert.IsTrue(r.BatContact && r.Bowled);
            Assert.IsFalse(r.Miss);
        }

        [Test]
        public void ClearingTheFrontNetIsLabelledAndCarryIsNotClaimed()
        {
            Hit();
            var r = Finalize(BallEvent.Exit(WallSide.Front, new Vector3(0f, 6.5f, 24.4f)));
            Assert.AreEqual(DeliveryOutcome.Hit, r.Outcome);
            Assert.AreEqual(ScoringZone.OverFront, r.Zone);
            Assert.AreEqual(DeliveryEnd.LeftEnclosure, r.End);
            Assert.AreEqual(CarryState.NoLanding, r.Carry);
            Assert.AreEqual("Carry: N/A", r.CarryLabel);
        }

        [Test]
        public void ClearingASideOrRearNetIsLabelled()
        {
            Hit();
            Assert.AreEqual(ScoringZone.OverSide, Finalize(BallEvent.Exit(WallSide.Left, new Vector3(-2.2f, 4.2f, 6f))).Zone);
            _a.Begin(8, default);
            _a.RecordBatContact(8, new ShotData { ContactPoint = BatPoint });
            Assert.IsTrue(_a.ProcessStep(8, Step(BallEvent.Exit(WallSide.Rear, new Vector3(0f, 4.2f, -3.4f))), out var r));
            Assert.AreEqual(ScoringZone.OverRear, r.Zone);
        }

        // ---------- Competing signals and finalize-once ----------

        [Test]
        public void StumpsBeatANetInTheSameStep()
        {
            Hit();
            var r = Finalize(BallEvent.Net(WallSide.Front, new Vector3(0f, 4f, 24f)), BallEvent.Stumps(Vector3.zero));
            Assert.AreEqual(DeliveryOutcome.Bowled, r.Outcome);
        }

        [Test]
        public void SameStepZonesResolveByPriority()
        {
            Hit();
            var r = Finalize(BallEvent.Net(WallSide.Left, new Vector3(-1.8f, 0.5f, 23.9f)), BallEvent.Net(WallSide.Front, new Vector3(-1.7f, 0.5f, 24f)));
            Assert.AreEqual(ScoringZone.FrontLow, r.Zone, "front low outranks a side net");
            _a.Begin(8, default);
            _a.RecordBatContact(8, new ShotData { ContactPoint = BatPoint });
            _a.ProcessStep(8, Step(BallEvent.Exit(WallSide.Front, new Vector3(0f, 6.4f, 24.4f)), BallEvent.Net(WallSide.Right, new Vector3(1.8f, 3.9f, 23f))), out r);
            Assert.AreEqual(ScoringZone.SideNet, r.Zone, "a physical net contact outranks leaving the enclosure");
        }

        [Test]
        public void DeliveryFinalizesExactlyOnce()
        {
            Hit();
            Finalize(BallEvent.Net(WallSide.Front, new Vector3(0f, 0.5f, 24f)));
            Assert.IsFalse(_a.ProcessStep(7, Step(BallEvent.Stumps(Vector3.zero)), out _), "a later stump strike can't re-decide it");
            Assert.IsFalse(_a.EndWithoutTerminal(7, DeliveryEnd.Timeout, out _));
            Assert.IsFalse(_a.RecordBatContact(7, default));
            Assert.IsFalse(_a.IsOpen);
        }

        [Test]
        public void StaleEventsAreIgnored()
        {
            Assert.IsFalse(_a.RecordBatContact(6, default), "contact for an old delivery");
            Assert.IsFalse(_a.ProcessStep(6, Step(BallEvent.Stumps(Vector3.zero)), out _), "stumps for an old delivery");
            Assert.IsTrue(_a.IsOpen);
            var r = Finalize(BallEvent.Net(WallSide.Rear, new Vector3(0f, 1f, -3f)));
            Assert.AreEqual(DeliveryOutcome.Miss, r.Outcome, "the stale contact did not count");
        }

        [Test]
        public void OnlyTheFirstBatContactIsRecorded()
        {
            Hit();
            Assert.IsFalse(_a.RecordBatContact(7, new ShotData { ContactPoint = Vector3.one * 9f }));
            _a.ProcessStep(7, Step(BallEvent.Ground(BatPoint + new Vector3(3f, -0.6f, 4f))), out _);
            Assert.AreEqual(5f, Finalize(BallEvent.Net(WallSide.Front, new Vector3(0f, 0.2f, 24f))).CarryMetres, 1e-4f, "carry from the first contact point");
        }

        [Test]
        public void CancelledDeliveryProducesNoResult()
        {
            Hit();
            _a.Cancel(7);
            Assert.IsFalse(_a.ProcessStep(7, Step(BallEvent.Net(WallSide.Front, new Vector3(0f, 2f, 24f))), out _));
            Assert.IsFalse(_a.EndWithoutTerminal(7, DeliveryEnd.Timeout, out _));
        }

        [Test]
        public void SettleOrTimeoutIsAHitWithNoZoneOrAMiss()
        {
            Hit();
            Assert.IsTrue(_a.EndWithoutTerminal(7, DeliveryEnd.Settled, out var hit));
            Assert.AreEqual(DeliveryOutcome.Hit, hit.Outcome);
            Assert.AreEqual(ScoringZone.None, hit.Zone);
            Assert.AreEqual("Hit", hit.Summary);
            _a.Begin(8, default);
            Assert.IsTrue(_a.EndWithoutTerminal(8, DeliveryEnd.Timeout, out var unhit));
            Assert.AreEqual(DeliveryOutcome.Miss, unhit.Outcome);
        }

        // ---------- Carry ----------

        [Test]
        public void CarryIsMeasuredFromContactToFirstLanding()
        {
            _a.ProcessStep(7, Step(BallEvent.Ground(new Vector3(0f, 0f, 6f))), out _); // the delivery's own bounce, before contact
            Hit();
            _a.ProcessStep(7, Step(BallEvent.Ground(new Vector3(6f, 0f, 9.2f))), out _);  // landing: (6, 8) from the bat point
            _a.ProcessStep(7, Step(BallEvent.Ground(new Vector3(7f, 0f, 14f))), out _);  // second bounce: ignored
            var r = Finalize(BallEvent.Net(WallSide.Front, new Vector3(0.5f, 0.1f, 24f)));
            Assert.AreEqual(CarryState.Measured, r.Carry);
            Assert.AreEqual(10f, r.CarryMetres, 1e-4f);
            Assert.AreEqual("Measured carry 10.0 m", r.CarryLabel);
        }

        [Test]
        public void NetBeforeOrWithTheLandingIsANetHitNotADistance()
        {
            Hit();
            var r = Finalize(BallEvent.Net(WallSide.Front, new Vector3(0f, 2f, 24f)));
            Assert.AreEqual(CarryState.NetFirst, r.Carry);
            Assert.AreEqual(0f, r.CarryMetres);
            Assert.AreEqual("Carry: net hit", r.CarryLabel);

            _a.Begin(8, default);
            _a.RecordBatContact(8, new ShotData { ContactPoint = BatPoint });
            _a.ProcessStep(8, Step(BallEvent.Ground(new Vector3(0f, 0f, 23.9f)), BallEvent.Net(WallSide.Front, new Vector3(0f, 0.04f, 24f))), out r);
            Assert.AreEqual(CarryState.NetFirst, r.Carry, "same-step landing and net: never claim the distance");
        }

        // ---------- Totals ----------

        [Test]
        public void TotalsAreDerivedFromRecords()
        {
            var results = new List<DeliveryResult>();
            Hit();
            results.Add(Finalize(BallEvent.Net(WallSide.Front, new Vector3(0f, 3.5f, 24f))));    // hit
            _a.Begin(8, default);
            _a.ProcessStep(8, Step(BallEvent.Stumps(Vector3.zero)), out var bowled);             // bowled
            results.Add(bowled);
            _a.Begin(9, default);
            _a.ProcessStep(9, Step(BallEvent.Net(WallSide.Rear, new Vector3(0f, 0.5f, -3f))), out var miss); // miss
            results.Add(miss);
            _a.Begin(10, default);
            _a.RecordBatContact(10, new ShotData { ContactPoint = BatPoint });
            _a.ProcessStep(10, Step(BallEvent.Stumps(Vector3.zero)), out var playedOn);         // contact + bowled
            results.Add(playedOn);

            var t = SessionTotals.From(results);
            Assert.AreEqual(4, t.BallsFaced);
            Assert.AreEqual(2, t.BatContacts);
            Assert.AreEqual(1, t.Misses);
            Assert.AreEqual(2, t.Bowled);
        }
    }
}
