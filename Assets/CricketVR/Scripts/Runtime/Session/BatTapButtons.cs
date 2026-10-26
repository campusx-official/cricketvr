using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.UI;

namespace CricketVR
{
    /// <summary>
    /// Fallback for the menu ray (REQ-025): touch a button with the bat's blade to press it. A distance test of
    /// points along the blade's centre line against each button's rectangle; no colliders (the bat proxy still
    /// collides with nothing). A press needs the blade to leave the panel (or 0.6 s) before the next one.
    /// </summary>
    public class BatTapButtons : MonoBehaviour
    {
        [SerializeField] BatController bat;
        [SerializeField] SessionUI ui;
        [Tooltip("A blade point within this distance of a button's plane counts as touching it (m).")]
        public float touchDistance = 0.03f;
        [Tooltip("The blade must move this far from the panel before another press (m).")]
        public float releaseDistance = 0.06f;
        public float cooldownSeconds = 0.6f;

        const int Samples = 7;
        readonly Vector3[] _corners = new Vector3[4];
        readonly Vector3[] _points = new Vector3[Samples];
        bool _armed = true;
        float _lastPress = float.NegativeInfinity;

        void Update()
        {
            if (bat == null || ui == null || !ui.NearPanelVisible || ui.Buttons == null)
            {
                _armed = true;
                return;
            }
            var g = bat.Geometry;
            var pose = bat.GripPose;
            for (int i = 0; i < Samples; i++)
            {
                // Toe to shoulder along the blade's centre line.
                float t = (float)i / (Samples - 1);
                var local = new Vector3(0f, g.Toe.y + t * g.bladeLength, 0f);
                _points[i] = pose.position + pose.rotation * local;
            }

            float nearest = float.PositiveInfinity;
            Button touched = null;
            foreach (var b in ui.Buttons)
            {
                if (b == null || !b.gameObject.activeInHierarchy || !b.interactable)
                    continue;
                ((RectTransform)b.transform).GetWorldCorners(_corners);
                var origin = _corners[0];
                var right = _corners[3] - _corners[0];
                var up = _corners[1] - _corners[0];
                var normal = Vector3.Cross(right, up).normalized;
                foreach (var p in _points)
                {
                    var d = p - origin;
                    float plane = Mathf.Abs(Vector3.Dot(d, normal));
                    nearest = Mathf.Min(nearest, plane);
                    float u = Vector3.Dot(d, right) / right.sqrMagnitude;
                    float v = Vector3.Dot(d, up) / up.sqrMagnitude;
                    if (plane <= touchDistance && u >= 0f && u <= 1f && v >= 0f && v <= 1f)
                        touched = b;
                }
            }

            if (touched != null && _armed && Time.unscaledTime - _lastPress >= cooldownSeconds)
            {
                _armed = false;
                _lastPress = Time.unscaledTime;
                Pulse();
                touched.onClick.Invoke();
            }
            else if (touched == null && nearest > releaseDistance)
            {
                _armed = true;
            }
        }

        void Pulse()
        {
            var usage = bat.Hand == BatHand.Right ? UnityEngine.InputSystem.CommonUsages.RightHand : UnityEngine.InputSystem.CommonUsages.LeftHand;
            if (InputSystem.GetDevice<XRController>(usage) is XRControllerWithRumble c)
                c.SendImpulse(0.4f, 0.04f);
        }

        public void Configure(BatController batController, SessionUI sessionUi)
        {
            bat = batController;
            ui = sessionUi;
        }
    }
}
