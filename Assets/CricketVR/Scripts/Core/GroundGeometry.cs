using System.Collections.Generic;
using UnityEngine;

namespace CricketVR
{
    /// <summary>
    /// Pure cricket-ground geometry (plan.md §15). Angles are measured from +Z (straight down the ground toward the
    /// machine) toward +X (off side for a right-hander). The boundary is an ellipse around the ground centre with
    /// radii (x, z); a circle when they are equal.
    /// </summary>
    public static class GroundGeometry
    {
        /// <summary>Fielder position from polar coordinates about the striker's middle stump (origin). Left-handers mirror.</summary>
        public static Vector3 FielderPosition(float angleDegrees, float radius, bool leftHanded)
        {
            float a = (leftHanded ? -angleDegrees : angleDegrees) * Mathf.Deg2Rad;
            return new Vector3(radius * Mathf.Sin(a), 0f, radius * Mathf.Cos(a));
        }

        /// <summary>Boundary radius along a horizontal direction from the centre (direction need not be normalized).</summary>
        public static float BoundaryRadiusAt(Vector2 radii, Vector2 directionXZ)
        {
            if (directionXZ.sqrMagnitude < 1e-12f)
                return Mathf.Min(radii.x, radii.y);
            var d = directionXZ.normalized;
            float kx = d.x / radii.x, kz = d.y / radii.y;
            return 1f / Mathf.Sqrt(kx * kx + kz * kz);
        }

        public static float BoundaryRadiusAtAngle(Vector2 radii, float angleRadians) =>
            BoundaryRadiusAt(radii, new Vector2(Mathf.Sin(angleRadians), Mathf.Cos(angleRadians)));

        /// <summary>Point at <paramref name="extra"/> metres beyond the boundary at an angle about the centre (y = 0).</summary>
        public static Vector3 BoundaryPoint(Vector3 centre, Vector2 radii, float angleRadians, float extra = 0f)
        {
            float r = BoundaryRadiusAtAngle(radii, angleRadians) + extra;
            return new Vector3(centre.x + r * Mathf.Sin(angleRadians), 0f, centre.z + r * Mathf.Cos(angleRadians));
        }

        /// <summary>Horizontal distance from <paramref name="point"/> to the rope, positive inside the ground.</summary>
        public static float DistanceInsideBoundary(Vector3 point, Vector3 centre, Vector2 radii)
        {
            var rel = new Vector2(point.x - centre.x, point.z - centre.z);
            return BoundaryRadiusAt(radii, rel) - rel.magnitude;
        }

        public static float HorizontalDistance(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        /// <summary>
        /// Points about every <paramref name="spacing"/> metres along the 30-yard fielding circle: semicircles of
        /// <paramref name="radius"/> around each middle stump (bulging away from the pitch) joined by straight lines.
        /// </summary>
        public static void FieldingCircle(float strikerZ, float nonStrikerZ, float radius, float spacing, List<Vector3> points)
        {
            points.Clear();
            float line = nonStrikerZ - strikerZ;
            float arc = Mathf.PI * radius;
            float perimeter = 2f * line + 2f * arc;
            int n = Mathf.Max(4, Mathf.RoundToInt(perimeter / Mathf.Max(0.1f, spacing)));
            for (int i = 0; i < n; i++)
                points.Add(FieldingCirclePoint(strikerZ, nonStrikerZ, radius, perimeter * i / n));
        }

        public static float FieldingCirclePerimeter(float strikerZ, float nonStrikerZ, float radius) =>
            2f * (nonStrikerZ - strikerZ) + 2f * Mathf.PI * radius;

        /// <summary>Point at arc length <paramref name="s"/>: right line, far semicircle, left line, near semicircle.</summary>
        static Vector3 FieldingCirclePoint(float strikerZ, float nonStrikerZ, float radius, float s)
        {
            float line = nonStrikerZ - strikerZ;
            float arc = Mathf.PI * radius;
            if (s < line)
                return new Vector3(radius, 0f, strikerZ + s);
            s -= line;
            if (s < arc)
            {
                float t = Mathf.PI * 0.5f - s / radius; // +90 deg (off side) over +Z to -90 deg
                return new Vector3(radius * Mathf.Sin(t), 0f, nonStrikerZ + radius * Mathf.Cos(t));
            }
            s -= arc;
            if (s < line)
                return new Vector3(-radius, 0f, nonStrikerZ - s);
            s -= line;
            float u = -Mathf.PI * 0.5f - s / radius; // -90 deg over -Z (behind the striker) to -270 deg
            return new Vector3(radius * Mathf.Sin(u), 0f, strikerZ + radius * Mathf.Cos(u));
        }
    }
}
