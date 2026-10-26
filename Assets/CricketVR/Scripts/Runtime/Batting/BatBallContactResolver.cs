using System;
using UnityEngine;

namespace CricketVR
{
    public struct BatContactReport
    {
        public int DeliveryId;
        public Vector3 Point;
        public Vector3 Normal;
        public Vector3 BallVelocityIn;
        public Vector3 BallVelocityOut;
        public Vector3 BatPointVelocity;
        public BatResponse Response;
        public SweptContact Sweep;
        public BatHand Hand;
        /// <summary>Contact position along the blade, measured up from the toe (m).</summary>
        public float HeightFromToe;
    }

    /// <summary>
    /// The single authority for bat-ball contact (plan.md §10). Every physics step, before the simulation, it sweeps
    /// the ball's path against the blade's motion over the step. On the first valid touch of a delivery it sets the
    /// ball's outgoing velocity from <see cref="BatContactMath.Resolve"/>. The physics engine's own Ball/BatProxy
    /// collision is disabled in the layer matrix, so no second, uncontrolled impulse can be added.
    /// Only the first bat strike per delivery is applied (MVP rule); later touches are ignored.
    /// </summary>
    [DefaultExecutionOrder(-40)] // after BatController (-50) has produced this step's bat poses
    public class BatBallContactResolver : MonoBehaviour
    {
        [SerializeField] GameConfig config;
        [SerializeField] BatController bat;
        [SerializeField] BallController ball;
        [SerializeField] Transform contactMarker;

        int _struckDelivery = -1;
        int _trackedDelivery = -1;
        float _closestGap = float.PositiveInfinity;
        int _suppressedTouches;

        public event Action<BatContactReport> BatContact;

        /// <summary>Closest bat-ball gap (m) seen during delivery <paramref name="id"/>, or NaN if it isn't the tracked one.</summary>
        public float ClosestGapFor(int id) => id == _trackedDelivery && !float.IsPositiveInfinity(_closestGap) ? _closestGap : float.NaN;

        /// <summary>Delivery id that has already been struck (-1 if none since the last launch).</summary>
        public int StruckDelivery => _struckDelivery;

        void OnEnable()
        {
            if (ball != null) ball.DeliveryEnded += OnDeliveryEnded;
        }

        void OnDisable()
        {
            if (ball != null) ball.DeliveryEnded -= OnDeliveryEnded;
        }

        void FixedUpdate()
        {
            if (config == null || bat == null || ball == null || !ball.IsLive)
                return;

            int id = ball.ActiveDeliveryId;
            if (id != _trackedDelivery)
            {
                _trackedDelivery = id;
                _closestGap = float.PositiveInfinity;
                _suppressedTouches = 0;
            }
            if (id == _struckDelivery || !bat.HasStepPoses)
                return;

            var body = ball.Body;
            float dt = Time.fixedDeltaTime;
            var b0 = body.position;
            var v = body.linearVelocity;
            // Where the physics engine will move the ball this step (velocity first, then position).
            var b1 = b0 + (v + Physics.gravity * dt) * dt;
            var g = bat.Geometry;

            var hit = BatContactMath.Sweep(bat.StepStartPose, bat.StepEndPose, g.BladeCenter, g.BladeSize * 0.5f, b0, b1, ball.Radius);
            TrackClosestApproach(b1, g);
            if (!hit.Hit)
                return;

            if (!bat.ContactAllowed)
            {
                // Tracking just reset (recentre, jump, focus): a pose jump must never become a shot.
                if (_suppressedTouches++ == 0 && config.contact.logContacts)
                    Debug.Log($"[CricketVR] Bat contact #{id} ignored: bat motion suppressed after {bat.Motion.LastResetReason}");
                return;
            }

            var vIn = v + Physics.gravity * (dt * hit.Fraction);
            bat.TryGetPointVelocity(hit.Point, out var vBat);
            var response = BatContactMath.Resolve(vIn, vBat, hit.Normal, hit.LocalPoint, hit.BatPose.rotation, g, config.contact.response);
            if (!response.Applied)
                return; // touching but already separating (e.g. bat moving away faster than the ball)

            var position = hit.Point + hit.Normal * (ball.Radius + 0.001f);
            if (!ball.ApplyBatHit(id, position, response.ExitVelocity))
                return;
            _struckDelivery = id;

            var report = new BatContactReport
            {
                DeliveryId = id,
                Point = hit.Point,
                Normal = hit.Normal,
                BallVelocityIn = vIn,
                BallVelocityOut = response.ExitVelocity,
                BatPointVelocity = vBat,
                Response = response,
                Sweep = hit,
                Hand = bat.Hand,
                HeightFromToe = hit.LocalPoint.y - g.Toe.y,
            };
            if (contactMarker != null && config.contact.showContactMarker)
            {
                contactMarker.gameObject.SetActive(true);
                contactMarker.position = hit.Point;
            }
            if (config.contact.logContacts)
                Log(report);
            BatContact?.Invoke(report);
        }

