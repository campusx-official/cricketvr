using System;
using UnityEngine;

namespace CricketVR
{
    /// <summary>
    /// GroundMode only (REQ-020, GD-9). Each physics step it checks the ball's position against the boundary in
    /// <see cref="GroundDefinition"/>: crossing the rope raises <see cref="BallLeftGround"/> once per delivery and
    /// hides the ball after the hide delay. A position test rather than trigger volumes: deterministic, cheap, and
    /// the same approach as the net-exit check. It never scores.
    /// Safety despawn (immediate): the ball falls below the outfield or goes far beyond the stands.
    /// Runs before ScoreManager (-45) so the event is adjudicated in the same step it is detected.
    /// </summary>
    [DefaultExecutionOrder(-46)]
    public class GroundBoundaryMonitor : MonoBehaviour
    {
        [SerializeField] GroundDefinition ground;
        [SerializeField] BallController ball;

        int _trackedDelivery = -1;
        bool _raised;

        /// <summary>(delivery id, ball position, time) the first time the ball leaves the ground in a delivery.</summary>
        public event Action<int, Vector3, float> BallLeftGround;

        void FixedUpdate()
        {
            if (ground == null || ball == null || !ball.InFlight)
                return;
            int id = ball.ActiveDeliveryId;
            if (id != _trackedDelivery)
            {
                _trackedDelivery = id;
                _raised = false;
            }

            var pos = ball.Body.position;
            var centre = ground.GroundCentre;
            bool unsafePosition = pos.y < ground.safetyDespawnBelowY ||
                                  GroundGeometry.HorizontalDistance(pos, centre) > ground.safetyDespawnDistance;
            if (!_raised && (unsafePosition || GroundGeometry.DistanceInsideBoundary(pos, centre, ground.BoundaryRadii) < 0f))
            {
                _raised = true;
                BallLeftGround?.Invoke(id, pos, Time.time);
                ball.Finish(id, ground.leftGroundHideDelay); // no-op if the result was already decided
            }
            if (unsafePosition)
            {
                Debug.Log($"[CricketVR] Ball #{id} safety despawn at {pos}");
                ball.Despawn(id);
            }
        }

        public void Configure(GroundDefinition definition, BallController ballController)
        {
            ground = definition;
            ball = ballController;
        }
    }
}
