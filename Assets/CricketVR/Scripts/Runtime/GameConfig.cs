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

        [Header("Bat mode (controller strapped to a real bat, plan.md §21)")]
        [Tooltip("The controller strapped to the bat (owner, 2026-11-01: the right one).")]
        public BatHand strappedController = BatHand.Right;
        [Tooltip("Is the controller on the hitting face (true) or the back (owner's bat, 2026-11-01)? The face flip swaps the hitting side in the headset.")]
        public bool controllerOnFace;

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

        [Tooltip("The delivery types (M7.5; the straighter one added 2026-10-27), one asset each; filled by the scene builder.")]
        public DeliveryType[] deliveryTypes = new DeliveryType[0];
        [Tooltip("T076 headset build only (set by CricketVR > Build > swing check, then restored): Net Practice bowls the fixed swing-check sequence.")]
        public bool swingCheck;
        [Tooltip("T082 headset build only (set by CricketVR > Build > spin check, then restored): Net Practice bowls the fixed spin-check sequence.")]
        public bool spinCheck;
        [Tooltip("T088 headset build only (set by CricketVR > Build > wicket check, then restored): both modes bowl the fixed wicket-check sequence at the stumps.")]
        public bool wicketCheck;

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

        /// <summary>The configured types' settings (missing assets are skipped; the planner falls back to the code defaults when none are set).</summary>
        public DeliveryTypeData[] TypeCatalogue()
        {
            if (deliveryTypes == null || deliveryTypes.Length == 0)
                return DeliveryTypeData.All;
            var list = new System.Collections.Generic.List<DeliveryTypeData>(deliveryTypes.Length);
            foreach (var t in deliveryTypes)
                if (t != null) list.Add(t.data);
            return list.ToArray();
        }
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
        [Tooltip("GroundMode: a hit ball's result is decided this long after contact unless something ends it sooner; " +
                 "it is the played-on window (stumps still count until then), so the batter can call the next ball promptly.")]
        public float groundHitDecisionSeconds = 1.5f;
        [Tooltip("GroundMode: how long a hit ball stays visible after its result is decided, so the shot can be watched (s).")]
        public float groundHitLinger = 5f;
        public bool logResults = true;
    }

    [Serializable]
    public class SessionSettings
    {
        public SessionTimings timings = SessionTimings.Default;
        [Tooltip("Bat mode (a real bat): B can't be pressed while holding it, so balls come automatically (owner 2026-11-01: an 8 s break, result shown then a countdown).")]
        public SessionTimings realBatTimings = new SessionTimings { waitForReady = false, firstCountdown = 8f, countdown = 3f, betweenBalls = 5f };
        public int defaultOvers = 2;
        [Tooltip("Match Simulation balls until the scenario generator exists (M10).")]
        public int matchPlaceholderBalls = 12;
        [Tooltip("Development builds: write one CSV row per finalized ball to Application.persistentDataPath (REQ-027).")]
        public bool logBallsCsv = true;
        [Tooltip("Pause if the bat controller stays untracked this long during a countdown or delivery (s).")]
        public float trackingLossPauseSeconds = 0.5f;
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
        public SessionSettings session = new SessionSettings();
        public WicketSettings wicket = new WicketSettings();
    }

    /// <summary>The wicket reaction (REQ-049..052, plan.md §20): cosmetic only.</summary>
    [Serializable]
    public class WicketSettings
    {
        [Tooltip("Stumps and bails knocked about when the wicket falls.")]
        public bool reaction = true;
        [Tooltip("The broken wicket stays at least this long after the result (s) before the next ball resets it.")]
        public float holdSeconds = 1.5f;
        [Tooltip("Knock-back time of a struck stump (s).")]
        public float knockSeconds = 0.12f;
        [Tooltip("LED 'zing' stumps and bails flash when dislodged.")]
        public bool ledInNetPractice = false;
        public bool ledInMatchSimulation = true;
        public Color ledColor = new Color(1f, 0.08f, 0.05f);
        [Tooltip("LED flashing time (s).")]
        public float ledSeconds = 1.5f;
        [Tooltip("Clatter volume at a 140 km/h hit.")]
        [Range(0f, 1f)] public float volume = 0.9f;
    }
}
