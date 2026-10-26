using System.Collections.Generic;

namespace CricketVR
{
    /// <summary>Totals derived from finalized delivery records only (never accumulated separately).</summary>
    public readonly struct SessionTotals
    {
        public readonly int BallsFaced, PracticeRuns, BatContacts, Misses, Bowled;
        /// <summary>GroundMode hits whose runs await the virtual-field scorer.</summary>
        public readonly int ScoringPending;

        SessionTotals(int balls, int runs, int contacts, int misses, int bowled, int pending)
        {
            ScoringPending = pending;
            BallsFaced = balls;
            PracticeRuns = runs;
            BatContacts = contacts;
            Misses = misses;
            Bowled = bowled;
        }

        public static SessionTotals From(IReadOnlyList<DeliveryResult> results)
        {
            int runs = 0, contacts = 0, misses = 0, bowled = 0, pending = 0;
            foreach (var r in results)
            {
                runs += r.Runs;
                if (r.BatContact) contacts++;
                if (r.Miss) misses++;
                if (r.Bowled) bowled++;
                if (r.Outcome == DeliveryOutcome.ScoringPending) pending++;
            }
            return new SessionTotals(results.Count, runs, contacts, misses, bowled, pending);
        }

        public override string ToString() =>
            $"Balls {BallsFaced} | practice runs {(ScoringPending > 0 ? $"{PracticeRuns} + {ScoringPending} pending" : PracticeRuns.ToString())} | " +
            $"contacts {BatContacts} | misses {Misses} | bowled {Bowled}";
    }
}
