using System.Collections.Generic;
using UnityEngine;
using FD = CricketVR.FieldDimensions;

namespace CricketVR
{
    /// <summary>
    /// Every cricket-ground dimension, in one place (REQ-018, plan.md §15). The ground builder and the future
    /// virtual-field scorer both read this asset: Assets/CricketVR/Settings/GroundDefinition.asset.
    /// Pitch, crease and stump values are the Laws constants from <see cref="FieldDimensions"/>; an EditMode test
    /// fails if they drift apart.
    /// </summary>
    [CreateAssetMenu(menuName = "CricketVR/Ground Definition", fileName = "GroundDefinition")]
    public class GroundDefinition : ScriptableObject
    {
        public const float MinBoundaryRadius = 55f, MaxBoundaryRadius = 75f;

        [Header("Pitch (Laws values; must equal FieldDimensions)")]
        public float pitchLength = FD.PitchLength;
        public float pitchWidth = FD.PitchWidth;
        [Tooltip("The strip matches the existing pitch collider, which must not change (GD-5).")]
        public float pitchStartZ = FD.PitchStartZ;
        public float pitchEndZ = FD.PitchEndZ;

        [Header("Creases")]
        public float poppingCreaseOffset = FD.PoppingCreaseOffset;
        [Tooltip("Painted this far either side of middle stump, running onto the outfield (GD-4).")]
        public float poppingCreaseHalfLength = 1.83f;
        public float bowlingCreaseHalfLength = FD.BowlingCreaseHalfLength;
        public float returnCreaseX = FD.ReturnCreaseX;
        [Tooltip("Return creases run back this far behind the popping crease.")]
        public float returnCreaseBehindPopping = 2.44f;
        public float creaseLineWidth = FD.CreaseLineWidth;

        [Header("Stumps")]
        public float stumpHeight = FD.StumpHeight;
        public float stumpSetWidth = FD.StumpSetWidth;

        [Header("Boundary (ellipse about the ground centre; a circle when equal)")]
        public float boundaryRadiusX = 65f;
        public float boundaryRadiusZ = 65f;

        [Header("Boundary rope")]
        public float ropeRadius = 0.06f;
        public int ropeSegments = 256;
        public int ropeSides = 6;
        public Color ropeColor = new Color(0.92f, 0.92f, 0.95f);

        [Header("30-yard fielding circle")]
        public float fieldingCircleRadius = 27.43f;
        public float circleMarkerSpacing = 5f;
        public float circleMarkerDiameter = 0.35f;

        [Header("Outfield")]
        [Tooltip("The grass disc extends this far past the rope, under the boards up to the stands (>= standsBeyondRope).")]
        public float outfieldBeyondRope = 10f;
        public float stripeWidth = 5f;
        public Color grassLight = new Color(0.38f, 0.58f, 0.25f);
        public Color grassDark = new Color(0.30f, 0.49f, 0.21f);

        [Header("Pitch look")]
        public Color pitchColor = new Color(0.80f, 0.71f, 0.52f);
        public Color pitchWornColor = new Color(0.66f, 0.56f, 0.40f);
        public Color creaseColor = new Color(0.97f, 0.97f, 0.97f);

        [Header("Sight screens (GD-2: black, white ball)")]
        public float sightScreenWidth = 12f;
        public float sightScreenHeight = 6f;
        public float sightScreenBeyondRope = 5f;
        public Color sightScreenColor = new Color(0.035f, 0.035f, 0.04f);

        [Header("Advertising boards (plain colours only)")]
        public float boardsBeyondRope = 3f;
        public float boardHeight = 1f;
        public float boardPanelLength = 6f;
        public Color[] boardColors =
        {
            new Color(0.10f, 0.25f, 0.60f), new Color(0.85f, 0.85f, 0.88f), new Color(0.75f, 0.15f, 0.12f),
            new Color(0.95f, 0.70f, 0.10f), new Color(0.12f, 0.45f, 0.30f),
        };

        [Header("Stands")]
        public float standsBeyondRope = 9f;
        public float standDepth = 18f;
        public int standTiers = 5;
        public float standFrontHeight = 2f;
        public float standTopHeight = 12f;
        [Tooltip("Extra width either side of each sight screen left open in the boards and stands (m).")]
        public float sightScreenGapExtra = 1.5f;

        [Header("Sky and fog")]
        public Color skyZenith = new Color(0.33f, 0.55f, 0.86f);
        public Color skyHorizon = new Color(0.78f, 0.86f, 0.94f);
        public float skyDomeRadius = 450f;
        public float fogStart = 60f;
        public float fogEnd = 220f;

        [Header("Ground physics")]
        public float outfieldBounciness = 0.45f;
        public float outfieldDynamicFriction = 0.45f;
        public float outfieldStaticFriction = 0.5f;
        [Tooltip("Outfield collider thickness (m): a 45 m/s ball moves 0.31 m per 144 Hz step.")]
        public float outfieldColliderThickness = 1f;
        [Tooltip("The outfield collider extends this far past the rope.")]
        public float outfieldColliderBeyondRope = 12f;
        [Tooltip("Cosmetic rolling resistance on the outfield: deceleration = coefficient x g (PhysX has none).")]
        public float rollingResistance = 0.08f;

        [Header("Leaving the ground")]
        [Tooltip("After crossing the rope the ball is hidden this long later (s).")]
        public float leftGroundHideDelay = 1.5f;
        [Tooltip("Safety despawn beyond this horizontal distance from the ground centre (m).")]
        public float safetyDespawnDistance = 150f;
        [Tooltip("Safety despawn when the ball's centre falls below this height (m), i.e. under the outfield.")]
        public float safetyDespawnBelowY = -0.5f;

        public Vector3 GroundCentre => new Vector3(0f, 0f, pitchLength * 0.5f);
        public Vector2 BoundaryRadii => new Vector2(boundaryRadiusX, boundaryRadiusZ);

        /// <summary>Problems that make this definition unusable; empty when valid.</summary>
        public List<string> Validate(FieldConfig field = null)
        {
            var errors = new List<string>();
            foreach (var (label, r) in new[] { ("boundaryRadiusX", boundaryRadiusX), ("boundaryRadiusZ", boundaryRadiusZ) })
                if (r < MinBoundaryRadius || r > MaxBoundaryRadius)
                    errors.Add($"{label} {r} m is outside {MinBoundaryRadius}-{MaxBoundaryRadius} m");
            if (outfieldBeyondRope < standsBeyondRope)
                errors.Add($"outfieldBeyondRope {outfieldBeyondRope} m must reach the stands ({standsBeyondRope} m) or the sky shows through");
            if (field != null)
                foreach (var f in field.fielders)
                    foreach (bool left in new[] { false, true })
                    {
                        float inside = GroundGeometry.DistanceInsideBoundary(GroundGeometry.FielderPosition(f.angleDegrees, f.radius, left), GroundCentre, BoundaryRadii);
                        if (inside < FieldConfig.MinInsideRope)
                            errors.Add($"fielder '{f.name}' ({f.angleDegrees} deg, {f.radius} m) is only {inside:F1} m inside the rope; needs {FieldConfig.MinInsideRope} m");
                    }
            return errors;
        }
    }
}
