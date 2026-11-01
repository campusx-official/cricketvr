using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;

namespace CricketVR
{
    /// <summary>
    /// Bat-mode calibration (REQ-054, plan.md §21): measures where the strapped controller sits on the real bat, in three
    /// steps, each started with A on the bat's controller (B cancels):
    ///   1. toe on the floor, rock the bat around it (pivot: the toe in the controller frame);
    ///   2. bat upside down, top of the handle on the floor, rock it (pivot: the handle top);
    ///   3. bat flat on the floor, controller side up, still (the face direction).
    /// A step that fails its quality check (too little rocking, the end moved, not on the floor, not still) says why and
    /// waits to be repeated; nothing changes until all three pass. The result goes to <see cref="BatController.SetMount"/>.
    /// </summary>
    public class BatMountCalibrator : MonoBehaviour
    {
        [SerializeField] GameConfig config;
        [SerializeField] BatController bat;

        public enum Step { Idle, ToeReady, Toe, TopReady, Top, FlatReady, Flat }

        const float MaxFlatWobble = 3f; // degrees the flat bat may move while it is measured

        readonly List<Pose> _samples = new List<Pose>(1024);
        InputAction _next, _cancel;
        Vector3 _toe, _top, _upSum;
        int _upCount;
        float _stepEnd;
        string _problem = "";

        public Step Current { get; private set; } = Step.Idle;
        public bool Active => Current != Step.Idle;
        /// <summary>The last finished calibration's summary, or the reason it was cancelled.</summary>
        public string LastResult { get; private set; } = "";
        /// <summary>Raised whenever the instructions change.</summary>
        public event Action Changed;
        /// <summary>Off only for automated tests without an XR device.</summary>
        public bool requireTrackedState = true;

        void OnEnable()
        {
            _next = new InputAction("CalibrateNext", InputActionType.Button, "<XRController>/{PrimaryButton}");
            _cancel = new InputAction("CalibrateCancel", InputActionType.Button, "<XRController>/{SecondaryButton}");
            _next.Enable();
            _cancel.Enable();
        }

        void OnDisable()
        {
            _next?.Dispose();
            _cancel?.Dispose();
            if (Active) Cancel("interrupted");
        }

        public void Begin()
        {
            if (bat == null || config == null)
                return;
            _problem = "";
            Go(Step.ToeReady);
            Debug.Log("[CricketVR] Bat calibration started");
        }

        public void Cancel(string why = "cancelled")
        {
            if (!Active)
                return;
            LastResult = $"Calibration {why}; the bat mount is unchanged.";
            Debug.Log($"[CricketVR] Bat calibration {why}");
            Go(Step.Idle);
        }

        /// <summary>The A press: starts measuring in a ready step. Public for tests.</summary>
        public void Advance()
        {
            switch (Current)
            {
                case Step.ToeReady: StartMeasuring(Step.Toe, config.bat.pivotSeconds); break;
                case Step.TopReady: StartMeasuring(Step.Top, config.bat.pivotSeconds); break;
                case Step.FlatReady: StartMeasuring(Step.Flat, config.bat.flatSeconds); break;
            }
        }

        /// <summary>Adds one controller pose to the step being measured. Public for tests; Update feeds the tracked pose.</summary>
        public void AddSample(Pose controller)
        {
            if (Current == Step.Toe || Current == Step.Top)
            {
                _samples.Add(controller);
            }
            else if (Current == Step.Flat)
            {
                _samples.Add(controller);
                _upSum += Quaternion.Inverse(controller.rotation) * Vector3.up;
                _upCount++;
            }
        }

        /// <summary>Ends the step being measured now and evaluates it. Public for tests; Update calls it when the time is up.</summary>
        public void FinishStep()
        {
            switch (Current)
            {
                case Step.Toe:
                    if (TryPivot("toe", out _toe)) Go(Step.TopReady); else Go(Step.ToeReady);
                    break;
                case Step.Top:
                    if (TryPivot("handle top", out _top)) Go(Step.FlatReady); else Go(Step.TopReady);
                    break;
                case Step.Flat:
                    Finish();
                    break;
            }
        }

        void Update()
        {
            if (!Active)
                return;
            if (_cancel.WasPressedThisFrame())
            {
                Cancel();
                return;
            }
            if (_next.WasPressedThisFrame())
                Advance();
            if (Current == Step.Toe || Current == Step.Top || Current == Step.Flat)
            {
                var c = bat.StrappedController;
                if (!requireTrackedState || bat.IsControllerTracked(config.bat.strappedController))
                    AddSample(new Pose(c.position, c.rotation));
                if (Time.unscaledTime >= _stepEnd)
                    FinishStep();
            }
        }

        void StartMeasuring(Step step, float seconds)
        {
            _samples.Clear();
            _upSum = Vector3.zero;
            _upCount = 0;
            _problem = "";
            _stepEnd = Time.unscaledTime + seconds;
            Buzz(0.3f, 0.05f);
            Go(step);
        }

