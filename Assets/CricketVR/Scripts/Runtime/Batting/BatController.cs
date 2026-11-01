using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

namespace CricketVR
{
    /// <summary>
    /// Owns the bat: which controller holds it, grip alignment, the visual, the kinematic physics proxy,
    /// and the pose history used for contact-point velocity. In bat mode (<see cref="BatMode.Strapped"/>, plan.md §21)
    /// the controller is strapped to a real bat: the bat sits at the owner's fitted mount instead of the hand grip, and
    /// everything downstream (proxy, sweeps, contact velocity) is unchanged. While fitting, the visual bat is frozen in
    /// front of the player with a ghost controller on it, which the owner moves to where the real controller is taped. Contacts are only trustworthy while
    /// <see cref="ContactAllowed"/> is true; any tracking discontinuity clears history first.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class BatController : MonoBehaviour
    {
        [SerializeField] GameConfig config;
        [Tooltip("Transform driven by the LEFT controller's grip pose (devicePosition/deviceRotation), not the aim pose.")]
        [SerializeField] Transform leftController;
        [Tooltip("Transform driven by the RIGHT controller's grip pose (devicePosition/deviceRotation), not the aim pose.")]
        [SerializeField] Transform rightController;
        [Tooltip("Root of the visual bat; re-parented under the selected controller at runtime.")]
        [SerializeField] Transform batVisual;
        [SerializeField] Renderer bladeRenderer;
        [SerializeField] Rigidbody physicsProxy;
        [SerializeField] BoxCollider proxyCollider;
        [Tooltip("Ghost controller shown on the frozen bat while fitting bat mode (child of the bat visual).")]
        [SerializeField] Transform fitGhost;
        [Tooltip("Off only for automated tests without an XR device.")]
        [SerializeField] bool requireTrackedState = true;

        static readonly Color SuppressedTint = new Color(1f, 0.35f, 0.3f);
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        // v5: owner-aligned defaults baked into GameConfig (2026-10-23); older saved nudges no longer apply.
        const string PrefsPrefix = "CricketVR.Grip.v5.";
        const string StrapPrefsPrefix = "CricketVR.Strap.v1";
        const string MountPrefsPrefix = "CricketVR.Mount.v1.";
        const string ModePrefsKey = "CricketVR.BatMode";
        const int StrapSlot = 2;

        BatMotionTracker _tracker;
        BatHand _hand = BatHand.Right;
        // Fine-tuning per slot: 0 right hand, 1 left hand, 2 strapped to a real bat (on top of the mount).
        readonly GripAlignment[] _alignment = new GripAlignment[3];
        BatMode _mode = BatMode.InHand;
        Pose _mount;
        Pose _fitController; // while fitting: the controller's pose in the visual bat's frame
        InputAction _leftTracked, _rightTracked;
        readonly List<XRInputSubsystem> _inputSubsystems = new List<XRInputSubsystem>();
        bool _originChanged, _focusChanged;
        MaterialPropertyBlock _tintBlock;
        Color _bladeColor = Color.white;
        bool _tinted;
        bool _hasPose; // FixedUpdate can run before the first Update has sampled a pose
        bool _stepContinuous; // false after a reset: the next step must not sweep across the discontinuity

        // Swing logging (evidence for AC-03): peak toe speed between rest periods.
        bool _inSwing;
        float _peakToeSpeed, _peakOmega;

        public event Action<MotionResetReason> MotionReset;

        public BatHand Hand => _hand;
        public BatGeometry Geometry => config.bat.geometry;
        public GripAlignment Alignment => _alignment[Slot];
        public BatMode Mode => _mode;
        /// <summary>The bat frame's pose in the strapped controller's grip frame (bat mode).</summary>
        public Pose Mount => _mount;
        /// <summary>True once the owner has fitted the mount; false while it is the photo-based default.</summary>
        public bool MountFitted { get; private set; }
        /// <summary>The visual bat is frozen and the ghost controller is being moved (bat-mode setup).</summary>
        public bool Fitting { get; private set; }
        /// <summary>While fitting: the ghost controller's pose in the visual bat's frame.</summary>
        public Pose FitController => _fitController;
        /// <summary>Bat mode: true if the hitting face is the side the controller is strapped to.</summary>
        public bool HitsWithControllerSide => config.bat.controllerOnFace != _alignment[StrapSlot].faceFlipped;
        int Slot => _mode == BatMode.Strapped ? StrapSlot : (int)_hand;
        public BatMotionTracker Motion => _tracker;
        public bool ContactAllowed => _tracker != null && _tracker.ContactAllowed(Time.unscaledTimeAsDouble);
        public Pose GripPose { get; private set; }

        /// <summary>Bat (grip-frame) pose at the start and end of the current physics step, for swept contact tests.</summary>
        public Pose StepStartPose { get; private set; }
        public Pose StepEndPose { get; private set; }
        /// <summary>False until a pose is sampled, while untracked, and on the step right after a motion reset.</summary>
        public bool HasStepPoses { get; private set; }
        Transform Controller(BatHand hand) => hand == BatHand.Right ? rightController : leftController;

        void Awake()
        {
            if (config == null || leftController == null || rightController == null || batVisual == null || physicsProxy == null || proxyCollider == null)
            {
                Debug.LogError("[CricketVR] BatController is missing references; run CricketVR > Setup > 6. Build or Repair Main Scene.", this);
                enabled = false;
                return;
            }

            _tracker = new BatMotionTracker(config.bat.motionLimits, 16);
            _alignment[(int)BatHand.Right] = LoadAlignment(PrefsPrefix + BatHand.Right, config.bat.rightHandGrip);
            _alignment[(int)BatHand.Left] = LoadAlignment(PrefsPrefix + BatHand.Left, config.bat.leftHandGrip);
            _alignment[StrapSlot] = LoadAlignment(StrapPrefsPrefix, default);
            _mount = LoadMount(BatMount.DefaultPose(Geometry, config.bat.controllerOnFace), out bool fitted);
            MountFitted = fitted;
            _mode = (BatMode)PlayerPrefs.GetInt(ModePrefsKey, (int)BatMode.InHand);

            physicsProxy.isKinematic = true;
            physicsProxy.useGravity = false;
            physicsProxy.interpolation = RigidbodyInterpolation.None;
            // Speculative CCD is the only continuous mode Unity supports for kinematic bodies.
            physicsProxy.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            proxyCollider.center = Vector3.zero;
            proxyCollider.size = Geometry.BladeSize;

            if (bladeRenderer != null)
            {
                _tintBlock = new MaterialPropertyBlock();
                _bladeColor = bladeRenderer.sharedMaterial.HasProperty(BaseColorId) ? bladeRenderer.sharedMaterial.GetColor(BaseColorId) : Color.white;
            }
            if (fitGhost != null) fitGhost.gameObject.SetActive(false);
            ApplyHand(_mode == BatMode.Strapped ? config.bat.strappedController : _hand, MotionResetReason.HandChanged);
        }

        void OnEnable()
        {
            _leftTracked = new InputAction("LeftTracked", InputActionType.PassThrough, "<XRController>{LeftHand}/isTracked");
            _rightTracked = new InputAction("RightTracked", InputActionType.PassThrough, "<XRController>{RightHand}/isTracked");
            _leftTracked.Enable();
            _rightTracked.Enable();

            SubsystemManager.GetSubsystems(_inputSubsystems);
            foreach (var s in _inputSubsystems)
                s.trackingOriginUpdated += OnTrackingOriginUpdated;
            Application.focusChanged += OnFocusChanged;
        }

        void OnDisable()
        {
            _leftTracked?.Dispose();
            _rightTracked?.Dispose();
            foreach (var s in _inputSubsystems)
                s.trackingOriginUpdated -= OnTrackingOriginUpdated;
            _inputSubsystems.Clear();
            Application.focusChanged -= OnFocusChanged;
        }

        void OnTrackingOriginUpdated(XRInputSubsystem _) => _originChanged = true;
        void OnFocusChanged(bool _) => _focusChanged = true;

        /// <summary>
        /// Tracked state of the controller holding the bat. Read from the XR node device first (exactly one device
        /// per hand). The Input System binding "&lt;XRController&gt;{RightHand}/isTracked" is only a fallback: on Quest it
        /// can resolve to a different device than the one driving the pose (E-021).
        /// </summary>
        public bool IsControllerTracked(BatHand hand)
        {
            if (!requireTrackedState)
                return true;
            if (TryXrNodeTracked(hand, out bool tracked))
                return tracked;
            return InputSystemTracked(hand);
        }

        static bool TryXrNodeTracked(BatHand hand, out bool tracked)
        {
            tracked = false;
            var device = InputDevices.GetDeviceAtXRNode(hand == BatHand.Right ? XRNode.RightHand : XRNode.LeftHand);
            return device.isValid && device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.isTracked, out tracked);
        }

