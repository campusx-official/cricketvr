using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;

namespace CricketVR
{
    /// <summary>
    /// Temporary in-headset setup controls until the Start panel exists (M7). Works from either controller:
    ///   hold A/X 1 s       toggle handedness (stance side)
    ///   hold B/Y 1 s       move the bat to the other controller
    ///   thumbstick up/down               tilt (about the blade's width)
    ///   thumbstick left/right            spin the bat clockwise/anticlockwise about its own handle
    ///   hold trigger + left/right        lean sideways (about the face normal)
    ///   hold grip + up/down              slide the bat along its length in the hand
    ///   hold grip + left/right           face turn (swings the direction the tilt points)
    ///   thumbstick click                 tap: reset alignment; hold 1 s: flip the face 180 degrees
    /// Holding the stick repeats the step. Angles step by GameConfig.bat.alignmentStepDegrees.
    /// Each action gives a short vibration on the controller that triggered it.
    /// While fitting bat mode (<see cref="BatController.Fitting"/>, the virtual bat frozen with a ghost controller on it),
    /// in two modes switched with a tap of the stick click (holding the stick or A/B repeats the step):
    ///   MOVE  B / A: up / down the bat;  stick left/right: across the blade;  stick up/down: away from / onto the blade
    ///   TURN  B / A: roll about its own length;  stick left/right: turn on the bat's surface;  stick up/down: tilt
    ///   hold stick click 1 s: OK (save the fit)
    /// </summary>
    public class BatSetupControls : MonoBehaviour
    {
        [SerializeField] BatController bat;
        [SerializeField] HandednessSetup handedness;
        [Tooltip("Disabled during a live session (M7).")]
        public bool allowSetupInput = true;

        /// <summary>Raised when the owner confirms the bat-mode fit (the fit is already saved).</summary>
        public event System.Action FitConfirmed;
        /// <summary>Raised when the fit switches between moving and turning the ghost controller.</summary>
        public event System.Action FitModeChanged;
        /// <summary>While fitting: the stick and A/B turn the ghost controller (else they move it).</summary>
        public bool FitTurning { get; private set; }

        const float HoldSeconds = 1f;
        const float StickThreshold = 0.7f;
        const float StickRepeatSeconds = 0.15f;

        InputAction _primary, _secondary, _stick, _stickClick, _trigger, _grip;
        float _primaryHeld, _secondaryHeld, _clickHeld, _nextStickStep, _nextButtonStep;
        bool _primaryFired, _secondaryFired, _clickFired;

        void OnEnable()
        {
            // Usage-based bindings without a hand filter: whichever controller is working can be used.
            _primary = new InputAction("Primary", InputActionType.Button, "<XRController>/{PrimaryButton}");
            _secondary = new InputAction("Secondary", InputActionType.Button, "<XRController>/{SecondaryButton}");
            _stick = new InputAction("Stick", InputActionType.Value, "<XRController>/{Primary2DAxis}");
            _stickClick = new InputAction("StickClick", InputActionType.Button, "<XRController>/{Primary2DAxisClick}");
            _trigger = new InputAction("Trigger", InputActionType.Button, "<XRController>/{TriggerButton}");
            _grip = new InputAction("Grip", InputActionType.Button, "<XRController>/{GripButton}");
            _trigger.Enable();
            _grip.Enable();
            _primary.Enable();
            _secondary.Enable();
            _stick.Enable();
            _stickClick.Enable();
        }

        void OnDisable()
        {
            _primary?.Dispose();
            _secondary?.Dispose();
            _stick?.Dispose();
            _stickClick?.Dispose();
            _trigger?.Dispose();
            _grip?.Dispose();
        }

