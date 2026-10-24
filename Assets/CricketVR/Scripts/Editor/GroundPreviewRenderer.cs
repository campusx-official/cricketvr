using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CricketVR.Editor
{
    /// <summary>
    /// Renders the GroundMode scene from the batting stance (and a few other viewpoints) into Builds/Preview/Ground_*.png
    /// (gitignored), so layout, colours and ball contrast can be checked without the headset. The camera uses the
    /// headset's 1 cm near plane, so z-fighting shows up here too. The scene is opened but never saved.
    /// Batch: -executeMethod CricketVR.Editor.GroundPreviewRenderer.Render
    /// </summary>
    public static class GroundPreviewRenderer
    {
        [MenuItem("CricketVR/Debug/Render Ground Preview")]
        public static void Render()
        {
            EditorSceneManager.OpenScene(MainSceneBuilder.ScenePath, OpenSceneMode.Single);
            var env = GameObject.Find("Main/Environment");
            if (env == null || !env.TryGetComponent<EnvironmentModeSwitch>(out var modeSwitch))
            {
                Debug.LogError("[CricketVR] Main/Environment with EnvironmentModeSwitch not found; build the Main scene first.");
                return;
            }
            modeSwitch.Apply(EnvironmentMode.GroundMode);

            // A ball at the release point and one mid-flight, to judge contrast against the sight screen and outfield.
            var ballSource = GameObject.Find("Main/Gameplay/Ball");
            if (ballSource != null)
            {
                foreach (var p in new[] { FieldDimensions.DefaultReleasePoint, new Vector3(0f, 1.2f, 9f) })
                {
                    var b = Object.Instantiate(ballSource);
                    foreach (var mb in b.GetComponents<MonoBehaviour>()) Object.DestroyImmediate(mb);
                    b.transform.position = p;
                    b.GetComponent<Renderer>().enabled = true;
                }
            }

            var camGo = new GameObject("PreviewCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 1000f;
            var rt = new RenderTexture(1600, 1000, 24) { antiAliasing = 4 };
            cam.targetTexture = rt;

            var eye = new Vector3(FieldDimensions.RightHandedStance.x, 1.65f, FieldDimensions.RightHandedStance.z);
            Shoot(cam, rt, eye, FieldDimensions.DefaultReleasePoint, 90f, "Stance_Warmup");
            Shoot(cam, rt, eye, FieldDimensions.DefaultReleasePoint, 90f, "Stance");
            Shoot(cam, rt, eye, FieldDimensions.DefaultReleasePoint, 25f, "Stance_Zoom");
            Shoot(cam, rt, eye, eye + new Vector3(1f, -0.25f, 0.2f), 90f, "OffSide");
            Shoot(cam, rt, eye, eye + new Vector3(-1f, -0.15f, -0.4f), 90f, "LegSide");
            Shoot(cam, rt, eye, new Vector3(0f, 0.3f, 1.5f), 90f, "Crease");
            Shoot(cam, rt, new Vector3(0f, 90f, -70f), new Vector3(0f, 0f, 12f), 60f, "Aerial");

            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(camGo);
        }

        static void Shoot(Camera cam, RenderTexture rt, Vector3 from, Vector3 at, float fov, string label)
        {
            cam.fieldOfView = fov;
            cam.transform.position = from;
            cam.transform.LookAt(at);
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            Directory.CreateDirectory("Builds/Preview");
            var path = $"Builds/Preview/Ground_{label}.png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            Debug.Log("[CricketVR] Wrote " + Path.GetFullPath(path));
        }
    }
}