        bool InputSystemTracked(BatHand hand)
        {
            var action = hand == BatHand.Right ? _rightTracked : _leftTracked;
            return action != null && action.ReadValue<float>() > 0.5f;
        }

        // Development diagnostics for tracked-state sources (E-021): logged at start and every 5 s for 30 s.
        float _nextDiagnostic;
        int _diagnosticsLeft = 7;

        void LogTrackingDiagnostics()
        {
            if (_diagnosticsLeft <= 0 || Time.unscaledTime < _nextDiagnostic)
                return;
            _diagnosticsLeft--;
            _nextDiagnostic = Time.unscaledTime + 5f;
            var node = InputDevices.GetDeviceAtXRNode(_hand == BatHand.Right ? XRNode.RightHand : XRNode.LeftHand);
            bool nodeKnown = TryXrNodeTracked(_hand, out bool nodeTracked);
            var action = _hand == BatHand.Right ? _rightTracked : _leftTracked;
            string bound = "";
            if (action != null)
                foreach (var c in action.controls)
                    bound += $"{c.device.name}:{c.ReadValueAsObject()} ";
            Debug.Log($"[CricketVR] Bat tracking diagnostics ({_hand}, {_mode}): XR node '{node.name}' valid {node.isValid}, " +
                      $"isTracked {(nodeKnown ? nodeTracked.ToString() : "n/a")}; Input System isTracked {InputSystemTracked(_hand)} " +
                      $"[{bound.Trim()}]; step poses {HasStepPoses}; contact allowed {ContactAllowed}");
        }

