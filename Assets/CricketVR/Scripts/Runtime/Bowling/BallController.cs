using System;
using UnityEngine;

namespace CricketVR
{
    public enum BallEndReason
    {
        Timeout,
        Settled,
        Cancelled,
        /// <summary>The delivery's result was decided (<see cref="Finish"/>) and the dead ball's linger ended.</summary>
        Finalized,
        /// <summary>Removed at once by a safety check (left the playable volume); see <see cref="Despawn"/>.</summary>
        Despawned,
    }

    /// <summary>
    /// The single reusable ball. Owns its flight lifecycle: launch, contact reporting, timeout/settle, and a complete
    /// reset between deliveries. Every event carries the delivery ID so listeners can reject stale events.
    /// Ball flight is pure physics after release (no animation), with no air drag in the MVP.
    /// </summary>
    [RequireComponent(typeof(Rigidbody), typeof(SphereCollider))]
    public class BallController : MonoBehaviour
    {
        [SerializeField] GameConfig config;
        [SerializeField] TrailRenderer trail;
        [SerializeField] Renderer ballRenderer;

        Rigidbody _body;
        int _groundMask; // Pitch and Outfield both count as ground (GD-6)
        float _launchTime, _slowSince, _parkAt;
        bool _bounced, _dead;

        // Pitch measurement (T070): descending samples up to the step where the ball turns upward.
        const int DescentHistory = 4;
        const int MaxStepsWithoutRebound = 4;
        readonly Vector3[] _descent = new Vector3[DescentHistory];
        readonly float[] _descentTimes = new float[DescentHistory];
        int _descentCount, _stepsSinceGround;
        float _groundY;
        bool _pitched;

        public int ActiveDeliveryId { get; private set; }
        public bool InFlight { get; private set; }
        /// <summary>In flight and its result not yet decided: it can still be hit and still score.</summary>
        public bool IsLive => InFlight && !_dead;
        public Rigidbody Body => _body;
        public float Radius => config.delivery.ballRadius;

        /// <summary>First contact with the ground (pitch or outfield) for this delivery: (id, contact point, time since release).
        /// The contact point lies under the ball's sunk position at the end of the contact step, 6–10 cm past where it
        /// came down; use <see cref="Pitched"/> for where the ball bounced.</summary>
        public event Action<int, Vector3, float> FirstBounce;
        /// <summary>Where the first bounce happened: (id, ball centre where the incoming flight reached the ground,
        /// time since release). Raised a step or two after <see cref="FirstBounce"/>, once the ball turns upward.</summary>
        public event Action<int, Vector3, float> Pitched;
        /// <summary>Any collision during flight: (id, collision). Used by later milestones for stumps, nets and the bat.</summary>
        public event Action<int, Collision> Collided;
        public event Action<int, BallEndReason> DeliveryEnded;

        void Awake()
        {
            _body = GetComponent<Rigidbody>();
            _groundMask = LayerMask.GetMask("Pitch", "Outfield");
            if (config == null)
            {
                Debug.LogError("[CricketVR] BallController has no GameConfig; run CricketVR > Setup > 6. Build or Repair Main Scene.", this);
                enabled = false;
                return;
            }
            var settings = config.delivery;
            GetComponent<SphereCollider>().radius = 0.5f; // unit sphere mesh; size comes from the transform scale
            transform.localScale = Vector3.one * settings.ballRadius * 2f;
            _body.mass = settings.ballMass;
            _body.useGravity = true;
            _body.linearDamping = 0f; // no air drag in the MVP (plan.md §9)
            _body.angularDamping = 0.05f;
            _body.interpolation = RigidbodyInterpolation.Interpolate;
            _body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            Park(transform.position);
        }

        /// <summary>
        /// Starts a delivery. Any delivery already in flight is cancelled first, so only one ball is ever active.
        /// </summary>
        public void Launch(int deliveryId, Vector3 position, Vector3 velocity)
        {
            if (InFlight)
                End(BallEndReason.Cancelled);

            // Full reset: physics state, trail, flags, timers. Then release.
            _body.isKinematic = false;
            _body.position = position;
            transform.position = position;
            _body.linearVelocity = velocity;
            _body.angularVelocity = Vector3.zero;
            if (trail != null)
            {
                trail.Clear(); // otherwise a line is drawn from the previous ball position
                trail.emitting = true;
            }
            if (ballRenderer != null)
                ballRenderer.enabled = true;

            ActiveDeliveryId = deliveryId;
            InFlight = true;
            _bounced = false;
            _dead = false;
            _pitched = false;
            _descentCount = 0;
            _launchTime = Time.time;
            _slowSince = -1f;
        }

        /// <summary>
        /// Applies a bat strike decided by <see cref="BatBallContactResolver"/> (the only authority for bat response).
        /// Ignored for a stale delivery id.
        /// </summary>
        public bool ApplyBatHit(int deliveryId, Vector3 position, Vector3 velocity)
        {
            if (!IsLive || deliveryId != ActiveDeliveryId)
                return false;
            _body.position = position;
            _body.linearVelocity = velocity;
            _body.angularVelocity = Vector3.zero;
            _slowSince = -1f;
            _descentCount = 0; // the ball was moved: earlier samples are not part of its path any more
            return true;
        }

