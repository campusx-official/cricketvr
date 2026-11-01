using UnityEngine;

namespace CricketVR
{
    /// <summary>How the bat is held: the controller in the hand (a virtual bat), or strapped to a real bat (bat mode).</summary>
    public enum BatMode { InHand, Strapped }

    /// <summary>
    /// Bat mode (REQ-053..056, plan.md §21): where the strapped controller sits on the real bat. The mount is the bat
    /// frame's pose in the controller's grip frame (the same thing <see cref="BatFrame.GripRotation"/> gives in the
    /// hand), so the rest of the bat code is unchanged. The owner fits it in the headset (Bat setup). Pure and testable.
    /// </summary>
    public static class BatMount
    {
        /// <summary>
        /// The owner's strapping (photos, 2026-11-01), before it is fitted: the controller is taped on the back of the blade
        /// just below the shoulder, its handle toward the bat's handle and its buttons (grip +Y) facing out from that side
        /// (<paramref name="controllerOnFace"/> false); the grip origin is about 6 cm down the blade and 2.5 cm off the surface.
        /// </summary>
        public static Pose DefaultPose(BatGeometry g, bool controllerOnFace)
        {
            float side = controllerOnFace ? 1f : -1f;
            // The controller's pose in the bat frame: its forward (+Z, toward its buttons end) runs down the bat to the toe.
            var controllerInBat = new Pose(
                new Vector3(0f, -g.gripToBlade - 0.06f, side * (g.bladeThickness * 0.5f + 0.025f)),
                Quaternion.LookRotation(Vector3.down, new Vector3(0f, 0f, side)));
            return Inverse(controllerInBat);
        }

        public static Pose Inverse(Pose p)
        {
            var inv = Quaternion.Inverse(p.rotation);
            return new Pose(inv * -p.position, inv);
        }

        /// <summary>How far the fitted controller may sit from the bat's centre line, across the blade and off its surfaces (m).</summary>
        public const float MaxAcross = 0.15f, MaxOut = 0.15f;

        /// <summary>Where the controller may be fitted along the bat (bat-frame y): from the toe to the top of the handle.</summary>
        public static float ClampAlong(BatGeometry g, float y) => Mathf.Clamp(y, g.Toe.y, g.handleLength - g.gripToBlade);
    }
}
