using UnityEngine;

namespace CricketVR
{
    /// <summary>
    /// One delivery type's settings as an asset (REQ-041), one per type under Assets/CricketVR/Settings/DeliveryTypes/.
    /// Created with the plan.md §19 starting values by the scene builder, which never overwrites an existing asset.
    /// </summary>
    [CreateAssetMenu(menuName = "CricketVR/Delivery Type", fileName = "DeliveryType")]
    public class DeliveryType : ScriptableObject
    {
        public DeliveryTypeData data = DeliveryTypeData.Stock;
    }
}
