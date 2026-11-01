using System;
using UnityEngine;

namespace CricketVR
{
    /// <summary>Which side of the stumps the batter stands (stance and off side).</summary>
    public enum Handedness { Right, Left }

    /// <summary>Which physical controller holds the bat.</summary>
    public enum BatHand { Right, Left }

    /// <summary>
    /// Bat shape in the bat's own frame: +Y runs up the handle, -Y toward the toe, +Z out of the hitting face,
    /// X across the blade. The origin is the grip point (centre of the bottom hand's fist).
    /// </summary>
    [Serializable]
    public struct BatGeometry
    {
        public float bladeWidth;
        public float bladeLength;
        public float bladeThickness;
        public float handleLength;
        public float handleDiameter;
        [Tooltip("Handle length below the grip point, i.e. grip point to the top of the blade (m).")]
        public float gripToBlade;

        public static BatGeometry Default => new BatGeometry
        {
            bladeWidth = 0.108f,
            bladeLength = 0.56f,
            bladeThickness = 0.04f,
            handleLength = 0.30f,
            handleDiameter = 0.032f,
            gripToBlade = 0.09f, // blade starts just past the controller's ring
        };

        /// <summary>Blade centre in the bat frame.</summary>
        public Vector3 BladeCenter => new Vector3(0f, -gripToBlade - bladeLength * 0.5f, 0f);
        public Vector3 BladeSize => new Vector3(bladeWidth, bladeLength, bladeThickness);
        /// <summary>Toe (bottom centre of the blade) in the bat frame.</summary>
        public Vector3 Toe => new Vector3(0f, -gripToBlade - bladeLength, 0f);
        /// <summary>Handle centre in the bat frame; the handle extends above and slightly below the grip.</summary>
        public Vector3 HandleCenter => new Vector3(0f, handleLength * 0.5f - gripToBlade, 0f);
    }

    public static class BatFrame
    {
        /// <summary>
        /// Rotation from the controller's GRIP pose to the bat frame, before owner alignment offsets.
        /// Default follows the owner's reference (a VR bat mount, 2026-10-23): the bat runs along the controller's
        /// handle with the blade extending past the ring end (grip +Z), and the blade lies parallel to the button
        /// face (face normal along grip +Y). Same for both hands. The owner fine-tunes all three axes in-headset.
        /// Must be driven by devicePosition/deviceRotation, not the aim (pointer) pose (E-008/E-009).
        /// </summary>
        public static Quaternion BaseRotation(BatHand hand) => Quaternion.LookRotation(Vector3.up, Vector3.back);

        /// <summary>
        /// Full controller-to-bat rotation with the owner's offsets, applied in the bat frame:
        /// face turn about the handle (bat Y), then tilt about the blade width (bat X), then lean about the face normal (bat Z).
        /// Finally <paramref name="spinDegrees"/> (plus 180 if <paramref name="faceFlipped"/>) twists the bat about its own,
        /// already tilted, length. Applied last so it turns the bat around the handle the player sees without changing
        /// the bat's angle. (The face turn, applied first, swings the direction the tilt points instead.)
        /// </summary>
        public static Quaternion GripRotation(BatHand hand, float tiltDegrees, float faceTurnDegrees, float leanDegrees,
            bool faceFlipped = false, float spinDegrees = 0f)
        {
            return BaseRotation(hand) * OffsetRotation(tiltDegrees, faceTurnDegrees, leanDegrees, faceFlipped, spinDegrees);
        }

        /// <summary>The owner's offsets alone, in the bat frame (see <see cref="GripRotation"/>); bat mode applies them after the mount.</summary>
        public static Quaternion OffsetRotation(float tiltDegrees, float faceTurnDegrees, float leanDegrees,
            bool faceFlipped = false, float spinDegrees = 0f)
        {
            return Quaternion.AngleAxis(faceTurnDegrees, Vector3.up)
                * Quaternion.AngleAxis(tiltDegrees, Vector3.right)
                * Quaternion.AngleAxis(leanDegrees, Vector3.forward)
                * Quaternion.AngleAxis(spinDegrees + (faceFlipped ? 180f : 0f), Vector3.up);
        }

        public static BatHand DefaultHandFor(Handedness handedness) =>
            handedness == Handedness.Right ? BatHand.Right : BatHand.Left;
    }
}