        void Update()
        {
            if (!allowSetupInput || bat == null || handedness == null)
                return;
            if (bat.Fitting)
            {
                UpdateFit();
                return;
            }
            FitTurning = false; // every fit starts in MOVE

            if (HoldFired(_primary, ref _primaryHeld, ref _primaryFired))
            {
                handedness.Toggle();
                Buzz(_primary, 0.5f, 0.15f);
            }

            if (HoldFired(_secondary, ref _secondaryHeld, ref _secondaryFired))
            {
                var other = bat.Hand == BatHand.Right ? BatHand.Left : BatHand.Right;
                if (bat.IsControllerTracked(other))
                {
                    bat.SetBatHand(other);
                    Buzz(_secondary, 0.5f, 0.15f);
                }
                else
                {
                    Debug.LogWarning($"[CricketVR] {other} controller isn't tracking; bat stays in the {bat.Hand} hand.");
                    Buzz(_secondary, 0.2f, 0.4f);
                }
            }

            // Tap = reset (on release, so a hold doesn't also reset); hold = flip the face.
            // Read the flag first: HoldFired clears it on the release frame.
            bool clickWasHeld = _clickFired;
            if (HoldFired(_stickClick, ref _clickHeld, ref _clickFired))
            {
                bat.FlipFace();
                Buzz(_stickClick, 0.5f, 0.15f);
            }
            if (_stickClick.WasReleasedThisFrame() && !clickWasHeld)
            {
                bat.ResetAlignment();
                Buzz(_stickClick, 0.4f, 0.1f);
            }

            var stick = _stick.ReadValue<Vector2>();
            bool deflected = Mathf.Abs(stick.x) > StickThreshold || Mathf.Abs(stick.y) > StickThreshold;
            if (!deflected)
            {
                _nextStickStep = 0f;
            }
            else if (Time.unscaledTime >= _nextStickStep)
            {
                _nextStickStep = Time.unscaledTime + StickRepeatSeconds;
                bool vertical = Mathf.Abs(stick.y) >= Mathf.Abs(stick.x);
                int sign = (int)Mathf.Sign(vertical ? stick.y : stick.x);
                if (_grip.IsPressed())
                {
                    if (vertical) bat.NudgeAlignment(0, 0, 0, sign);
                    else bat.NudgeAlignment(0, sign);
                }
                else if (_trigger.IsPressed())
                {
                    if (!vertical) bat.NudgeAlignment(0, 0, sign, 0);
                }
                else if (vertical)
                {
                    bat.NudgeAlignment(sign, 0);
                }
                else
                {
                    bat.NudgeAlignment(0, 0, spinSteps: sign);
                }
                Buzz(_stick, 0.15f, 0.03f);
            }
        }

        void UpdateFit()
        {
            // A and B: once on the press, then repeating while held.
            int buttons = (_secondary.IsPressed() ? 1 : 0) - (_primary.IsPressed() ? 1 : 0);
            if (_secondary.WasPressedThisFrame() || _primary.WasPressedThisFrame())
                _nextButtonStep = 0f;
            if (buttons != 0 && Time.unscaledTime >= _nextButtonStep)
            {
                _nextButtonStep = Time.unscaledTime + (_nextButtonStep == 0f ? 0.4f : StickRepeatSeconds);
                if (FitTurning) bat.FitRoll(buttons);
                else bat.FitMove(buttons);
                Buzz(buttons > 0 ? _secondary : _primary, 0.15f, 0.03f);
            }
            // Never let a hold started while fitting fire handedness or the hand swap afterwards.
            _primaryFired = _secondaryFired = true;

            bool clickWasHeld = _clickFired;
            if (HoldFired(_stickClick, ref _clickHeld, ref _clickFired))
            {
                bat.EndFit(true);
                FitTurning = false;
                Buzz(_stickClick, 0.6f, 0.25f);
                FitConfirmed?.Invoke();
                return;
            }
            if (_stickClick.WasReleasedThisFrame() && !clickWasHeld)
            {
                FitTurning = !FitTurning;
                Buzz(_stickClick, 0.4f, 0.08f);
                FitModeChanged?.Invoke();
            }

            var stick = _stick.ReadValue<Vector2>();
            if (Mathf.Abs(stick.x) <= StickThreshold && Mathf.Abs(stick.y) <= StickThreshold)
            {
                _nextStickStep = 0f;
            }
            else if (Time.unscaledTime >= _nextStickStep)
            {
                _nextStickStep = Time.unscaledTime + StickRepeatSeconds;
                bool vertical = Mathf.Abs(stick.y) >= Mathf.Abs(stick.x);
                int sign = (int)Mathf.Sign(vertical ? stick.y : stick.x);
                if (!FitTurning)
                {
                    if (vertical) bat.FitMove(0, outward: sign);
                    else bat.FitMove(0, across: sign);
                }
                else if (vertical)
                {
                    bat.FitTilt(sign);
                }
                else
                {
                    // Stick right turns it clockwise as you look at it: the frozen bat shows you the controller's side.
                    bat.FitTurn(sign * (bat.HitsWithControllerSide ? -1 : 1));
                }
                Buzz(_stick, 0.15f, 0.03f);
            }
        }

        static bool HoldFired(InputAction action, ref float held, ref bool fired)
        {
            if (!action.IsPressed())
            {
                held = 0f;
                fired = false;
                return false;
            }
            held += Time.unscaledDeltaTime;
            if (fired || held < HoldSeconds)
                return false;
            fired = true;
            return true;
        }

        static void Buzz(InputAction action, float amplitude, float seconds)
        {
            if (action.activeControl?.device is XRControllerWithRumble controller)
                controller.SendImpulse(amplitude, seconds);
        }

        public void Configure(BatController batController, HandednessSetup handednessSetup)
        {
            bat = batController;
            handedness = handednessSetup;
        }
    }
}
