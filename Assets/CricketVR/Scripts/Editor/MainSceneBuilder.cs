using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;

namespace CricketVR.Editor
{
    /// <summary>
    /// Creates or repairs Main.unity. Only adds objects that are missing (matched by name or
    /// component); it never deletes objects or moves anything the owner has already placed.
    /// </summary>
    public static class MainSceneBuilder
    {
        public const string ScenePath = "Assets/CricketVR/Scenes/Main.unity";
        const string MaterialsFolder = "Assets/CricketVR/Materials";
        const string XrOriginPrefabName = "XR Origin (XR Rig)";

        [MenuItem("CricketVR/Setup/6. Build or Repair Main Scene")]
        public static void BuildOrRepair()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            var created = new List<string>();
            var scene = OpenOrCreateScene(created);

            var main = FindOrCreateRoot(scene, "Main", created);
            var environment = FindOrCreateChild(main, "Environment", created);
            var player = FindOrCreateChild(main, "Player", created);

            EnsureLight(environment, created);
            EnsureAmbient(created);
            EnsureFloor(environment, created);
            EnsureReferenceObjects(environment, created);
            PracticeNetBuilder.Build(main, environment, created);
            EnsureXrOrigin(player, created);
            BattingBuilder.Build(player, environment, main, created);
            var config = AssetDatabase.LoadAssetAtPath<GameConfig>(BattingBuilder.ConfigPath);
            BowlingBuilder.Build(main, environment, config, created);
            GroundBuilder.Build(main, environment, config, created); // M6.5: generated cricket ground (inactive in NetMode)

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            EnsureSceneInBuildSettings();
            AssetDatabase.SaveAssets(); // GameConfig, materials, and project settings such as the collision matrix

            Debug.Log(created.Count == 0
                ? "[CricketVR] Main scene already complete; nothing added."
                : "[CricketVR] Main scene: added " + string.Join(", ", created));
        }

        /// <summary>Batch-mode entry: -executeMethod CricketVR.Editor.MainSceneBuilder.BuildOrRepairBatch</summary>
        public static void BuildOrRepairBatch() => BuildOrRepair();

