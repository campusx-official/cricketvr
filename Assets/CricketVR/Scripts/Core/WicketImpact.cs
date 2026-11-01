using UnityEngine;

namespace CricketVR
{
    /// <summary>The batter's names for the three stumps (off is +X for a right-hander, −X for a left-hander).</summary>
    public enum StumpName { Off, Middle, Leg }

    /// <summary>How one stump is knocked back: tilted about its base, toward <see cref="Direction"/>.</summary>
    public struct StumpKnock
    {
        public bool Struck;
        /// <summary>1 = hit through its centre, 0 = only just touched.</summary>
        public float Squareness;
        public float Degrees;
        /// <summary>Horizontal unit vector the top of the stump moves toward (the ball's travel).</summary>
        public Vector3 Direction;
        /// <summary>Rotation axis through the base (world): up × direction.</summary>
        public Vector3 Axis => Vector3.Cross(Vector3.up, Direction);
    }

    /// <summary>How one bail leaves the stumps.</summary>
    public struct BailLaunch
    {
        public bool Dislodged;
        public Vector3 Velocity;
        public Vector3 AngularVelocity;
    }

    /// <summary>
    /// The whole reaction for one wicket (REQ-049). Stumps and bails are indexed by world position:
    /// stump 0 = +X, 1 = middle, 2 = −X; bail 0 = the +X bail (on stumps 0 and 1), 1 = the −X bail (on 1 and 2).
    /// </summary>
    public class WicketReactionPlan
    {
        public readonly StumpKnock[] Stumps = new StumpKnock[3];
        public readonly BailLaunch[] Bails = new BailLaunch[2];
        /// <summary>Clatter volume 0..1, from the impact speed.</summary>
        public float Loudness;
        /// <summary>The ball's velocity after the stumps (cosmetic carry-on).</summary>
        public Vector3 BallVelocityAfter;
    }

    /// <summary>
    /// Wicket reaction from the decided stump hit (plan.md §20): which stumps move and how far, how the bails fly,
    /// how loud. Pure and deterministic: the same inputs and seed give the same reaction. Positions are relative to
    /// the base of the middle stump (the stumps' origin).
    /// </summary>
    public static class WicketImpact
    {
        public const float MaxKnockDegrees = 35f;
        /// <summary>
        /// Speeds at the stumps (m/s) mapped to the low and high end of the knock-back. A ball loses pace off the pitch:
        /// measured at the stumps, an 80 km/h ball arrives at about 68 km/h and a 140 km/h one at about 117 km/h, so
        /// 17 m/s (61 km/h) is gentle and 32 m/s (115 km/h) is full.
        /// </summary>
        public const float GentleSpeed = 17f, FullSpeed = 32f;
        const float GentleShare = 0.55f;
        const float CarryOnShare = 0.35f, CarryOnMaxDegrees = 8f;

        /// <summary>Centre x of stump 0 (+X), 1 (middle) and 2 (−X), relative to the middle stump.</summary>
        public static float StumpX(int index) =>
            (1 - index) * (FieldDimensions.StumpSetWidth - FieldDimensions.StumpDiameter) * 0.5f;

        /// <summary>Distance between ball and stump centres at which they touch.</summary>
        public static float Reach(float ballRadius) => ballRadius + FieldDimensions.StumpDiameter * 0.5f;

        /// <summary>The batter's name for a stump index.</summary>
        public static StumpName NameOf(int index, Handedness hand)
        {
            if (index == 1) return StumpName.Middle;
            bool plusX = index == 0;
            return plusX == (hand != Handedness.Left) ? StumpName.Off : StumpName.Leg;
        }

        /// <summary>
        /// The ball centre where it reaches the stumps' face (the front face for a ball travelling −Z), from its
        /// state at the start of the physics step in which it touched them.
        /// </summary>
        public static Vector3 BallCentreAtStumps(Vector3 position, Vector3 velocity, float dt, float ballRadius)
        {
            float face = FieldDimensions.StumpDiameter * 0.5f + ballRadius;
            float plane = velocity.z <= 0f ? face : -face;
            float t = Mathf.Abs(velocity.z) > 1e-6f ? (plane - position.z) / velocity.z : 0f;
            return position + velocity * Mathf.Clamp(t, 0f, Mathf.Max(0f, dt));
        }

