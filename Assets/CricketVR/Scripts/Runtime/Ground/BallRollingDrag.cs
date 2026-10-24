using UnityEngine;

namespace CricketVR
{
    /// <summary>
    /// Cosmetic rolling resistance (REQ-019). PhysX has none, so a ball would roll across the outfield forever.
    /// While the ball touches the Outfield layer, its horizontal speed drops by coefficient x g per second, and its
    /// spin by the same fraction (otherwise friction would turn the spin back into speed). Never reverses the ball;
    /// does nothing on the pitch or in the air, so delivery bounce calibration is untouched.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class BallRollingDrag : MonoBehaviour
    {
        [Tooltip("Deceleration on the outfield = coefficient x g.")]
        public float coefficient = 0.08f;

        Rigidbody _body;
        int _outfieldLayer;
        float _lastTouch = float.NegativeInfinity;

        void Awake()
        {
            _body = GetComponent<Rigidbody>();
            _outfieldLayer = LayerMask.NameToLayer("Outfield");
        }

        void OnCollisionEnter(Collision c) => Touch(c);
        void OnCollisionStay(Collision c) => Touch(c);

        /// <summary>
        /// Real contact only: PhysX reports surfaces within its contact offset (~1 cm) as touching, and the outfield
        /// lies 2 mm below the pitch, so a ball rolling on the pitch also "touches" the outfield. Separation tells them apart.
        /// </summary>
        void Touch(Collision c)
        {
            if (_outfieldLayer < 0 || c.gameObject.layer != _outfieldLayer)
                return;
            for (int i = 0; i < c.contactCount; i++)
                if (c.GetContact(i).separation <= MaxTouchSeparation)
                {
                    _lastTouch = Time.fixedTime;
                    return;
                }
        }

        const float MaxTouchSeparation = 0.001f;

        void FixedUpdate()
        {
            if (_body.isKinematic || Time.fixedTime - _lastTouch > Time.fixedDeltaTime * 1.5f)
                return;
            var v = _body.linearVelocity;
            float speed = new Vector2(v.x, v.z).magnitude;
            if (speed < 1e-4f)
                return;
            float k = Mathf.Max(0f, speed - coefficient * -Physics.gravity.y * Time.fixedDeltaTime) / speed;
            _body.linearVelocity = new Vector3(v.x * k, v.y, v.z * k);
            _body.angularVelocity *= k;
        }
    }
}
