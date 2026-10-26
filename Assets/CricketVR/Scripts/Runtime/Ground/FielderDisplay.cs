using UnityEngine;

namespace CricketVR
{
    /// <summary>
    /// Shows the placeholder fielders for the batting hand (the field mirrors for a left-hander) or hides them
    /// (<see cref="FieldConfig.showFielders"/>, or <see cref="SetVisible"/> at runtime). REQ-022.
    /// </summary>
    public class FielderDisplay : MonoBehaviour
    {
        [SerializeField] FieldConfig field;
        [SerializeField] BatController bat;
        [SerializeField] GameObject rightHanded;
        [SerializeField] GameObject leftHanded;

        bool _visible = true;
        bool _initialised;
        BatHand _shown;

        void Start()
        {
            _visible = field == null || field.showFielders;
            Refresh(true);
        }

        void Update() => Refresh(false);

        public void SetVisible(bool visible)
        {
            _visible = visible;
            Refresh(true);
        }

        void Refresh(bool force)
        {
            var hand = bat != null ? bat.Hand : BatHand.Right;
            if (!force && _initialised && hand == _shown)
                return;
            _initialised = true;
            _shown = hand;
            if (rightHanded != null) rightHanded.SetActive(_visible && hand == BatHand.Right);
            if (leftHanded != null) leftHanded.SetActive(_visible && hand == BatHand.Left);
        }

        public void Configure(FieldConfig config, BatController batController, GameObject right, GameObject left)
        {
            field = config;
            bat = batController;
            rightHanded = right;
            leftHanded = left;
        }
    }
}