        bool TryPivot(string end, out Vector3 local)
        {
            local = default;
            float spread = BatMount.RotationSpread(_samples);
            if (_samples.Count < 10 || spread < BatMount.MinPivotSpread)
                return Fail($"The bat was only rocked {spread:F0}°. Rock it further (about 30° each way), keeping the {end} still.");
            if (!BatMount.SolvePivot(_samples, out local, out var world, out float rms))
                return Fail($"Couldn't find the {end}: rock the bat in more than one direction.");
            if (rms > BatMount.MaxPivotRms)
                return Fail($"The {end} moved {rms * 100f:F1} cm while rocking. Keep it still on one spot.");
            if (Mathf.Abs(world.y) > BatMount.MaxFloorError)
                return Fail($"The {end} was {world.y * 100f:F0} cm from the floor. Put it on the floor (or redo the headset's floor setup).");
            Debug.Log($"[CricketVR] Bat calibration: {end} at {local * 100f} cm in the controller frame; " +
                      $"rocked {spread:F0} deg, RMS {rms * 1000f:F1} mm, {_samples.Count} samples, pivot height {world.y * 100f:F1} cm");
            Buzz(0.5f, 0.12f);
            return true;
        }

        void Finish()
        {
            float wobble = BatMount.RotationSpread(_samples);
            if (_upCount < 5 || wobble > MaxFlatWobble)
            {
                Fail(_upCount < 5 ? "The controller wasn't tracked. Keep it in view of the headset." :
                    $"The bat moved {wobble:F0}° while lying flat. Let go of it and keep it still.");
                Go(Step.FlatReady);
                return;
            }
            float length = (_top - _toe).magnitude;
            var up = (_upSum / _upCount).normalized;
            if (!BatMount.TryFromCalibration(_toe, _top, up, config.bat.controllerOnFace, bat.Geometry, out var mount, out var problem))
            {
                Fail(problem);
                // A wrong length means a bad pivot: start again from the toe.
                bool lengthWrong = Mathf.Abs(length - BatMount.ModelLength(bat.Geometry)) > BatMount.LengthTolerance;
                Go(lengthWrong ? Step.ToeReady : Step.FlatReady);
                return;
            }
            bat.SetMount(mount, calibrated: true);
            LastResult = $"Calibrated. Toe to handle top {length * 100f:F0} cm (model bat {BatMount.ModelLength(bat.Geometry) * 100f:F0} cm).\n" +
                         "Check: put the toe on the floor; the virtual toe should touch the virtual floor.";
            Debug.Log($"[CricketVR] Bat calibration done: measured length {length * 100f:F1} cm");
            Buzz(0.7f, 0.25f);
            Go(Step.Idle);
        }

        bool Fail(string problem)
        {
            _problem = problem;
            Debug.LogWarning($"[CricketVR] Bat calibration step failed: {problem}");
            Buzz(0.2f, 0.5f);
            return false;
        }

        void Go(Step step)
        {
            Current = step;
            Changed?.Invoke();
        }

        /// <summary>What the panel shows for the current step.</summary>
        public string Instructions
        {
            get
            {
                string again = string.IsNullOrEmpty(_problem) ? "" : $"<color=#ff9a70>{_problem}</color>\n\n";
                switch (Current)
                {
                    case Step.ToeReady:
                        return again + "<b>Step 1 of 3: the toe</b>\nStand the bat on its toe on the floor.\nPress A, then slowly rock the bat around in every direction, keeping the toe on one spot, for " +
                               $"{config.bat.pivotSeconds:F0} seconds (until the buzz).";
                    case Step.Toe:
                    case Step.Top:
                        return "<b>Measuring...</b>\nKeep rocking; keep the end on one spot.";
                    case Step.TopReady:
                        return again + "<b>Step 2 of 3: the handle</b>\nTurn the bat upside down: the top of the handle on the floor.\nPress A, then rock it the same way until the buzz.";
                    case Step.FlatReady:
                        return again + "<b>Step 3 of 3: the face</b>\nLay the bat flat on the floor, controller side up.\nPress A and let go; keep it still until the buzz.";
                    case Step.Flat:
                        return "<b>Measuring...</b>\nKeep the bat still.";
                    default:
                        return LastResult;
                }
            }
        }

        void Buzz(float amplitude, float seconds)
        {
            var usage = config.bat.strappedController == BatHand.Right
                ? UnityEngine.InputSystem.CommonUsages.RightHand
                : UnityEngine.InputSystem.CommonUsages.LeftHand;
            if (InputSystem.GetDevice<XRController>(usage) is XRControllerWithRumble c)
                c.SendImpulse(amplitude, seconds);
        }

        public void Configure(GameConfig cfg, BatController batController)
        {
            config = cfg;
            bat = batController;
        }
    }
}