        void Update()
        {
            double now = Time.unscaledTimeAsDouble;

            // Events from other threads/callbacks are applied here so the history changes in one place.
            if (_originChanged) { _originChanged = false; ResetMotion(MotionResetReason.OriginChanged); }
            if (_focusChanged) { _focusChanged = false; ResetMotion(MotionResetReason.FocusChanged); }

            var controller = Controller(_hand);
            LocalGrip(out var localPosition, out var localRotation);
            GripPose = new Pose(controller.TransformPoint(localPosition), controller.rotation * localRotation);
            _hasPose = true;

            bool tracked = IsControllerTracked(_hand);
            var reason = _tracker.AddSample(now, GripPose.position, GripPose.rotation, tracked);
            if (reason != MotionResetReason.None)
                NotifyReset(reason);

            UpdateSwingLog();
            UpdateTint();
            LogTrackingDiagnostics();
        }

        void FixedUpdate()
        {
            bool usable = _hasPose && !Fitting && IsControllerTracked(_hand);
            if (proxyCollider.enabled != usable)
                proxyCollider.enabled = usable;
            if (!usable)
            {
                HasStepPoses = false;
                _stepContinuous = false;
                return;
            }

            // Kinematic bodies must be moved with MovePosition/MoveRotation so the physics engine
            // sees the motion, rather than teleporting the transform.
            var pose = GripPose;
            StepStartPose = _stepContinuous ? StepEndPose : pose;
            StepEndPose = pose;
            HasStepPoses = true;
            _stepContinuous = true;
            physicsProxy.MovePosition(pose.position + pose.rotation * Geometry.BladeCenter);
            physicsProxy.MoveRotation(pose.rotation);
        }