        /// <param name="centre">Ball centre at impact, relative to the middle stump's base.</param>
        /// <param name="velocity">The ball's velocity as it reached the stumps.</param>
        public static WicketReactionPlan Compute(Vector3 centre, Vector3 velocity, float ballRadius, int seed)
        {
            var plan = new WicketReactionPlan();
            var rng = new System.Random(seed);
            float R() => (float)rng.NextDouble();

            float speed = velocity.magnitude;
            var travel = new Vector3(velocity.x, 0f, velocity.z);
            travel = travel.sqrMagnitude > 1e-8f ? travel.normalized : Vector3.back;
            float reach = Reach(ballRadius);
            float speedShare = Mathf.Lerp(GentleShare, 1f, Mathf.InverseLerp(GentleSpeed, FullSpeed, speed));

            int nearest = 0;
            for (int i = 0; i < 3; i++)
            {
                float dx = Mathf.Abs(centre.x - StumpX(i));
                if (dx < Mathf.Abs(centre.x - StumpX(nearest))) nearest = i;
                if (dx >= reach) continue;
                float s = 1f - dx / reach;
                plan.Stumps[i] = new StumpKnock
                {
                    Struck = true, Squareness = s, Direction = travel,
                    Degrees = MaxKnockDegrees * Mathf.Pow(s, 0.7f) * speedShare,
                };
            }
            // The stumps box was hit, so something moved: at worst the nearest stump was only just touched.
            if (!plan.Stumps[nearest].Struck)
                plan.Stumps[nearest] = new StumpKnock { Struck = true, Squareness = 0f, Direction = travel, Degrees = 0f };

            // Bails: each rests on two stumps; it flies if either moves, or if the ball reaches bail height over it.
            bool atBailHeight = centre.y > FieldDimensions.StumpHeight - ballRadius;
            for (int b = 0; b < 2; b++)
            {
                float bailX = (StumpX(b) + StumpX(b + 1)) * 0.5f;
                float under = Mathf.Max(plan.Stumps[b].Struck ? plan.Stumps[b].Squareness : -1f,
                                        plan.Stumps[b + 1].Struck ? plan.Stumps[b + 1].Squareness : -1f);
                bool overIt = atBailHeight && Mathf.Abs(centre.x - bailX) < FieldDimensions.BailLength * 0.5f + ballRadius;
                // Always draw the same random numbers, so a bail's flight doesn't depend on the other's.
                float lift = Mathf.Lerp(25f, 55f, R()) * Mathf.Deg2Rad;
                float yaw = Mathf.Lerp(-20f, 20f, R());
                var spin = new Vector3(R() - 0.5f, R() - 0.5f, R() - 0.5f) * 2f;
                float spinRate = Mathf.Lerp(10f, 30f, R());
                if (under < 0f && !overIt)
                    continue;
                float s = Mathf.Max(under, overIt ? 0.3f : 0f);
                float bailSpeed = speed * (0.05f + 0.15f * s);
                // Away from the impact: a bail beside the hit is pushed out sideways as well as back.
                var side = Vector3.right * Mathf.Sign(bailX - centre.x) * 0.35f;
                var heading = (Quaternion.AngleAxis(yaw, Vector3.up) * travel + side).normalized;
                plan.Bails[b] = new BailLaunch
                {
                    Dislodged = true,
                    Velocity = (heading * Mathf.Cos(lift) + Vector3.up * Mathf.Sin(lift)) * bailSpeed,
                    AngularVelocity = spin.normalized * spinRate * Mathf.Lerp(0.4f, 1f, Mathf.Clamp01(speed / FullSpeed)),
                };
            }

            plan.Loudness = Mathf.Clamp(speed / FullSpeed, 0.2f, 1f);
            float turn = Mathf.Lerp(-CarryOnMaxDegrees, CarryOnMaxDegrees, R());
            plan.BallVelocityAfter = Quaternion.AngleAxis(turn, Vector3.up) * velocity * CarryOnShare;
            return plan;
        }
    }
}
