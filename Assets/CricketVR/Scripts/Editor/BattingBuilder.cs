using System.Collections.Generic;
using System.IO;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;

namespace CricketVR.Editor
{
    /// <summary>
    /// M3: GameConfig asset, bat visual, kinematic physics proxy, and the bat/handedness/setup components.
    /// Adds missing pieces only; serialized references are re-wired each run so they can't go stale.
    /// </summary>
    static class BattingBuilder
    {
        public const string ConfigPath = "Assets/CricketVR/Settings/GameConfig.asset";
        public const string BatProxyLayer = "BatProxy";

        public static void Build(GameObject player, GameObject environment, GameObject main, List<string> created)
        {
            PracticeNetBuilder.EnsureLayers(created, BatProxyLayer);
            var config = EnsureConfig(created);

            var origin = Object.FindAnyObjectByType<XROrigin>(FindObjectsInactive.Include);
            if (origin == null)
            {
                Debug.LogError("[CricketVR] No XR Origin; bat setup skipped.");
                return;
            }
            var left = origin.transform.Find("Camera Offset/Left Controller");
            var right = origin.transform.Find("Camera Offset/Right Controller");
            if (left == null || right == null)
            {
                Debug.LogError("[CricketVR] XR Origin has no 'Camera Offset/Left Controller' or 'Right Controller'; bat setup skipped.");
                return;
            }
            DisableTeleportInteractors(origin.transform, created);
            var leftGrip = EnsureGripTracker(left.parent, "Left Grip", "LeftHand", created);
            var rightGrip = EnsureGripTracker(right.parent, "Right Grip", "RightHand", created);

            var batRoot = player.transform.Find("Bat")?.gameObject;
            if (batRoot == null)
            {
                batRoot = new GameObject("Bat");
                batRoot.transform.SetParent(player.transform, false);
                created.Add("Bat");
            }

            var geometry = config.bat.geometry;
            var visual = batRoot.transform.Find("BatVisual");
            if (visual == null)
            {
                visual = BuildVisual(batRoot.transform, geometry).transform;
                created.Add("BatVisual");
            }

            ApplyRealisticBat(visual, geometry, created);

            var proxy = batRoot.transform.Find("BatPhysicsProxy")?.gameObject;
            if (proxy == null)
            {
                proxy = new GameObject("BatPhysicsProxy");
                proxy.transform.SetParent(batRoot.transform, false);
                proxy.AddComponent<Rigidbody>();
                proxy.AddComponent<BoxCollider>();
                created.Add("BatPhysicsProxy");
            }
            proxy.layer = LayerMask.NameToLayer(BatProxyLayer);
            var body = proxy.GetComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            proxy.GetComponent<BoxCollider>().size = geometry.BladeSize;

            var batController = GetOrAdd<BatController>(batRoot, created);
            var handedness = GetOrAdd<HandednessSetup>(batRoot, created);
            var controls = GetOrAdd<BatSetupControls>(batRoot, created);

            var blade = visual.Find("Blade")?.GetComponent<Renderer>();
            batController.Configure(config, leftGrip, rightGrip, visual, blade, body, proxy.GetComponent<BoxCollider>());
            handedness.Configure(config, batController, origin.transform,
                environment.transform.Find("BattingAnchor/StanceMarker_RightHanded"),
                environment.transform.Find("BattingAnchor/StanceMarker_LeftHanded"),
                main.transform.Find("UI/LiveScoreboard"));
            controls.Configure(batController, handedness);
            EditorUtility.SetDirty(batController);
            EditorUtility.SetDirty(handedness);
            EditorUtility.SetDirty(controls);
        }