        /// <summary>World velocity of a point on the bat, including rotation. False while motion is untrusted.</summary>
        public bool TryGetPointVelocity(Vector3 worldPoint, out Vector3 velocity)
        {
            velocity = Vector3.zero;
            if (!ContactAllowed || !_tracker.TryGetVelocity(out var linear, out var angular))
                return false;
            velocity = PoseHistory.PointVelocity(linear, angular, _tracker.History.Latest.Position, worldPoint);
            return true;
        }

        public void SetBatHand(BatHand hand)
        {
            if (hand == _hand)
                return;
            if (_mode == BatMode.Strapped)
            {
                Debug.Log($"[CricketVR] Bat mode: the bat stays on the strapped {_hand} controller");
                return;
            }
            ApplyHand(hand, MotionResetReason.HandChanged);
            Debug.Log($"[CricketVR] Bat hand: {hand}");
        }

        /// <summary>
        /// In hand (a virtual bat) or strapped to a real bat. Bat mode always uses the strapped controller; leaving it
        /// keeps that hand. Persists locally.
        /// </summary>
        public void SetMode(BatMode mode)
        {
            if (mode == _mode)
                return;
            if (Fitting) EndFit(false);
            _mode = mode;
            PlayerPrefs.SetInt(ModePrefsKey, (int)mode);
            PlayerPrefs.Save();
            ApplyHand(mode == BatMode.Strapped ? config.bat.strappedController : _hand, MotionResetReason.HandChanged);
            Debug.Log($"[CricketVR] Bat mode: {mode} ({_hand} controller{(mode == BatMode.Strapped ? MountFitted ? ", fitted mount" : ", default mount: fit it in Bat setup" : "")})");
        }

        /// <summary>
        /// Sets the mount and clears the fine-tuning on top of it, keeping the face flip. Persists locally.
        /// </summary>
        public void SetMount(Pose mount, bool fitted = true)
        {
            _mount = mount;
            MountFitted = fitted;
            SaveMount(mount, fitted);
            var a = _alignment[StrapSlot];
            _alignment[StrapSlot] = new GripAlignment { faceFlipped = a.faceFlipped };
            SaveAlignment(StrapPrefsPrefix, _alignment[StrapSlot]);
            ApplyVisualPose();
            ResetMotion(MotionResetReason.Manual);
            var e = mount.rotation.eulerAngles;
            Debug.Log($"[CricketVR] Bat mount {(fitted ? "fitted" : "default")}: grip point {mount.position * 100f} cm, rotation {e} deg in the controller frame");
        }

        // ---------- Bat-mode fitting (REQ-054) ----------

        /// <summary>
        /// Freezes the visual bat at <paramref name="display"/> (world) and shows the ghost controller where the mount puts
        /// the real one. Contacts are off until <see cref="EndFit"/>. Bat mode only.
        /// </summary>
        public void BeginFit(Pose display)
        {
            if (_mode != BatMode.Strapped || Fitting)
                return;
            LocalGrip(out var position, out var rotation);
            _fitController = BatMount.Inverse(new Pose(position, rotation));
            Fitting = true;
            batVisual.SetParent(transform, false);
            batVisual.SetPositionAndRotation(display.position, display.rotation);
            if (fitGhost != null) fitGhost.gameObject.SetActive(true);
            UpdateGhost();
            ResetMotion(MotionResetReason.Manual);
            Debug.Log("[CricketVR] Bat fit started");
        }

