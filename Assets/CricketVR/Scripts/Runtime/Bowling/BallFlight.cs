using UnityEngine;

namespace CricketVR
{
    /// <summary>
    /// Delivery-type flight on the ball (M7.5, plan.md §19). Before the ball pitches (and unless it is hit first) it
    /// applies the solver's <see cref="FlightPlan"/> through <see cref="DeliveryFlightModel.Acceleration"/>, one step
    /// at a time exactly as the solver integrated it. Stock has a still plan: no force at all, so its physics is
    /// unchanged. Also measures the ball's swing, curve and arrival line for the per-ball record, and drives the seam
    /// visual.
    /// Runs before the contact resolver (-40); the force is added with AddForce, so the resolver's view of the
    /// step's velocity is unchanged by it.
    /// </summary>
    [DefaultExecutionOrder(-42)]
    [RequireComponent(typeof(BallController))]
    public class BallFlight : MonoBehaviour
    {
        [Tooltip("Child that carries the seam mesh; rotated here, independent of the physics body's rotation.")]
        [SerializeField] Transform visual;

        const int MaxPathSamples = 2048; // 14 s at 144 Hz, more than the ball's timeout

        BallController _ball;
        FlightPlan _plan;
        bool _forcing, _measuring, _pitched;
        int _step;
        Vector3 _release, _previous;
        float _releaseLineX;
        readonly Vector2[] _path = new Vector2[MaxPathSamples];
        int _pathCount;

        // Visual
        Vector3 _spinAxis = Vector3.right;
        Quaternion _seamFrame = Quaternion.identity;
        float _revPerSecond, _angle;

        public int DeliveryId { get; private set; }
        /// <summary>Largest horizontal distance from the release-to-pitch-point line (m); NaN until it pitches.</summary>
        public float MeasuredCurve { get; private set; } = float.NaN;
        /// <summary>Ball x where it reached the striker's popping crease (m); NaN if it was hit or stopped first.</summary>
        public float ArrivalLine { get; private set; } = float.NaN;
        /// <summary>
        /// Swing (m): distance at the popping crease between the ball and its release line (the launch velocity's
        /// horizontal direction from the release point); NaN until it reaches the crease unhit.
        /// </summary>
        public float MeasuredSwing => float.IsNaN(ArrivalLine) ? float.NaN : Mathf.Abs(ArrivalLine - _releaseLineX);

        void Awake()
        {
            _ball = GetComponent<BallController>();
        }

        void OnEnable() => _ball.Pitched += OnPitched;
        void OnDisable() => _ball.Pitched -= OnPitched;

        /// <summary>
        /// Starts the flight of a delivery the ball has just launched. Call after <see cref="BallController.Launch"/>
        /// and outside FixedUpdate, so the next physics step is step 0 of the solver's flight.
        /// </summary>
        public void Begin(int deliveryId, Vector3 release, Vector3 launchVelocity, in FlightPlan plan, in DeliveryTypeData type)
        {
            DeliveryId = deliveryId;
            _plan = plan;
            _forcing = !plan.IsStill;
            _measuring = true;
            _pitched = false;
            _step = 0;
            _release = release;
            _previous = release;
            _releaseLineX = Mathf.Abs(launchVelocity.z) > 1e-6f
                ? release.x + launchVelocity.x * (FieldDimensions.PoppingCreaseOffset - release.z) / launchVelocity.z
                : release.x;
            _pathCount = 0;
            MeasuredCurve = float.NaN;
            ArrivalLine = float.NaN;

            _revPerSecond = Mathf.Min(type.visualRevPerSecond, DeliveryTypeData.MaxVisualRevPerSecond);
            _spinAxis = BallSpinVisual.PaceSpinAxis(launchVelocity, plan.IsStill ? 0 : plan.SwingDirection);
            _seamFrame = Quaternion.FromToRotation(Vector3.up, _spinAxis);
            _angle = 0f;
            UpdateVisual();
        }

        void FixedUpdate()
        {
            if (!_ball.InFlight || _ball.ActiveDeliveryId != DeliveryId)
            {
                _forcing = _measuring = false;
                return;
            }

            if (_measuring)
                Measure(_ball.Body.position);

            if (_forcing)
            {
                // Movement acts only before the bounce, and never on a hit ball. The solver's flight takes its last
                // acceleration in the step that reaches the pitch point (step·dt ≤ T); PhysX often reports the ground a
                // step later, and one more step of swing would add ~1 cm at the crease, so stop on the solver's count.
                if (_ball.HasBeenHit || _ball.TouchedGround || _step * Time.fixedDeltaTime > _plan.PlannedTime)
                {
                    _forcing = false;
                }
                else
                {
                    var a = DeliveryFlightModel.Acceleration(_step * Time.fixedDeltaTime, _ball.Body.linearVelocity, _plan);
                    _ball.Body.AddForce(a, ForceMode.Acceleration);
                    _step++;
                }
            }
        }

        void Measure(Vector3 position)
        {
            if (_ball.HasBeenHit)
            {
                _measuring = false;
                return;
            }
            if (!_pitched && _pathCount < MaxPathSamples)
                _path[_pathCount++] = new Vector2(position.x, position.z);

            // Arrival line: the step that crosses the striker's popping crease.
            float crease = FieldDimensions.PoppingCreaseOffset;
            if (_previous.z > crease && position.z <= crease)
            {
                float f = (_previous.z - crease) / (_previous.z - position.z);
                ArrivalLine = Mathf.Lerp(_previous.x, position.x, f);
                if (_pitched) _measuring = false;
            }
            _previous = position;
        }

        void OnPitched(int id, PitchMeasurement pitch, float time)
        {
            if (id != DeliveryId || _pitched)
                return;
            _pitched = true;
            var from = new Vector2(_release.x, _release.z);
            var chord = new Vector2(pitch.Point.x, pitch.Point.z) - from;
            float length = chord.magnitude;
            if (length < 1e-4f)
                return;
            var along = chord / length;
            var normal = new Vector2(-along.y, along.x);
            float max = 0f;
            for (int i = 0; i < _pathCount; i++)
            {
                var d = _path[i] - from;
                if (Vector2.Dot(d, along) <= length) // samples past the pitch point (the ball sank in) don't count
                    max = Mathf.Max(max, Mathf.Abs(Vector2.Dot(d, normal)));
            }
            MeasuredCurve = max;
            if (!float.IsNaN(ArrivalLine)) _measuring = false;
        }

        void LateUpdate()
        {
            if (visual == null || !_ball.InFlight)
                return;
            _angle = Mathf.Repeat(_angle + BallSpinVisual.DegreesThisFrame(_revPerSecond, Time.deltaTime), 360f);
            UpdateVisual();
        }

        void UpdateVisual()
        {
            if (visual != null)
                visual.rotation = Quaternion.AngleAxis(_angle, _spinAxis) * _seamFrame;
        }

        public void Configure(Transform seamVisual) => visual = seamVisual;
    }
}
