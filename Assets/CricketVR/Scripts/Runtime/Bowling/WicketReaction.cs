using System;
using UnityEngine;

namespace CricketVR
{
    /// <summary>
    /// The striker's wicket reacting to a decided Bowled (REQ-049..052, plan.md §20): the struck stumps are knocked
    /// back about their base (scripted, not physics), the bails fly as rigidbodies on the WicketDebris layer, the
    /// ball carries on past the stumps, a clatter plays and (optionally) the LEDs flash. Purely cosmetic: it listens
    /// to <see cref="ScoreManager.WicketBroken"/> and never decides anything. <see cref="ResetWicket"/> puts every
    /// piece back exactly where it started.
    /// </summary>
    public class WicketReaction : MonoBehaviour
    {
        [SerializeField] GameConfig config;
        [SerializeField] ScoreManager score;
        [SerializeField] BowlingMachine machine;
        [SerializeField] BallController ball;
        [Tooltip("Stump visuals by world position: +X, middle, −X.")]
        [SerializeField] Transform[] stumps = new Transform[3];
        [Tooltip("Bails by world position: +X bail, −X bail.")]
        [SerializeField] Rigidbody[] bails = new Rigidbody[2];
        [Tooltip("The stumps' detection box; the ball ignores it after the reaction until the reset.")]
        [SerializeField] Collider detectionBox;
        [SerializeField] AudioSource audioSource;
        [Tooltip("StumpHit_N.wav recordings; empty = a generated placeholder clatter.")]
        [SerializeField] AudioClip[] clips = new AudioClip[0];
        [Tooltip("Renderers that flash when the LED setting is on.")]
        [SerializeField] Renderer[] ledRenderers = new Renderer[0];
        [Tooltip("Bright unlit material the LED renderers show while lit.")]
        [SerializeField] Material ledMaterial;

        Vector3[] _stumpPos, _bailPos;
        Quaternion[] _stumpRot, _bailRot;
        float[] _knockDegrees = new float[3];
        Vector3[] _knockAxis = new Vector3[3];
        Material[] _ledOriginals;
        bool _ledLit;
        AudioClip _placeholder;
        EnvironmentMode _mode;
        float _brokenAt = float.NegativeInfinity;
        bool _resetPending;
        int _brokenDelivery = -1;

        public bool Broken { get; private set; }
        public WicketReactionPlan LastPlan { get; private set; }
        /// <summary>Raised when the wicket breaks: (delivery id, plan). For tests and logging.</summary>
        public event Action<int, WicketReactionPlan> Reacted;
        public bool LedOn => _mode == EnvironmentMode.GroundMode ? config.wicket.ledInMatchSimulation : config.wicket.ledInNetPractice;
        public Transform[] Stumps => stumps;
        public Rigidbody[] Bails => bails;

        void Awake()
        {
            if (config == null || stumps == null || stumps.Length != 3 || bails == null || bails.Length != 2)
            {
                Debug.LogError("[CricketVR] WicketReaction is missing references; run CricketVR > Setup > 6. Build or Repair Main Scene.", this);
                enabled = false;
                return;
            }
            _stumpPos = new Vector3[3];
            _stumpRot = new Quaternion[3];
            for (int i = 0; i < 3; i++)
            {
                _stumpPos[i] = stumps[i].localPosition;
                _stumpRot[i] = stumps[i].localRotation;
            }
            _bailPos = new Vector3[2];
            _bailRot = new Quaternion[2];
            for (int i = 0; i < 2; i++)
            {
                _bailPos[i] = bails[i].transform.localPosition;
                _bailRot[i] = bails[i].transform.localRotation;
                bails[i].isKinematic = true;
            }
            _ledOriginals = new Material[ledRenderers != null ? ledRenderers.Length : 0];
            for (int i = 0; i < _ledOriginals.Length; i++)
                _ledOriginals[i] = ledRenderers[i] != null ? ledRenderers[i].sharedMaterial : null;
            _mode = config.environment;
        }

        void OnEnable()
        {
            if (score != null) score.WicketBroken += React;
            if (machine != null) machine.Bowled += OnBowled;
        }

        void OnDisable()
        {
            if (score != null) score.WicketBroken -= React;
            if (machine != null) machine.Bowled -= OnBowled;
        }

        /// <summary>The session's mode (LED default differs per mode). Resets the wicket.</summary>
        public void SetMode(EnvironmentMode mode)
        {
            _mode = mode;
            ResetWicket();
        }