        /// <summary>
        /// Moves the ghost controller by whole slide steps: <paramref name="up"/> along the bat toward the handle,
        /// <paramref name="across"/> to the player's right as the frozen bat is shown (controller side toward them), and
        /// <paramref name="outward"/> away from the blade on the controller's side. Kept within reach of the bat.
        /// </summary>
        public void FitMove(int up, int across = 0, int outward = 0)
        {
            if (!Fitting) return;
            float side = HitsWithControllerSide ? 1f : -1f; // bat Z of the controller's side
            float step = config.bat.slideStep;
            var p = _fitController.position;
            p.y = BatMount.ClampAlong(Geometry, p.y + up * step);
            p.x = Mathf.Clamp(p.x - side * across * step, -BatMount.MaxAcross, BatMount.MaxAcross);
            p.z = Mathf.Clamp(p.z + side * outward * step, -BatMount.MaxOut, BatMount.MaxOut);
            _fitController.position = p;
            UpdateGhost();
        }

        /// <summary>Turns the ghost controller in place on the bat's surface (about the face normal), by whole angle steps.</summary>
        public void FitTurn(int steps) => FitRotate(Quaternion.AngleAxis(steps * config.bat.alignmentStepDegrees, Vector3.forward), local: false);

        /// <summary>Tilts the ghost controller in place (about the blade's width), by whole angle steps.</summary>
        public void FitTilt(int steps) => FitRotate(Quaternion.AngleAxis(steps * config.bat.alignmentStepDegrees, Vector3.right), local: false);

        /// <summary>Rolls the ghost controller in place about its own length (its grip +Z), by whole angle steps.</summary>
        public void FitRoll(int steps) => FitRotate(Quaternion.AngleAxis(steps * config.bat.alignmentStepDegrees, Vector3.forward), local: true);

        void FitRotate(Quaternion turn, bool local)
        {
            if (!Fitting) return;
            _fitController.rotation = local ? _fitController.rotation * turn : turn * _fitController.rotation;
            UpdateGhost();
        }

        /// <summary>Ends fitting; with <paramref name="keep"/> the ghost's pose becomes the mount (saved).</summary>
        public void EndFit(bool keep)
        {
            if (!Fitting)
                return;
            Fitting = false;
            if (fitGhost != null) fitGhost.gameObject.SetActive(false);
            var flip = _alignment[StrapSlot].faceFlipped;
            if (keep)
            {
                // The visual frame is the mount followed by the face flip (fine-tuning is folded in and cleared).
                var grip = BatMount.Inverse(_fitController);
                var unflip = Quaternion.Inverse(BatFrame.OffsetRotation(0f, 0f, 0f, flip));
                SetMount(new Pose(grip.position, grip.rotation * unflip));
                Debug.Log($"[CricketVR] Bat fit saved: controller {_fitController.position * 100f} cm, {_fitController.rotation.eulerAngles} deg in the bat frame; hitting face {(HitsWithControllerSide ? "with" : "without")} the controller");
            }
            else
            {
                _alignment[StrapSlot] = LoadAlignment(StrapPrefsPrefix, default); // undo an unsaved face swap
                Debug.Log("[CricketVR] Bat fit cancelled");
            }
            batVisual.SetParent(Controller(_hand), false);
            ApplyVisualPose();
            ResetMotion(MotionResetReason.Manual);
        }

        void UpdateGhost()
        {
            if (fitGhost == null) return;
            fitGhost.localPosition = _fitController.position;
            fitGhost.localRotation = _fitController.rotation;
        }

        /// <summary>Adjusts the current hand's grip by whole steps on each axis (angles wrap). Persists locally.</summary>
        public void NudgeAlignment(int tiltSteps, int faceSteps, int leanSteps = 0, int slideSteps = 0, int spinSteps = 0)
        {
            var s = config.bat;
            var a = _alignment[Slot];
            a.tilt = Mathf.DeltaAngle(0f, a.tilt + tiltSteps * s.alignmentStepDegrees);
            a.faceTurn = Mathf.DeltaAngle(0f, a.faceTurn + faceSteps * s.alignmentStepDegrees);
            a.lean = Mathf.DeltaAngle(0f, a.lean + leanSteps * s.alignmentStepDegrees);
            a.slide = Mathf.Clamp(a.slide + slideSteps * s.slideStep, -0.3f, 0.3f);
            a.spin = Mathf.DeltaAngle(0f, a.spin + spinSteps * s.alignmentStepDegrees);
            SetAlignment(a);
        }

