using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;

namespace CricketVR
{
    /// <summary>
    /// Temporary in-headset setup controls until the Start panel exists (M7). Works from either controller:
    ///   hold A/X 1 s       toggle handedness (stance side)
    ///   hold B/Y 1 s       move the bat to the other controller; in bat mode, calibrate the strapped bat instead
    ///   thumbstick up/down               tilt (about the blade's width)
    ///   thumbstick left/right            spin the bat clockwise/anticlockwise about its own handle
    ///   hold trigger + left/right        lean sideways (about the face normal)
    ///   hold grip + up/down              slide the bat along its length in the hand
    ///   hold grip + left/right           face turn (swings the direction the tilt points)
    ///   thumbstick click                 tap: reset alignment; hold 1 s: flip the face 180 degrees
    /// Holding the stick repeats the step. Angles step by GameConfig.bat.alignmentStepDegrees.
    /// Each action gives a short vibration on the controller that triggered it.
    /// </summary>
    public class BatSetupControls : MonoBehaviour
    {
        [SerializeField] BatController bat;
        [SerializeField] HandednessSetup handedness;
        [SerializeField] BatMountCalibrator calibrator;
        [Tooltip("Disabled during a live session (M7).")]
        public bool allowSetupInput = true;

        const float HoldSeconds = 1f;
        const float StickThreshold = 0.7f;
        const float StickRepeatSeconds = 0.15f;

        InputAction _primary, _secondary, _stick, _stickClick, _trigger, _grip;
        float _primaryHeld, _secondaryHeld, _clickHeld, _nextStickStep;
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
            if (calibrator != null && calibrator.Active)
            {
                // The calibration owns A and B until it ends; a hold already in progress must not fire afterwards.
                _primaryFired = _secondaryFired = true;
                return;
            }

            if (HoldFired(_primary, ref _primaryHeld, ref _primaryFired))
            {
                handedness.Toggle();
                Buzz(_primary, 0.5f, 0.15f);
            }

            if (HoldFired(_secondary, ref _secondaryHeld, ref _secondaryFired))
            {
                if (bat.Mode == BatMode.Strapped)
                {
                    // Bat mode has no other controller to move to: B measures the strapped bat instead.
                    if (calibrator != null)
                    {
                        calibrator.Begin();
                        Buzz(_secondary, 0.5f, 0.15f);
                    }
                }
                else
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

        public void Configure(BatController batController, HandednessSetup handednessSetup, BatMountCalibrator mountCalibrator = null)
        {
            bat = batController;
            handedness = handednessSetup;
            calibrator = mountCalibrator;
        }
    }
}
