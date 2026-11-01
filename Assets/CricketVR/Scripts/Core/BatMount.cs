using System.Collections.Generic;
using UnityEngine;

namespace CricketVR
{
    /// <summary>How the bat is held: the controller in the hand (a virtual bat), or strapped to a real bat (bat mode).</summary>
    public enum BatMode { InHand, Strapped }

    /// <summary>
    /// Bat mode (REQ-053..056, plan.md §21): where the strapped controller sits on the real bat. The mount is the bat
    /// frame's pose in the controller's grip frame (the same thing <see cref="BatFrame.GripRotation"/> gives in the
    /// hand), so the rest of the bat code is unchanged. Calibration measures it on the real bat: the toe and the top of
    /// the handle by pivoting each on the floor, and the face by laying the bat flat. Pure and testable.
    /// </summary>
    public static class BatMount
    {
        /// <summary>Calibration fails if the measured toe-to-handle length is further than this from the model bat (m).</summary>
        public const float LengthTolerance = 0.12f;
        /// <summary>A pivot needs at least this much rotation between its samples (degrees).</summary>
        public const float MinPivotSpread = 25f;
        /// <summary>A pivot fails above this RMS distance of the samples from one fixed point (m).</summary>
        public const float MaxPivotRms = 0.015f;
        /// <summary>A pivot point further than this from the floor (y = 0) means the end wasn't on the floor (m).</summary>
        public const float MaxFloorError = 0.10f;
        /// <summary>The face measured flat on the floor must be within this of square to the bat's length (degrees).</summary>
        public const float MaxSquareError = 20f;

        /// <summary>
        /// The owner's strapping (photos, 2026-11-01), before calibration: the controller is taped on the back of the blade
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

        /// <summary>
        /// Pivot calibration: the point fixed to the controller (<paramref name="local"/>, grip frame) that stayed at one
        /// world point (<paramref name="world"/>) while the controller moved. Least squares over
        /// R_i·local + t_i = world. False if there are too few samples or too little rotation to fix it.
        /// </summary>
        public static bool SolvePivot(IReadOnlyList<Pose> samples, out Vector3 local, out Vector3 world, out float rms)
        {
            local = world = Vector3.zero;
            rms = float.PositiveInfinity;
            if (samples == null || samples.Count < 10 || RotationSpread(samples) < MinPivotSpread)
                return false;

            // Normal equations A^T A x = A^T b with A_i = [R_i | -I], b_i = -t_i, x = (local, world).
            var ata = new double[6, 6];
            var atb = new double[6];
            foreach (var s in samples)
            {
                var r = Matrix4x4.Rotate(s.rotation);
                for (int row = 0; row < 3; row++)
                {
                    var a = new double[6];
                    for (int c = 0; c < 3; c++) a[c] = r[row, c];
                    a[3 + row] = -1.0;
                    double b = -s.position[row];
                    for (int i = 0; i < 6; i++)
                    {
                        atb[i] += a[i] * b;
                        for (int j = 0; j < 6; j++) ata[i, j] += a[i] * a[j];
                    }
                }
            }
            if (!Solve(ata, atb, out var x))
                return false;
            local = new Vector3((float)x[0], (float)x[1], (float)x[2]);
            world = new Vector3((float)x[3], (float)x[4], (float)x[5]);

            double sum = 0;
            foreach (var s in samples)
                sum += (s.position + s.rotation * local - world).sqrMagnitude;
            rms = (float)System.Math.Sqrt(sum / samples.Count);
            return true;
        }

        /// <summary>Largest rotation (degrees) of any sample from the first.</summary>
        public static float RotationSpread(IReadOnlyList<Pose> samples)
        {
            float max = 0f;
            for (int i = 1; i < samples.Count; i++)
                max = Mathf.Max(max, Quaternion.Angle(samples[0].rotation, samples[i].rotation));
            return max;
        }

        /// <summary>
        /// The mount from the three measurements, all in the controller's grip frame: the toe and the top of the handle
        /// (pivots), and the world's up direction while the bat lay flat on the floor, controller side up. The hitting face
        /// is the controller side if <paramref name="controllerOnFace"/>, else the other side (the face flip swaps it). The bat's grip point is placed from the toe, so the
        /// virtual toe sits on the real one even if the real bat is a little longer or shorter than the model.
        /// </summary>
        public static bool TryFromCalibration(Vector3 toe, Vector3 handleTop, Vector3 upWhenFlat, bool controllerOnFace,
            BatGeometry g, out Pose mount, out string problem)
        {
            mount = default;
            var along = handleTop - toe;
            float length = along.magnitude;
            float model = ModelLength(g);
            if (Mathf.Abs(length - model) > LengthTolerance)
            {
                problem = $"the toe and handle top are {length * 100f:F0} cm apart; the bat should be about {model * 100f:F0} cm. Redo the pivots, keeping each end still on the floor.";
                return false;
            }
            var batUp = along / length;
            float square = Mathf.Abs(90f - Vector3.Angle(batUp, upWhenFlat));
            if (square > MaxSquareError)
            {
                problem = $"the flat bat was tilted {square:F0}° from flat. Lay it flat on the floor, controller side up, and keep it still.";
                return false;
            }
            var face = Vector3.ProjectOnPlane(upWhenFlat, batUp).normalized * (controllerOnFace ? 1f : -1f);
            var rotation = Quaternion.LookRotation(face, batUp);
            mount = new Pose(toe - rotation * g.Toe, rotation);
            problem = null;
            return true;
        }

        /// <summary>Toe to the top of the handle on the model bat (m).</summary>
        public static float ModelLength(BatGeometry g) => g.bladeLength + g.handleLength;

        static bool Solve(double[,] a, double[] b, out double[] x)
        {
            int n = b.Length;
            x = new double[n];
            var m = (double[,])a.Clone();
            var v = (double[])b.Clone();
            for (int col = 0; col < n; col++)
            {
                int pivot = col;
                for (int r = col + 1; r < n; r++)
                    if (System.Math.Abs(m[r, col]) > System.Math.Abs(m[pivot, col])) pivot = r;
                if (System.Math.Abs(m[pivot, col]) < 1e-9)
                    return false;
                if (pivot != col)
                {
                    for (int c = 0; c < n; c++) { var t = m[col, c]; m[col, c] = m[pivot, c]; m[pivot, c] = t; }
                    var tv = v[col]; v[col] = v[pivot]; v[pivot] = tv;
                }
                for (int r = col + 1; r < n; r++)
                {
                    double f = m[r, col] / m[col, col];
                    for (int c = col; c < n; c++) m[r, c] -= f * m[col, c];
                    v[r] -= f * v[col];
                }
            }
            for (int r = n - 1; r >= 0; r--)
            {
                double s = v[r];
                for (int c = r + 1; c < n; c++) s -= m[r, c] * x[c];
                x[r] = s / m[r, r];
            }
            return true;
        }
    }
}