        // Every launch starts with a whole wicket, whoever bowled it (session, debug keys, tests).
        void OnBowled(int id, DeliveryConfig request, DeliverySolution solution) => ResetWicket();

        /// <summary>
        /// A wicket was decided for delivery <paramref name="id"/>: the ball's position and velocity at the start of the
        /// physics step in which it reached the stumps (world). Subscribed to <see cref="ScoreManager.WicketBroken"/>;
        /// a later source of decided wickets (the M9 played-on sweep, T059) calls it the same way. Once per delivery.
        /// </summary>
        public void React(int id, Vector3 stepStartPosition, Vector3 velocity)
        {
            if (!enabled || !config.wicket.reaction || id == _brokenDelivery)
                return;
            ResetWicket(); // from a clean wicket, always
            _brokenDelivery = id;
            var origin = transform.position;
            var centre = WicketImpact.BallCentreAtStumps(stepStartPosition - origin, velocity, Time.fixedDeltaTime, ball != null ? ball.Radius : 0.036f);
            var plan = WicketImpact.Compute(centre, velocity, ball != null ? ball.Radius : 0.036f, id);
            LastPlan = plan;
            Broken = true;
            _brokenAt = Time.time;

            for (int i = 0; i < 3; i++)
            {
                _knockDegrees[i] = plan.Stumps[i].Struck ? plan.Stumps[i].Degrees : 0f;
                _knockAxis[i] = transform.InverseTransformDirection(plan.Stumps[i].Axis);
            }
            for (int b = 0; b < 2; b++)
            {
                if (!plan.Bails[b].Dislodged)
                    continue;
                var body = bails[b];
                body.isKinematic = false;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                body.linearVelocity = plan.Bails[b].Velocity;
                body.angularVelocity = plan.Bails[b].AngularVelocity;
            }

            // Only a dead ball (its result decided) is sent on past the stumps: the reaction never changes a live one.
            if (ball != null && ball.InFlight && !ball.IsLive && ball.ActiveDeliveryId == id)
            {
                if (detectionBox != null)
                    Physics.IgnoreCollision(ball.GetComponent<Collider>(), detectionBox, true);
                ball.Body.linearVelocity = plan.BallVelocityAfter; // cosmetic: the result is already decided
            }
            PlayClatter(plan.Loudness);
            if (config.scoring.logResults)
            {
                var hand = machine != null ? machine.LastRequest.handedness : Handedness.Right;
                var sb = new System.Text.StringBuilder($"[CricketVR] Wicket #{id}: {velocity.magnitude * 3.6f:F0} km/h at x {centre.x:+0.000;-0.000} m, h {centre.y:F2} m;");
                for (int i = 0; i < 3; i++)
                    if (plan.Stumps[i].Struck)
                        sb.Append($" {WicketImpact.NameOf(i, hand)} stump {plan.Stumps[i].Degrees:F0} deg (square {plan.Stumps[i].Squareness:F2})");
                for (int b = 0; b < 2; b++)
                    if (plan.Bails[b].Dislodged)
                        sb.Append($"; {(b == 0 ? "+X" : "-X")} bail {plan.Bails[b].Velocity.magnitude:F1} m/s");
                sb.Append(LedOn ? "; LED" : "");
                Debug.Log(sb.ToString());
            }
            Reacted?.Invoke(id, plan);
        }

        void PlayClatter(float loudness)
        {
            if (audioSource == null)
                return;
            AudioClip clip;
            if (clips != null && clips.Length > 0)
                clip = clips[UnityEngine.Random.Range(0, clips.Length)];
            else
                clip = _placeholder != null ? _placeholder : (_placeholder = PlaceholderClatter());
            audioSource.PlayOneShot(clip, config.wicket.volume * loudness);
        }

        /// <summary>
        /// PLACEHOLDER until the owner's StumpHit_N.wav recordings: a few short decaying wood clicks (stump, then the
        /// two bails), generated once.
        /// </summary>
        static AudioClip PlaceholderClatter()
        {
            const int rate = 44100;
            int n = rate / 4;
            var data = new float[n];
            var rng = new System.Random(4);
            foreach (var (start, pitch, gain) in new[] { (0f, 900f, 1f), (0.018f, 2300f, 0.55f), (0.045f, 2600f, 0.45f), (0.11f, 2400f, 0.25f) })
            {
                int s0 = (int)(start * rate);
                for (int i = s0; i < n; i++)
                {
                    float t = (i - s0) / (float)rate;
                    float env = Mathf.Exp(-t * 60f);
                    if (env < 1e-3f) break;
                    float noise = (float)rng.NextDouble() * 2f - 1f;
                    data[i] += gain * env * (0.6f * Mathf.Sin(2f * Mathf.PI * pitch * t) + 0.4f * noise);
                }
            }
            for (int i = 0; i < n; i++) data[i] = Mathf.Clamp(data[i] * 0.7f, -1f, 1f);
            var clip = AudioClip.Create("StumpHit_PLACEHOLDER", n, 1, rate, false);
            clip.SetData(data, 0);
            Debug.Log("[CricketVR] Wicket sound: using the generated PLACEHOLDER clatter (no StumpHit_N.wav recordings yet).");
            return clip;
        }