        void TrackClosestApproach(Vector3 ballCentre, BatGeometry g)
        {
            var pose = bat.StepEndPose;
            var local = Quaternion.Inverse(pose.rotation) * (ballCentre - pose.position) - g.BladeCenter;
            var half = g.BladeSize * 0.5f;
            var outside = new Vector3(Mathf.Max(0f, Mathf.Abs(local.x) - half.x), Mathf.Max(0f, Mathf.Abs(local.y) - half.y), Mathf.Max(0f, Mathf.Abs(local.z) - half.z));
            _closestGap = Mathf.Min(_closestGap, outside.magnitude - ball.Radius);
        }

        void OnDeliveryEnded(int id, BallEndReason reason)
        {
            if (id != _trackedDelivery || id == _struckDelivery || config == null || !config.contact.logContacts)
                return;
            if (_closestGap < config.contact.logNearMissWithin)
                Debug.Log($"[CricketVR] No bat contact #{id}: closest gap {_closestGap * 100f:F1} cm" +
                          (_suppressedTouches > 0 ? $"; {_suppressedTouches} touching step(s) ignored while bat motion was suppressed" : ""));
        }

        void Log(BatContactReport r)
        {
            var o = r.BallVelocityOut;
            float horizontal = new Vector2(o.x, o.z).magnitude;
            float launch = Mathf.Atan2(o.y, horizontal) * Mathf.Rad2Deg;
            // Direction: 0 = straight back past the bowler (+Z), positive = toward +X (off side for a right-hander).
            float direction = Mathf.Atan2(o.x, o.z) * Mathf.Rad2Deg;
            Debug.Log($"[CricketVR] Bat contact #{r.DeliveryId} ({r.Hand} hand): in {r.BallVelocityIn.magnitude:F1} m/s, " +
                      $"bat {r.BatPointVelocity.magnitude:F1} m/s, approach {r.Response.ApproachSpeed:F1} m/s, " +
                      $"out {o.magnitude:F1} m/s ({Units.MsToKmh(o.magnitude):F0} km/h), launch {launch:F0} deg, direction {direction:F0} deg, " +
                      $"e {r.Response.Restitution:F2}, sweet {r.Response.SweetFactor:F2}, edge {r.Response.EdgeFactor:+0.00;-0.00;0.00}, " +
                      $"at {r.HeightFromToe:F2} m from toe, " +
                      $"substeps {r.Sweep.Substeps}{(r.Sweep.InitialOverlap ? ", initial overlap" : "")}" +
                      (string.IsNullOrEmpty(r.Response.Note) ? "" : $", {r.Response.Note}"));
        }

        public void Configure(GameConfig cfg, BatController batController, BallController ballController, Transform marker)
        {
            config = cfg;
            bat = batController;
            ball = ballController;
            contactMarker = marker;
        }
    }
}