        static Scene OpenOrCreateScene(List<string> created)
        {
            if (File.Exists(ScenePath))
                return EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, ScenePath);
            created.Add("scene file");
            return scene;
        }

        static GameObject FindOrCreateRoot(Scene scene, string name, List<string> created)
        {
            var existing = scene.GetRootGameObjects().FirstOrDefault(go => go.name == name);
            if (existing != null)
                return existing;
            created.Add(name);
            return new GameObject(name);
        }

        static GameObject FindOrCreateChild(GameObject parent, string name, List<string> created)
        {
            var existing = parent.transform.Find(name);
            if (existing != null)
                return existing.gameObject;
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            created.Add(name);
            return go;
        }

        static void EnsureLight(GameObject environment, List<string> created)
        {
            if (Object.FindAnyObjectByType<Light>() != null)
                return;
            var go = new GameObject("Directional Light");
            go.transform.SetParent(environment.transform, false);
            go.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            // Keep realtime shadows off until there is a measured reason to pay for them on Quest.
            light.shadows = LightShadows.None;
            created.Add("Directional Light");
        }

        static void EnsureAmbient(List<string> created)
        {
            // Flat ambient keeps the unlit sides of primitives readable without extra lights.
            // Only replaces Unity's default skybox ambient, so a deliberate owner choice is kept.
            if (RenderSettings.ambientMode != UnityEngine.Rendering.AmbientMode.Skybox)
                return;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.56f, 0.6f);
            created.Add("flat ambient light");
        }

        static void EnsureFloor(GameObject environment, List<string> created)
        {
            if (environment.transform.Find("Floor") != null)
                return;
            // Unity's Plane primitive is 10 m x 10 m at scale 1, lying on y = 0.
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.SetParent(environment.transform, false);
            floor.transform.localScale = new Vector3(3f, 1f, 3f);
            floor.GetComponent<Renderer>().sharedMaterial = GetOrCreateMaterial("Floor", new Color(0.32f, 0.45f, 0.30f));
            created.Add("Floor");
        }

        /// <summary>
        /// M1 scale references: a 1 m cube 3 m ahead and a 1.8 m post to the right,
        /// so the owner can sanity-check floor height and metre scale in the headset.
        /// </summary>
        static void EnsureReferenceObjects(GameObject environment, List<string> created)
        {
            var refs = FindOrCreateChild(environment, "ScaleReferences", created);

            if (refs.transform.Find("Cube_1m") == null)
            {
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = "Cube_1m";
                cube.transform.SetParent(refs.transform, false);
                cube.transform.localPosition = new Vector3(0f, 0.5f, 3f);
                cube.GetComponent<Renderer>().sharedMaterial = GetOrCreateMaterial("ReferenceOrange", new Color(0.95f, 0.55f, 0.15f));
                created.Add("Cube_1m");
            }

            if (refs.transform.Find("Post_1.8m") == null)
            {
                // Cylinder primitive is 2 m tall at scale 1, so y-scale 0.9 gives 1.8 m.
                var post = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                post.name = "Post_1.8m";
                post.transform.SetParent(refs.transform, false);
                post.transform.localPosition = new Vector3(1.5f, 0.9f, 2f);
                post.transform.localScale = new Vector3(0.1f, 0.9f, 0.1f);
                post.GetComponent<Renderer>().sharedMaterial = GetOrCreateMaterial("ReferenceBlue", new Color(0.2f, 0.45f, 0.9f));
                created.Add("Post_1.8m");
            }
        }

        static void EnsureXrOrigin(GameObject player, List<string> created)
        {
            var origin = Object.FindAnyObjectByType<XROrigin>(FindObjectsInactive.Include);
            if (origin == null)
            {
                var prefab = FindXrOriginPrefab();
                if (prefab == null)
                {
                    Debug.LogError($"[CricketVR] '{XrOriginPrefabName}' prefab not found. Run CricketVR > Setup > 2. Import XRI Starter Assets, then re-run this step.");
                    return;
                }
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                instance.transform.SetParent(player.transform, false);
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;
                origin = instance.GetComponent<XROrigin>();
                created.Add(XrOriginPrefabName);
            }

            // The tracking-space origin (the centre of the player's boundary) maps to the right-handed stance,
            // facing the launcher (+Z). Only moves a rig still at the M1 default position.
            if (origin.transform.position == Vector3.zero)
            {
                origin.transform.position = FieldDimensions.RightHandedStance;
                origin.transform.rotation = Quaternion.identity;
                created.Add("placed XR Origin at right-handed stance");
            }

            // Floor-relative origin: the headset's real height above the floor sets eye height.
            origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;
            origin.CameraYOffset = 0f;

            // The batting game is stationary (CLAUDE.md §2), so disable the Starter Assets
            // move/turn/teleport providers instead of deleting them from the prefab instance.
            foreach (var provider in origin.GetComponentsInChildren<LocomotionProvider>(true))
            {
                if (provider.enabled)
                {
                    provider.enabled = false;
                    created.Add("disabled " + provider.GetType().Name);
                }
            }
            EditorUtility.SetDirty(origin);

            var origins = Object.FindObjectsByType<XROrigin>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (origins.Length > 1)
                Debug.LogError($"[CricketVR] Found {origins.Length} XR Origins in the scene; keep exactly one.");
        }

        static GameObject FindXrOriginPrefab()
        {
            return AssetDatabase.FindAssets("t:Prefab")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.Contains("Starter Assets") && Path.GetFileNameWithoutExtension(p) == XrOriginPrefabName)
                .Select(AssetDatabase.LoadAssetAtPath<GameObject>)
                .FirstOrDefault();
        }

        static Material GetOrCreateMaterial(string name, Color color)
        {
            var path = $"{MaterialsFolder}/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
                return existing;

            Directory.CreateDirectory(MaterialsFolder);
            var shader = Shader.Find("Universal Render Pipeline/Simple Lit") ?? Shader.Find("Standard");
            var mat = new Material(shader) { color = color };
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        static void EnsureSceneInBuildSettings()
        {
            var scenes = EditorBuildSettings.scenes.ToList();
            int missing = scenes.RemoveAll(s => !File.Exists(s.path));
            if (missing == 0 && scenes.Count > 0 && scenes[0].path == ScenePath && scenes[0].enabled)
                return;
            scenes.RemoveAll(s => s.path == ScenePath);
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log("[CricketVR] Main scene set as the first build scene.");
        }
    }
}