        void LateUpdate()
        {
            if (!Broken)
            {
                if (_resetPending) _resetPending = false;
                return;
            }
            float t = Time.time - _brokenAt;
            float k = Mathf.Clamp01(t / Mathf.Max(1e-3f, config.wicket.knockSeconds));
            float ease = 1f - (1f - k) * (1f - k) * (1f - k); // fast out, settling into the tilt
            for (int i = 0; i < 3; i++)
            {
                if (_knockDegrees[i] <= 0f)
                    continue;
                var rot = Quaternion.AngleAxis(_knockDegrees[i] * ease, _knockAxis[i]);
                var pivot = new Vector3(_stumpPos[i].x, 0f, _stumpPos[i].z); // the stump's base, pegged in the ground
                stumps[i].localPosition = pivot + rot * (_stumpPos[i] - pivot);
                stumps[i].localRotation = rot * _stumpRot[i];
            }
            UpdateLed(t);
            if (_resetPending && t >= config.wicket.holdSeconds)
                ResetWicket();
        }

        /// <summary>The LEDs flash 0.12 s on, 0.08 s off for <see cref="WicketSettings.ledSeconds"/> (a material swap: no lights).</summary>
        void UpdateLed(float t)
        {
            bool lit = LedOn && ledMaterial != null && t < config.wicket.ledSeconds && Mathf.Repeat(t, 0.2f) < 0.12f;
            SetLed(lit);
        }

        void SetLed(bool lit)
        {
            if (lit == _ledLit || ledRenderers == null || _ledOriginals == null)
                return;
            _ledLit = lit;
            for (int i = 0; i < ledRenderers.Length; i++)
                if (ledRenderers[i] != null)
                    ledRenderers[i].sharedMaterial = lit ? ledMaterial : _ledOriginals[i];
        }

        /// <summary>The LEDs are showing their lit colour right now (tests).</summary>
        public bool LedLit => _ledLit;

        /// <summary>
        /// The next ball is being called: reset once the broken wicket has been shown for the hold time (at once if
        /// it already has).
        /// </summary>
        public void ResetAfterHold()
        {
            if (!Broken) return;
            if (Time.time - _brokenAt >= config.wicket.holdSeconds) ResetWicket();
            else _resetPending = true;
        }

        /// <summary>Every stump and bail back exactly where it started, bails kinematic and still, LEDs off.</summary>
        public void ResetWicket()
        {
            _resetPending = false;
            if (_stumpPos == null)
                return;
            for (int i = 0; i < 3; i++)
            {
                stumps[i].localPosition = _stumpPos[i];
                stumps[i].localRotation = _stumpRot[i];
                _knockDegrees[i] = 0f;
            }
            for (int b = 0; b < 2; b++)
            {
                var body = bails[b];
                if (!body.isKinematic)
                {
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                    body.isKinematic = true;
                }
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                body.transform.localPosition = _bailPos[b];
                body.transform.localRotation = _bailRot[b];
                body.position = body.transform.position;
                body.rotation = body.transform.rotation;
            }
            if (ball != null && detectionBox != null)
                Physics.IgnoreCollision(ball.GetComponent<Collider>(), detectionBox, false);
            SetLed(false);
            Broken = false;
            _brokenDelivery = -1;
        }

        public void Configure(GameConfig cfg, ScoreManager scoreManager, BowlingMachine bowlingMachine, BallController ballController,
            Transform[] stumpVisuals, Rigidbody[] bailBodies, Collider box, AudioSource source, AudioClip[] clatterClips, Renderer[] leds,
            Material led = null)
        {
            config = cfg;
            score = scoreManager;
            machine = bowlingMachine;
            ball = ballController;
            stumps = stumpVisuals;
            bails = bailBodies;
            detectionBox = box;
            audioSource = source;
            clips = clatterClips;
            ledRenderers = leds;
            ledMaterial = led;
        }
    }
}