        static GameConfig EnsureConfig(List<string> created)
        {
            var config = AssetDatabase.LoadAssetAtPath<GameConfig>(ConfigPath);
            if (config != null)
                return config;
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath));
            config = ScriptableObject.CreateInstance<GameConfig>();
            AssetDatabase.CreateAsset(config, ConfigPath);
            AssetDatabase.SaveAssets();
            created.Add("GameConfig asset");
            return config;
        }

        static GameObject BuildVisual(Transform parent, BatGeometry g)
        {
            var root = new GameObject("BatVisual");
            root.transform.SetParent(parent, false);

            var willow = PracticeNetBuilder.Mat("BatWillow", new Color(0.93f, 0.84f, 0.60f));
            var grip = PracticeNetBuilder.Mat("BatGrip", new Color(0.12f, 0.12f, 0.14f));
            var spine = PracticeNetBuilder.Mat("BatSpine", new Color(0.55f, 0.38f, 0.20f));

            PracticeNetBuilder.CreateVisual(PrimitiveType.Cylinder, "Handle", root.transform, Vector3.zero, Vector3.one, grip);
            PracticeNetBuilder.CreateVisual(PrimitiveType.Cube, "Blade", root.transform, Vector3.zero, Vector3.one, willow);
            // A darker ridge on the back of the blade shows which side is the hitting face (+Z).
            PracticeNetBuilder.CreateVisual(PrimitiveType.Cube, "Spine", root.transform, Vector3.zero, Vector3.one, spine);
            return root;
        }

        /// <summary>
        /// Regenerates the realistic bat meshes from GameConfig (the source of truth for bat geometry) and assigns
        /// them to the visual's parts. The M3 placeholder 'Spine' stripe is deactivated, not deleted.
        /// </summary>
        static void ApplyRealisticBat(Transform visual, BatGeometry g, List<string> created)
        {
            var meshes = BatMeshGenerator.Generate(g);

            var willow = PracticeNetBuilder.Mat("BatWillow", Color.white);
            willow.SetTexture("_BaseMap", BatMeshGenerator.EnsureGrainTexture());
            willow.SetColor("_BaseColor", Color.white);
            willow.SetFloat("_Smoothness", 0.25f);
            EditorUtility.SetDirty(willow);

            SetPart(visual, "Blade", meshes.blade, willow, created);
            SetPart(visual, "Handle", meshes.handle, PracticeNetBuilder.Mat("BatGrip", new Color(0.10f, 0.10f, 0.12f)), created);
            SetPart(visual, "Splice", meshes.splice, PracticeNetBuilder.Mat("BatSplice", new Color(0.16f, 0.11f, 0.08f)), created);
            SetPart(visual, "Sticker", meshes.sticker, PracticeNetBuilder.Mat("BatSticker", new Color(0.07f, 0.13f, 0.36f)), created);
            SetPart(visual, "StickerAccent", meshes.stickerAccent, PracticeNetBuilder.Mat("BatStickerAccent", new Color(0.86f, 0.66f, 0.16f)), created);

            var placeholder = visual.Find("Spine");
            if (placeholder != null && placeholder.gameObject.activeSelf)
            {
                placeholder.gameObject.SetActive(false);
                created.Add("deactivated placeholder Spine");
            }
        }

        static void SetPart(Transform visual, string name, Mesh mesh, Material material, List<string> created)
        {
            var t = visual.Find(name);
            if (t == null)
            {
                t = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer)).transform;
                t.SetParent(visual, false);
                created.Add("bat part " + name);
            }
            // Meshes are authored in the bat frame, so every part sits at the bat origin unscaled.
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one;
            t.GetComponent<MeshFilter>().sharedMesh = mesh;
            var r = t.GetComponent<MeshRenderer>();
            r.sharedMaterial = material;
            // One of the few realtime shadow casters (bat, ball, stumps, machine; plan.md §15).
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }

        /// <summary>
        /// A transform driven by the controller's GRIP pose. The Starter Assets controllers follow the aim
        /// (pointer) pose, which is tilted relative to the handle; the bat must follow the grip so it lines up
        /// with the physical handle (E-009).
        /// </summary>
        static Transform EnsureGripTracker(Transform cameraOffset, string name, string hand, List<string> created)
        {
            var existing = cameraOffset.Find(name);
            if (existing != null)
                return existing;

            var go = new GameObject(name);
            go.transform.SetParent(cameraOffset, false);
            var driver = go.AddComponent<TrackedPoseDriver>();
            driver.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
            // Update again just before rendering so the visual bat has the lowest possible latency.
            driver.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
            driver.positionInput = new InputActionProperty(new InputAction(name + " Position", InputActionType.Value, $"<XRController>{{{hand}}}/devicePosition", expectedControlType: "Vector3"));
            driver.rotationInput = new InputActionProperty(new InputAction(name + " Rotation", InputActionType.Value, $"<XRController>{{{hand}}}/deviceRotation", expectedControlType: "Quaternion"));
            driver.trackingStateInput = new InputActionProperty(new InputAction(name + " Tracking State", InputActionType.Value, $"<XRController>{{{hand}}}/trackingState", expectedControlType: "Integer"));
            created.Add(name + " (grip-pose TrackedPoseDriver)");
            return go.transform;
        }

        static void DisableTeleportInteractors(Transform origin, List<string> created)
        {
            // Thumbstick-up would otherwise show a teleport arc while nudging bat tilt; locomotion is already off.
            foreach (var t in origin.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "Teleport Interactor" && t.gameObject.activeSelf)
                {
                    t.gameObject.SetActive(false);
                    created.Add($"deactivated {t.parent.name}/Teleport Interactor");
                }
            }
        }

        static T GetOrAdd<T>(GameObject go, List<string> created) where T : Component
        {
            var c = go.GetComponent<T>();
            if (c != null)
                return c;
            created.Add(typeof(T).Name);
            return go.AddComponent<T>();
        }
    }
}
