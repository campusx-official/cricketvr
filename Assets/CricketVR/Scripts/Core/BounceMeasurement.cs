using UnityEngine;

namespace CricketVR
{
    /// <summary>
    /// Where a ball pitched, measured from its own stepped path (T070, plan.md §19).
    /// PhysX resolves the pitch contact a step late: the ball ends the contact step 1.6–2.5 cm inside the pitch, and
    /// the contact report sits under that sunk position, 6–10 cm past where the ball came down. The physical touch is
    /// where the incoming flight first reaches contact height (ball centre one radius above the surface). Within a
    /// step PhysX moves the ball in a straight line, so linear interpolation between two samples is exact.
    /// </summary>
    public static class BounceMeasurement
    {
        /// <param name="samples">Ball centre after each physics step while descending, oldest first; the last one is
        /// the lowest (the step before the vertical velocity turned upward).</param>
        /// <param name="count">Number of valid samples (at least 1).</param>
        /// <param name="contactHeight">Surface height plus the ball radius.</param>
        /// <param name="stepsBeforeLowest">How many physics steps (fractional) before the lowest sample the touch was.</param>
        public static Vector3 TouchPoint(Vector3[] samples, int count, float contactHeight, out float stepsBeforeLowest)
        {
            int last = count - 1;
            stepsBeforeLowest = 0f;
            if (samples[last].y >= contactHeight)
                return samples[last]; // turned back without sinking in: it touched at its lowest sample

            for (int i = last; i > 0; i--)
            {
                var a = samples[i - 1];
                var b = samples[i];
                if (a.y >= contactHeight)
                {
                    float f = (a.y - contactHeight) / (a.y - b.y);
                    stepsBeforeLowest = last - (i - 1) - f;
                    return Vector3.LerpUnclamped(a, b, f);
                }
            }
            // Below contact height for the whole history: the oldest sample is the best available.
            stepsBeforeLowest = last;
            return samples[0];
        }
    }
}
