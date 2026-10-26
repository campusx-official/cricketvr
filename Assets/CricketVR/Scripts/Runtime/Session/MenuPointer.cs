using UnityEngine;

namespace CricketVR
{
    /// <summary>
    /// Controls the XRI interactors from the Starter Assets rig (REQ-025): the right controller's Near-Far ray is
    /// shown only while a panel with buttons is open; every other interactor (left ray, both pokes, both teleports)
    /// stays off, so the faulty left controller is never needed and nothing can press a button by accident during
    /// play. Pull the right trigger to press the button under the ray.
    /// </summary>
    public class MenuPointer : MonoBehaviour
    {
        [SerializeField] GameObject rightRay;
        [Tooltip("Interactors that are never used in this game.")]
        [SerializeField] GameObject[] alwaysOff;

        void Awake()
        {
            if (alwaysOff != null)
                foreach (var go in alwaysOff) if (go != null) go.SetActive(false);
            SetVisible(false);
        }

        public void SetVisible(bool visible)
        {
            if (rightRay != null && rightRay.activeSelf != visible)
                rightRay.SetActive(visible);
        }

        public void Configure(GameObject ray, GameObject[] off)
        {
            rightRay = ray;
            alwaysOff = off;
        }
    }
}