        /// <summary>Grip-to-bat transform in the controller's local frame, including the owner's offsets.</summary>
        void LocalGrip(out Vector3 position, out Quaternion rotation)
        {
            var a = Alignment;
            if (_mode == BatMode.Strapped)
            {
                // Fine-tuning turns the bat about its grip point, as in the hand; the mount puts that point on the real bat.
                rotation = _mount.rotation * BatFrame.OffsetRotation(a.tilt, a.faceTurn, a.lean, a.faceFlipped, a.spin);
                position = _mount.position + rotation * (Vector3.up * a.slide);
                return;
            }
            rotation = BatFrame.GripRotation(_hand, a.tilt, a.faceTurn, a.lean, a.faceFlipped, a.spin);
            position = a.position + rotation * (Vector3.up * a.slide);
        }

        /// <summary>Swaps the hitting face and the back, keeping the bat's angle.</summary>
        public void FlipFace()
        {
            var a = _alignment[Slot];
            a.faceFlipped = !a.faceFlipped;
            SetAlignment(a);
        }

        /// <summary>Back to the default grip; in bat mode, back to the mount (the calibration) keeping the face flip.</summary>
        public void ResetAlignment()
        {
            if (_mode == BatMode.Strapped)
                SetAlignment(new GripAlignment { faceFlipped = _alignment[StrapSlot].faceFlipped });
            else
                SetAlignment(_hand == BatHand.Right ? config.bat.rightHandGrip : config.bat.leftHandGrip);
        }

        void SetAlignment(GripAlignment a)
        {
            _alignment[Slot] = a;
            SaveAlignment(_mode == BatMode.Strapped ? StrapPrefsPrefix : PrefsPrefix + _hand, a);
            ApplyVisualPose();
            // Changing the grip offset moves the bat instantly: that is not a swing.
            ResetMotion(MotionResetReason.Manual);
            Debug.Log($"[CricketVR] {(_mode == BatMode.Strapped ? "Strapped bat" : _hand + " grip")} alignment: tilt {a.tilt:F0}, face {a.faceTurn:F0}, lean {a.lean:F0}, spin {a.spin:F0} deg, slide {a.slide * 100f:F0} cm, face flipped {a.faceFlipped}");
        }

        public void ResetMotion(MotionResetReason reason)
        {
            _tracker.Reset(reason, Time.unscaledTimeAsDouble);
            NotifyReset(reason);
        }

        void ApplyHand(BatHand hand, MotionResetReason reason)
        {
            _hand = hand;
            batVisual.SetParent(Controller(hand), false);
            ApplyVisualPose();
            ResetMotion(reason);
        }

        void ApplyVisualPose()
        {
            // The visual is a child of the controller, so it follows the late (onBeforeRender) pose update.
            if (Fitting) return; // frozen in front of the player until EndFit
            LocalGrip(out var position, out var rotation);
            batVisual.localPosition = position;
            batVisual.localRotation = rotation;
        }

        void NotifyReset(MotionResetReason reason)
        {
            _inSwing = false;
            _stepContinuous = false; // every reset path: never sweep the bat across a discontinuity
            Debug.Log($"[CricketVR] Bat motion reset: {reason}; contacts suppressed for {config.bat.motionLimits.suppressAfterReset:F2} s");
            MotionReset?.Invoke(reason);
        }

        void UpdateSwingLog()
        {
            if (!config.bat.logSwings || !_tracker.TryGetVelocity(out var linear, out var angular))
                return;
            var toe = GripPose.position + GripPose.rotation * Geometry.Toe;
            float toeSpeed = PoseHistory.PointVelocity(linear, angular, _tracker.History.Latest.Position, toe).magnitude;

            if (!_inSwing && toeSpeed > 3f)
            {
                _inSwing = true;
                _peakToeSpeed = 0f;
                _peakOmega = 0f;
            }
            if (_inSwing)
            {
                _peakToeSpeed = Mathf.Max(_peakToeSpeed, toeSpeed);
                _peakOmega = Mathf.Max(_peakOmega, angular.magnitude);
                if (toeSpeed < 1f)
                {
                    _inSwing = false;
                    Debug.Log($"[CricketVR] Swing ({_hand} hand): peak toe {_peakToeSpeed:F1} m/s, peak angular {_peakOmega:F1} rad/s, contact allowed {ContactAllowed}");
                }
            }
        }

