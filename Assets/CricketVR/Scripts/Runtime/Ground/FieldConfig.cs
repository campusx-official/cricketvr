using System;
using UnityEngine;

namespace CricketVR
{
    [Serializable]
    public struct FielderSpot
    {
        public string name;
        [Tooltip("0 = straight down the ground toward the machine; positive = off side for a right-hander (+X).")]
        public float angleDegrees;
        [Tooltip("Distance from the striker's middle stump (m).")]
        public float radius;

        public FielderSpot(string name, float angleDegrees, float radius)
        {
            this.name = name;
            this.angleDegrees = angleDegrees;
            this.radius = radius;
        }
    }

    /// <summary>
    /// Placeholder field (REQ-022): static figures at polar positions about the striker's stumps, mirrored for a
    /// left-hander. Assets/CricketVR/Settings/FieldConfig.asset. They do not move or field (yet).
    /// </summary>
    [CreateAssetMenu(menuName = "CricketVR/Field Config", fileName = "FieldConfig")]
    public class FieldConfig : ScriptableObject
    {
        /// <summary>Every fielder must stand at least this far inside the rope at its own angle (owner rule, GD-3).</summary>
        public const float MinInsideRope = 3f;

        public bool showFielders = true;

        public FielderSpot[] fielders =
        {
            new FielderSpot("Keeper", 180f, 12f),
            new FielderSpot("Slip", 160f, 16f),
            new FielderSpot("Point", 95f, 25f),
            new FielderSpot("Cover", 55f, 28f),
            new FielderSpot("Mid-off", 20f, 27f),
            new FielderSpot("Mid-on", -20f, 27f),
            new FielderSpot("Midwicket", -60f, 28f),
            new FielderSpot("Square leg", -95f, 25f),
            new FielderSpot("Third man", 145f, 50f), // 58 m in the brief would stand outside a 65 m rope (GD-3)
            new FielderSpot("Fine leg", -150f, 50f),
        };
    }
}
