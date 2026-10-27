using UnityEngine;

namespace CricketVR
{
    /// <summary>
    /// Practice-net geometry in metres. World convention (plan.md §5):
    /// origin = base of the striker's middle stump, +Y up, +Z from striker toward the launcher
    /// (deliveries travel -Z), +X = batter's right when facing the launcher (off side for a right-hander).
    /// </summary>
    public static class FieldDimensions
    {
        // Pitch (Laws of Cricket reference values).
        public const float PitchLength = 20.12f;          // stump base to stump base
        public const float PitchWidth = 3.05f;
        public const float PitchStartZ = -1.5f;            // pitch surface extends a little past each set of stumps
        public const float PitchEndZ = 21.5f;
        /// <summary>Top of the pitch collider. It sits 2 mm above the outfield (y = 0) so the pitch decides every bounce
        /// on the strip; the delivery solver aims the ball centre at this height plus one radius (E-052).</summary>
        public const float PitchSurfaceHeight = 0.002f;

        // Crease markings.
        public const float PoppingCreaseOffset = 1.22f;     // in front of each set of stumps
        public const float PoppingCreaseHalfLength = 1.75f; // Laws say >= 1.83 m; trimmed to stay inside the net
        public const float BowlingCreaseHalfLength = 1.32f;
        public const float ReturnCreaseX = 1.32f;
        public const float CreaseLineWidth = 0.05f;

        // Stumps and bails.
        public const float StumpHeight = 0.711f;
        public const float StumpSetWidth = 0.2286f;         // outer edge to outer edge
        public const float StumpDiameter = 0.035f;
        public const float BailLength = 0.1095f;
        public const float BailHeightAboveStumps = 0.013f;

        // Net enclosure (inner faces of the boundary walls).
        public const float NetHalfWidth = 1.8f;
        public const float NetRearZ = -3f;
        public const float NetFrontZ = 24f;
        public const float NetSideHeight = 4f;
        public const float NetRearHeight = 4f;
        public const float NetFrontHeight = 6f;
        public const float NetWallThickness = 0.1f;

        /// <summary>Default release point: bowler's popping crease, 2 m high (plan.md §5).</summary>
        public static readonly Vector3 DefaultReleasePoint = new Vector3(0f, 2.0f, PitchLength - PoppingCreaseOffset);

        /// <summary>
        /// Where the player stands (floor point under the head). A right-hander stands on the leg side (-X)
        /// of the stumps, just behind the popping crease, so the grounded bat sits near the line of the stumps.
        /// </summary>
        public static readonly Vector3 RightHandedStance = new Vector3(-0.3f, 0f, 1.0f);
        public static readonly Vector3 LeftHandedStance = new Vector3(0.3f, 0f, 1.0f);

        public static float NonStrikerStumpsZ => PitchLength;
    }
}