        void UpdateTint()
        {
            if (bladeRenderer == null || !config.bat.showSuppressionTint)
                return;
            bool tint = !ContactAllowed;
            if (tint == _tinted)
                return;
            _tinted = tint;
            bladeRenderer.GetPropertyBlock(_tintBlock);
            _tintBlock.SetColor(BaseColorId, tint ? SuppressedTint : _bladeColor);
            bladeRenderer.SetPropertyBlock(_tintBlock);
        }

        static GripAlignment LoadAlignment(string key, GripAlignment fallback)
        {
            fallback.tilt = PlayerPrefs.GetFloat(key + ".tilt", fallback.tilt);
            fallback.faceTurn = PlayerPrefs.GetFloat(key + ".face", fallback.faceTurn);
            fallback.lean = PlayerPrefs.GetFloat(key + ".lean", fallback.lean);
            fallback.slide = PlayerPrefs.GetFloat(key + ".slide", fallback.slide);
            fallback.spin = PlayerPrefs.GetFloat(key + ".spin", fallback.spin);
            fallback.faceFlipped = PlayerPrefs.GetInt(key + ".flipped", fallback.faceFlipped ? 1 : 0) != 0;
            return fallback;
        }

        static void SaveAlignment(string key, GripAlignment a)
        {
            PlayerPrefs.SetFloat(key + ".tilt", a.tilt);
            PlayerPrefs.SetFloat(key + ".face", a.faceTurn);
            PlayerPrefs.SetFloat(key + ".lean", a.lean);
            PlayerPrefs.SetFloat(key + ".slide", a.slide);
            PlayerPrefs.SetInt(key + ".flipped", a.faceFlipped ? 1 : 0);
            PlayerPrefs.SetFloat(key + ".spin", a.spin);
            PlayerPrefs.Save();
        }

        static Pose LoadMount(Pose fallback, out bool fitted)
        {
            fitted = PlayerPrefs.GetInt(MountPrefsPrefix + "fitted", 0) != 0;
            if (!fitted)
                return fallback;
            float F(string k) => PlayerPrefs.GetFloat(MountPrefsPrefix + k, 0f);
            var q = new Quaternion(F("qx"), F("qy"), F("qz"), F("qw"));
            if (q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w < 0.5f)
            {
                fitted = false;
                return fallback;
            }
            return new Pose(new Vector3(F("px"), F("py"), F("pz")), Quaternion.Normalize(q));
        }

        static void SaveMount(Pose mount, bool fitted)
        {
            void F(string k, float v) => PlayerPrefs.SetFloat(MountPrefsPrefix + k, v);
            F("px", mount.position.x); F("py", mount.position.y); F("pz", mount.position.z);
            F("qx", mount.rotation.x); F("qy", mount.rotation.y); F("qz", mount.rotation.z); F("qw", mount.rotation.w);
            PlayerPrefs.SetInt(MountPrefsPrefix + "fitted", fitted ? 1 : 0);
            PlayerPrefs.Save();
        }

        public void ConfigureFit(Transform ghost) => fitGhost = ghost;

        /// <summary>Wires references from code (scene builder and tests).</summary>
        public void Configure(GameConfig cfg, Transform left, Transform right, Transform visual, Renderer blade,
            Rigidbody proxy, BoxCollider collider, bool requireTracked = true)
        {
            config = cfg;
            leftController = left;
            rightController = right;
            batVisual = visual;
            bladeRenderer = blade;
            physicsProxy = proxy;
            proxyCollider = collider;
            requireTrackedState = requireTracked;
        }
    }
}
