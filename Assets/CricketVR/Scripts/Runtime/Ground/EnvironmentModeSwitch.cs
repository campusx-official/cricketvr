using UnityEngine;

namespace CricketVR
{
    public enum EnvironmentMode
    {
        /// <summary>The practice net with M6 net scoring (default until the virtual-field scorer exists).</summary>
        NetMode,
        /// <summary>The cricket ground; net scoring off, results read "practice scoring pending".</summary>
        GroundMode,
    }

    /// <summary>
    /// Shows the net or the ground at startup from <see cref="GameConfig.environment"/> (REQ-021). Shared objects
    /// (pitch collider, striker's stumps, machine, bat, ball, scoreboard) are never switched. Objects stay where the
    /// builders put them, so NetMode scenes and tests are unchanged.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class EnvironmentModeSwitch : MonoBehaviour
    {
        [SerializeField] GameConfig config;
        [Tooltip("Active only in NetMode (floor, nets, net colliders, painted crease cubes).")]
        [SerializeField] GameObject[] netModeObjects;
        [Tooltip("Renderers drawn only in NetMode; their colliders stay active (the pitch surface).")]
        [SerializeField] Renderer[] netModeRenderers;
        [Tooltip("Active only in GroundMode (the generated ground, the boundary monitor).")]
        [SerializeField] GameObject[] groundModeObjects;

        public EnvironmentMode Mode { get; private set; }

        void Awake()
        {
            if (config == null)
            {
                Debug.LogError("[CricketVR] EnvironmentModeSwitch has no GameConfig; run CricketVR > Setup > 6. Build or Repair Main Scene.", this);
                return;
            }
            Apply(config.environment);
        }

        public void Apply(EnvironmentMode mode)
        {
            Mode = mode;
            bool ground = mode == EnvironmentMode.GroundMode;
            if (netModeObjects != null)
                foreach (var go in netModeObjects) if (go != null) go.SetActive(!ground);
            if (netModeRenderers != null)
                foreach (var r in netModeRenderers) if (r != null) r.enabled = !ground;
            if (groundModeObjects != null)
                foreach (var go in groundModeObjects) if (go != null) go.SetActive(ground);
            Debug.Log($"[CricketVR] Environment: {mode}");
        }

        public void Configure(GameConfig cfg, GameObject[] netObjects, Renderer[] netRenderers, GameObject[] groundObjects)
        {
            config = cfg;
            netModeObjects = netObjects;
            netModeRenderers = netRenderers;
            groundModeObjects = groundObjects;
        }
    }
}
