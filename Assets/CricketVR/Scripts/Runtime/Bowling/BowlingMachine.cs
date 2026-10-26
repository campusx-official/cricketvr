using System;
using UnityEngine;

namespace CricketVR
{
    /// <summary>
    /// Turns a <see cref="DeliveryConfig"/> into a launch: solves the release velocity, rejects unreachable requests
    /// (never changing the requested speed), and launches the single ball. Records the measured bounce for each delivery.
    /// </summary>
    public class BowlingMachine : MonoBehaviour
    {
        [SerializeField] GameConfig config;
        [SerializeField] BallController ball;
        [Tooltip("Visual launcher; moved to the configured release point.")]
        [SerializeField] Transform launcherAnchor;
        [SerializeField] Transform targetMarker;
        [SerializeField] Transform bounceMarker;

        int _nextDeliveryId = 1;

        /// <summary>World-frame launch fields of the latest delivery.</summary>
        public DeliveryConfig LastConfig { get; private set; }
        /// <summary>The latest request as received (batter frame, with its type, movement and seed).</summary>
        public DeliveryRequest LastRequest { get; private set; }
        public DeliverySolution LastSolution { get; private set; }
        /// <summary>Measured first bounce of the latest delivery, if it has bounced.</summary>
        public Vector3? LastBounce { get; private set; }
        /// <summary>Distance (m) from the latest measured bounce to the requested point; NaN before it bounces.</summary>
        public float LastBounceError { get; private set; } = float.NaN;
        /// <summary>Where PhysX started the latest rebound relative to the pitch point (m, + = past it); NaN before it bounces.</summary>
        public float LastReboundOffset { get; private set; } = float.NaN;
        public float LastFlightTime { get; private set; }
        public string LastError { get; private set; }

        /// <summary>(delivery id, config, solution) after a successful launch.</summary>
        public event Action<int, DeliveryConfig, DeliverySolution> Bowled;
        /// <summary>(delivery id, measured bounce point, error from target in metres).</summary>
        public event Action<int, Vector3, float> BounceMeasured;
        /// <summary>A request was rejected (unreachable or invalid).</summary>
        public event Action<DeliveryConfig, string> Rejected;

        void OnEnable()
        {
            if (ball != null)
                ball.Pitched += OnPitched;
        }

        void OnDisable()
        {
            if (ball != null)
                ball.Pitched -= OnPitched;
        }

        void Start()
        {
            SetMarkersVisible(false);
        }

        /// <summary>Bowls a Stock delivery from world-frame launch fields (pre-M7.5 callers and debug presets).</summary>
        public int Bowl(DeliveryConfig config) => Bowl(DeliveryRequest.FromConfig(config));

        /// <summary>
        /// Bowls one delivery (REQ-040). The request is mirrored for its batter here, once (DT-7). Returns the
        /// delivery id, or 0 if the request was rejected.
        /// </summary>
        public int Bowl(DeliveryRequest delivery)
        {
            LastRequest = delivery;
            var world = delivery.ForBatter();
            if (!world.type.IsStill || world.lineReference != LineReference.AtBounce)
                return Reject(world.ToConfig(), $"{world.type.displayName} deliveries are not available yet (M7.5 in progress)");
            return BowlStill(world.ToConfig());
        }

        int Reject(DeliveryConfig request, string error)
        {
            LastConfig = request;
            LastSolution = default;
            LastBounce = null;
            LastBounceError = float.NaN;
            LastReboundOffset = float.NaN;
            LastFlightTime = 0f;
            LastError = error;
            Debug.LogWarning($"[CricketVR] Delivery rejected ({request}): {error}");
            Rejected?.Invoke(request, error);
            return 0;
        }

        /// <summary>Stock path, unchanged since M4: no movement, so the launch is the gravity-only solution.</summary>
        int BowlStill(DeliveryConfig request)
        {
            if (config == null || ball == null)
            {
                Debug.LogError("[CricketVR] BowlingMachine is missing references; run CricketVR > Setup > 6. Build or Repair Main Scene.", this);
                return 0;
            }

            var settings = config.delivery;
            var solution = DeliverySolver.Solve(request, -Physics.gravity.y, Time.fixedDeltaTime, settings.ballRadius);
            LastConfig = request;
            LastSolution = solution;
            LastBounce = null;
            LastBounceError = float.NaN;
            LastReboundOffset = float.NaN;
            LastFlightTime = 0f;

            if (!solution.Reachable)
            {
                LastError = solution.Error;
                Debug.LogWarning($"[CricketVR] Delivery rejected ({request}): {solution.Error}");
                Rejected?.Invoke(request, solution.Error);
                return 0;
            }

            LastError = null;
            if (launcherAnchor != null)
                launcherAnchor.position = request.releasePosition;

            int id = _nextDeliveryId++;
            ball.Launch(id, request.releasePosition, solution.Velocity);

            if (targetMarker != null && settings.showDebugMarkers)
            {
                targetMarker.gameObject.SetActive(true);
                targetMarker.position = new Vector3(solution.Target.x, 0.005f, solution.Target.z);
            }
            if (bounceMarker != null)
                bounceMarker.gameObject.SetActive(false);

            if (settings.logDeliveries)
                Debug.Log($"[CricketVR] Delivery #{id} {request}: v0 {solution.Velocity.magnitude:F2} m/s " +
                          $"({Units.MsToKmh(solution.Velocity.magnitude):F1} km/h), elevation {solution.ElevationDegrees:F2} deg, " +
                          $"predicted bounce ({solution.PredictedBounce.x:F3}, {solution.PredictedBounce.z:F3})");
            Bowled?.Invoke(id, request, solution);
            return id;
        }

        // Where the ball pitched (T070), not the PhysX contact report, which scatters a few cm around it.
        void OnPitched(int id, PitchMeasurement pitch, float flightTime)
        {
            if (id != ball.ActiveDeliveryId)
                return; // stale
            var point = pitch.Point;
            LastBounce = point;
            LastReboundOffset = pitch.ReboundOffset;
            LastFlightTime = flightTime;
            var target = LastSolution.Target;
            float error = Vector2.Distance(new Vector2(point.x, point.z), new Vector2(target.x, target.z));
            LastBounceError = error;

            if (bounceMarker != null && config.delivery.showDebugMarkers)
            {
                bounceMarker.gameObject.SetActive(true);
                bounceMarker.position = new Vector3(point.x, 0.006f, point.z);
            }
            if (config.delivery.logDeliveries)
                Debug.Log($"[CricketVR] Delivery #{id} bounced at ({point.x:F3}, {point.z:F3}) after {flightTime:F3} s; " +
                          $"{error * 100f:F1} cm from target; rebound starts {pitch.ReboundOffset * 100f:+0.0;-0.0} cm along");
            BounceMeasured?.Invoke(id, point, error);
        }

        void SetMarkersVisible(bool visible)
        {
            if (targetMarker != null) targetMarker.gameObject.SetActive(visible);
            if (bounceMarker != null) bounceMarker.gameObject.SetActive(visible);
        }

        public void Configure(GameConfig cfg, BallController ballController, Transform launcher, Transform target, Transform bounce)
        {
            config = cfg;
            ball = ballController;
            launcherAnchor = launcher;
            targetMarker = target;
            bounceMarker = bounce;
        }
    }
}