        /// <summary>
        /// The delivery's result has been decided: the ball becomes dead (no more hits, timeouts or settling) but
        /// stays visible and physical for <paramref name="linger"/> seconds so the player sees it meet the net or
        /// stumps, then it is parked. Ignored for a stale id.
        /// </summary>
        public bool Finish(int deliveryId, float linger)
        {
            if (InFlight && _dead && deliveryId == ActiveDeliveryId)
            {
                // Already decided: a later reason (e.g. crossing the rope) may only bring the hiding forward.
                _parkAt = Mathf.Min(_parkAt, Time.time + Mathf.Max(0f, linger));
                return false;
            }
            if (!IsLive || deliveryId != ActiveDeliveryId)
                return false;
            _dead = true;
            _parkAt = Time.time + Mathf.Max(0f, linger);
            return true;
        }

        /// <summary>
        /// Safety removal: parks the ball at once whether or not its result was decided. Whoever calls it must have
        /// reported the reason first (e.g. BallLeftGround), so the delivery still gets exactly one result.
        /// </summary>
        public void Despawn(int deliveryId)
        {
            if (InFlight && deliveryId == ActiveDeliveryId)
                End(BallEndReason.Despawned);
        }

        /// <summary>Ends the active delivery (if any) and hides the ball.</summary>
        public void Cancel()
        {
            if (InFlight)
                End(BallEndReason.Cancelled);
        }

        void FixedUpdate()
        {
            if (!InFlight)
                return;
            TrackPitch();
            if (_dead)
            {
                if (Time.time >= _parkAt) End(BallEndReason.Finalized);
                return;
            }

            float elapsed = Time.time - _launchTime;
            if (elapsed > config.delivery.ballTimeout)
            {
                End(BallEndReason.Timeout);
                return;
            }

            // Settled: slow for long enough after it has come down (a slow ball near release isn't settled).
            if (_bounced && _body.linearVelocity.magnitude < config.delivery.settleSpeed)
            {
                if (_slowSince < 0f) _slowSince = Time.time;
                else if (Time.time - _slowSince > config.delivery.settleTime) End(BallEndReason.Settled);
            }
            else
            {
                _slowSince = -1f;
            }
        }

        /// <summary>
        /// Runs before each physics step, so the body holds the state after the previous step. Collects descending
        /// samples; after the first ground contact, the step where the ball turns upward (or a few steps of rolling)
        /// gives the pitch point via <see cref="BounceMeasurement"/>. Measurement only: nothing here moves the ball.
        /// </summary>
        void TrackPitch()
        {
            if (_pitched)
                return;
            var position = _body.position;
            bool descending = _body.linearVelocity.y < 0f;
            if (_bounced)
                _stepsSinceGround++;

            if (descending && !(_bounced && _stepsSinceGround > MaxStepsWithoutRebound))
            {
                if (_descentCount == DescentHistory)
                {
                    Array.Copy(_descent, 1, _descent, 0, DescentHistory - 1);
                    Array.Copy(_descentTimes, 1, _descentTimes, 0, DescentHistory - 1);
                    _descentCount--;
                }
                _descent[_descentCount] = position;
                _descentTimes[_descentCount] = Time.time;
                _descentCount++;
                return;
            }
            if (!_bounced)
            {
                _descentCount = 0; // still rising (after release or a bat hit)
                return;
            }
            if (_descentCount == 0)
                return;

            _pitched = true;
            var point = BounceMeasurement.TouchPoint(_descent, _descentCount, _groundY + Radius, out float stepsBefore);
            float time = _descentTimes[_descentCount - 1] - stepsBefore * Time.fixedDeltaTime;
            Pitched?.Invoke(ActiveDeliveryId, point, time - _launchTime);
        }

        void OnCollisionEnter(Collision collision)
        {
            if (!InFlight)
                return;
            int id = ActiveDeliveryId;
            bool ground = (_groundMask & (1 << collision.gameObject.layer)) != 0;
            if (ground && !_pitched)
            {
                // Pitch and outfield can both report in the contact step (the outfield top is 2 mm lower);
                // the ball touches the higher surface.
                float surface = collision.GetContact(0).point.y;
                _groundY = _bounced ? Mathf.Max(_groundY, surface) : surface;
            }
            if (!_bounced && ground)
            {
                _bounced = true;
                _stepsSinceGround = 0;
                FirstBounce?.Invoke(id, collision.GetContact(0).point, Time.time - _launchTime);
            }
            Collided?.Invoke(id, collision);
        }

        void End(BallEndReason reason)
        {
            int id = ActiveDeliveryId;
            InFlight = false;
            Park(_body.position);
            DeliveryEnded?.Invoke(id, reason);
        }

        void Park(Vector3 at)
        {
            if (!_body.isKinematic)
            {
                _body.linearVelocity = Vector3.zero;
                _body.angularVelocity = Vector3.zero;
            }
            _body.isKinematic = true;
            _body.position = at;
            if (trail != null)
            {
                trail.emitting = false;
                trail.Clear();
            }
            if (ballRenderer != null)
                ballRenderer.enabled = false;
        }

        public void Configure(GameConfig cfg, TrailRenderer trailRenderer, Renderer visual)
        {
            config = cfg;
            trail = trailRenderer;
            ballRenderer = visual;
        }
    }
}
