using UnityEngine;

namespace CricketVR
{
    /// <summary>Where a delivery pitched, and where PhysX actually started its rebound.</summary>
    public struct PitchMeasurement
    {
        /// <summary>Ball centre where the incoming flight reaches contact height (the pitch point; T070).</summary>
        public Vector3 Point;
        /// <summary>Ball centre at its lowest sample, where PhysX's rebound starts.</summary>
        public Vector3 ReboundStart;
        /// <summary>Signed horizontal distance (m) from <see cref="Point"/> to <see cref="ReboundStart"/> along the
        /// incoming direction: + = the rebound starts past the pitch point (ball sank in), − = before it (speculative
        /// contact turned the ball while it was still above the pitch).</summary>
        public float ReboundOffset;
        /// <summary>Physics steps (fractional) from the pitch point to the lowest sample; negative when the pitch
        /// point is extrapolated beyond it.</summary>
        public float StepsBeforeLowest;
    }

    /// <summary>
    /// Where a ball pitched, measured from its own stepped path (T070, plan.md §19). PhysX resolves the pitch contact
    /// at a point that depends on where the ball is at the start of the contact step: a ball within the contact
    /// offset is turned speculatively while still up to ~2 cm above the pitch (before the pitch point); otherwise it
    /// ends the step 1–2.5 cm inside the pitch and turns from there (past it). The contact report follows the same
    /// scatter. The pitch point, where the incoming flight reaches contact height, does not: within a step PhysX
    /// moves the ball in a straight line, so interpolating (or extrapolating a fraction of a step) is exact to
    /// well under a millimetre.
    /// </summary>
    public static class BounceMeasurement
    {
        /// <param name="samples">Ball centre after each physics step while descending, oldest first; the last one is
        /// the lowest (the step before the vertical velocity turned upward).</param>
        /// <param name="count">Number of valid samples (at least 1).</param>
        /// <param name="contactHeight">Surface height plus the ball radius.</param>
        public static PitchMeasurement Measure(Vector3[] samples, int count, float contactHeight)
        {
            int last = count - 1;
            var lowest = samples[last];
            var m = new PitchMeasurement { Point = lowest, ReboundStart = lowest };

            if (lowest.y >= contactHeight)
            {
                // Turned above the pitch: extend the last step's straight line down to contact height.
                if (count >= 2 && samples[last - 1].y > lowest.y)
                {
                    var a = samples[last - 1];
                    float f = (a.y - contactHeight) / (a.y - lowest.y); // >= 1
                    m.Point = Vector3.LerpUnclamped(a, lowest, f);
                    m.StepsBeforeLowest = 1f - f;
                }
            }
            else
            {
                m.StepsBeforeLowest = last;
                m.Point = samples[0]; // below contact height for the whole history: the oldest sample is the best available
                for (int i = last; i > 0; i--)
                {
                    var a = samples[i - 1];
                    if (a.y >= contactHeight)
                    {
                        float f = (a.y - contactHeight) / (a.y - samples[i].y);
                        m.Point = Vector3.LerpUnclamped(a, samples[i], f);
                        m.StepsBeforeLowest = last - (i - 1) - f;
                        break;
                    }
                }
            }

            var travel = count >= 2 ? lowest - samples[0] : Vector3.zero;
            travel.y = 0f;
            var along = travel.sqrMagnitude > 1e-12f ? travel.normalized : Vector3.zero;
            var offset = lowest - m.Point;
            offset.y = 0f;
            m.ReboundOffset = Vector3.Dot(offset, along);
            return m;
        }
    }
}
