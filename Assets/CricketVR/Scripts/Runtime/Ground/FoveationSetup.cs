using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace CricketVR
{
    /// <summary>
    /// Fixed foveated rendering (plan.md §15, T046): renders the edges of each eye at lower resolution, which
    /// saves GPU time on Quest with little visible cost. Needs the OpenXR "Foveated Rendering" feature (enabled by
    /// the ground builder), Vulkan and URP. Level 0 = off, 1 = maximum; flags None = fixed (not eye-tracked).
    /// The level comes from <see cref="GameConfig.foveationLevel"/>; Meta's runtime can also override it for an
    /// A/B measurement without rebuilding (see validation.md AC-14 steps).
    /// </summary>
    public class FoveationSetup : MonoBehaviour
    {
        [SerializeField] GameConfig config;
        readonly List<XRDisplaySubsystem> _displays = new List<XRDisplaySubsystem>();

        /// <summary>The level last applied (0 = off).</summary>
        public float Level { get; private set; }

        IEnumerator Start()
        {
            // The display subsystem starts with XR; wait a few frames for it.
            for (int i = 0; i < 30; i++)
            {
                SubsystemManager.GetSubsystems(_displays);
                if (_displays.Exists(d => d.running))
                    break;
                yield return null;
            }
            Apply(config != null ? config.foveationLevel : 0.5f);
        }

        /// <summary>Sets the fixed foveation level on every XR display (0 = off). Used by the AC-14 A/B probe too.</summary>
        public void Apply(float level)
        {
            Level = Mathf.Clamp01(level);
            foreach (var d in _displays)
            {
                d.foveatedRenderingFlags = XRDisplaySubsystem.FoveatedRenderingFlags.None;
                d.foveatedRenderingLevel = Level;
            }
            Debug.Log(_displays.Count == 0
                ? "[CricketVR] Foveated rendering: no XR display (Editor without a headset?)"
                : $"[CricketVR] Foveated rendering level {Level:F2} (fixed) on {_displays.Count} display(s)");
        }

        public void Configure(GameConfig cfg) => config = cfg;
    }
}
