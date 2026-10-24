using System;
using UnityEngine;

namespace CricketVR
{
    [Serializable]
    public struct GripAlignment
    {
        [Tooltip("Grip point offset in the controller's local frame (m).")]
        public Vector3 position;
        [Tooltip("Rotation about the blade's width axis (degrees, -180..180).")]
        public float tilt;
        [Tooltip("Turn about the untilted handle axis, applied before tilt: swings the direction the tilt points (degrees, -180..180).")]
        public float faceTurn;
        [Tooltip("Rotation about the face normal, leaning the bat sideways (degrees, -180..180).")]
        public float lean;
        [Tooltip("Slides the bat along its length in the hand (m); positive moves it toward the handle end.")]
        public float slide;
        [Tooltip("Swap the hitting face and the back without changing the bat's angle.")]
        public bool faceFlipped;
        [Tooltip("Twist about the bat's own (tilted) handle axis, applied last (degrees, -180..180).")]
        public float spin;
    }

    [Serializable]
    public class BatSettings
    {
        public BatGeometry geometry = BatGeometry.Default;
        public BatMotionLimits motionLimits = BatMotionLimits.Default;
        public GripAlignment rightHandGrip;
        public GripAlignment leftHandGrip;

        [Header("In-headset alignment nudges")]
        [Tooltip("Degrees per thumbstick step.")]
        public float alignmentStepDegrees = 5f;
        [Tooltip("Metres per slide step.")]
        public float slideStep = 0.01f;

        [Header("Debug")]
        [Tooltip("Tint the blade red while contacts are suppressed after a tracking discontinuity.")]
        public bool showSuppressionTint = true;
        [Tooltip("Log a summary of each swing (peak toe speed, peak angular speed) to the device log.")]
        public bool logSwings = true;

        public GripAlignment GripFor(BatHand hand) => hand == BatHand.Right ? rightHandGrip : leftHandGrip;
    }

    [Serializable]
    public class DeliverySettings
    {
        public DeliveryConfig defaultDelivery = DeliveryConfig.Create("Good length", 80f, 0f, 6f);

        [Tooltip("Cycled in the headset with a tap of B (debug).")]
        public DeliveryConfig[] debugPresets =
        {
            DeliveryConfig.Create("Good length", 80f, 0f, 6f),
            DeliveryConfig.Create("Full", 80f, 0f, 3.5f),
            DeliveryConfig.Create("Short", 80f, 0f, 9f),
            DeliveryConfig.Create("Outside off", 80f, 0.25f, 6f),
            DeliveryConfig.Create("Quick", 100f, 0f, 6.5f),
            DeliveryConfig.Create("Slow", 60f, 0f, 5f),
            DeliveryConfig.Create("Unreachable test", 30f, 0f, 6f),
        };

        [Header("Ball")]
        public float ballRadius = 0.036f;
        public float ballMass = 0.16f;

        [Header("Delivery lifetime")]
        [Tooltip("A delivery ends this long after release at the latest (s).")]
        public float ballTimeout = 10f;
        [Tooltip("Below this speed (m/s) for settleTime, the ball counts as settled.")]
        public float settleSpeed = 0.2f;
        public float settleTime = 0.5f;

        [Header("Debug")]
        public bool showDebugMarkers = true;
        public bool logDeliveries = true;
    }

    [Serializable]
    public class ContactSettings
    {
        public BatResponseTuning response = BatResponseTuning.Default;

        [Header("Feedback")]
        [Tooltip("Approach speed (m/s) that gives full impact volume and the strongest vibration.")]
        public float fullImpactSpeed = 30f;
        public float hapticMinAmplitude = 0.55f;
        public float hapticMaxAmplitude = 1f;
        public float hapticSeconds = 0.06f;

        [Header("Debug")]
        public bool showContactMarker = true;
        public bool logContacts = true;
        [Tooltip("Log deliveries that passed this close (m) to the blade without contact.")]
        public float logNearMissWithin = 0.15f;
    }

    [Serializable]
    public class ScoringSettings
    {
        public ScoringRules rules = ScoringRules.Default;
        [Tooltip("A ball whose centre is this far (m) outside a net wall has left the enclosure over it.")]
        public float exitMargin = 0.3f;
        [Tooltip("After a delivery is finalized the ball stays visible (but can't be hit or score) for this long (s).")]
        public float deadBallLinger = 1.5f;
        public bool logResults = true;
    }

    /// <summary>Tunable game settings. One asset: Assets/CricketVR/Settings/GameConfig.asset.</summary>
    [CreateAssetMenu(menuName = "CricketVR/Game Config", fileName = "GameConfig")]
    public class GameConfig : ScriptableObject
    {
        [Tooltip("NetMode: practice net with net scoring. GroundMode: cricket ground, scoring pending (plan.md §15).")]
        public EnvironmentMode environment = EnvironmentMode.NetMode;
        [Tooltip("Fixed foveated rendering level on Quest: 0 = off, 1 = maximum (plan.md §15).")]
        [Range(0f, 1f)] public float foveationLevel = 0.5f;
        [Tooltip("Development builds: log performance every N seconds (0 = off). AC-14 evidence.")]
        public float perfWindowSeconds = 10f;
        [Tooltip("Development builds: alternate foveation between foveationLevel and off every N seconds (0 = never).")]
        public float foveationAbSeconds = 0f;
        public Handedness defaultHandedness = Handedness.Right;
        public BatSettings bat = new BatSettings();
        public DeliverySettings delivery = new DeliverySettings();
        public ContactSettings contact = new ContactSettings();
        public ScoringSettings scoring = new ScoringSettings();
    }
}
