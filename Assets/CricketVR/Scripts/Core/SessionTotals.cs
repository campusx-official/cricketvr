using System.Collections.Generic;

namespace CricketVR
{
    /// <summary>
    /// Counts derived from finalized delivery results only (never accumulated separately). No runs: Net Practice
    /// scores none (REQ-009 amended 2026-10-26); Match Simulation runs come from the virtual-field scorer (M9/M10).
    /// </summary>
    public readonly struct SessionTotals
    {
        public readonly int BallsFaced, BatContacts, Misses, Bowled;
        /// <summary>GroundMode hits whose runs await the virtual-field scorer.</summary>
        public readonly int ScoringPending;

        SessionTotals(int balls, int contacts, int misses, int bowled, int pending)
        {
            BallsFaced = balls;
            BatContacts = contacts;
            Misses = misses;
            Bowled = bowled;
            ScoringPending = pending;
        }

        public static SessionTotals From(IReadOnlyList<DeliveryResult> results)
        {
            int contacts = 0, misses = 0, bowled = 0, pending = 0;
            foreach (var r in results)
            {
                if (r.BatContact) contacts++;
                if (r.Miss) misses++;
                if (r.Bowled) bowled++;
                if (r.Outcome == DeliveryOutcome.ScoringPending) pending++;
            }
            return new SessionTotals(results.Count, contacts, misses, bowled, pending);
        }

        public static SessionTotals From(IReadOnlyList<BallRecord> records)
        {
            var results = new List<DeliveryResult>(records.Count);
            foreach (var r in records) results.Add(r.Result);
            return From(results);
        }

        public override string ToString() =>
            $"Balls {BallsFaced} | contacts {BatContacts} | misses {Misses} | bowled {Bowled}" +
            (ScoringPending > 0 ? $" | {ScoringPending} hit(s) scoring pending" : "");
    }
}
