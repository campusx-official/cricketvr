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

        public DeliveryConfig LastConfig { get; private set; }
        public DeliverySolution LastSolution { get; private set; }
        /// <summary>Measured first bounce of the latest delivery, if it has bounced.</summary>
        public Vector3? LastBounce { get; private set; }
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

        /// <summary>Bowls one delivery. Returns the delivery id, or 0 if the request was rejected.</summary>
        public int Bowl(DeliveryConfig request)
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

        // Where the ball pitched (T070), not the PhysX contact report, which lands 6-10 cm past it.
        void OnPitched(int id, Vector3 point, float flightTime)
        {
            if (id != ball.ActiveDeliveryId)
                return; // stale
            LastBounce = point;
            LastFlightTime = flightTime;
            var target = LastSolution.Target;
            float error = Vector2.Distance(new Vector2(point.x, point.z), new Vector2(target.x, target.z));

            if (bounceMarker != null && config.delivery.showDebugMarkers)
            {
                bounceMarker.gameObject.SetActive(true);
                bounceMarker.position = new Vector3(point.x, 0.006f, point.z);
            }
            if (config.delivery.logDeliveries)
                Debug.Log($"[CricketVR] Delivery #{id} bounced at ({point.x:F3}, {point.z:F3}) after {flightTime:F3} s; " +
                          $"{error * 100f:F1} cm from target");
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
