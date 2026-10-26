using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;

namespace CricketVR.Editor
{
    /// <summary>
    /// M6.5 cricket ground (plan.md §15). Regenerates Environment/Ground entirely from GroundDefinition and
    /// FieldConfig: meshes, textures and materials are written to assets so builds don't depend on this tool, and
    /// the same inputs always produce the same files (seeded noise). Nothing outside Environment/Ground is rebuilt;
    /// shared objects are only wired (mode switch, boundary monitor, rolling drag) and render settings applied.
    /// The ground is generated content: hand edits under Environment/Ground are replaced on the next build.
    /// </summary>
    public static class GroundBuilder
    {
        public const string OutfieldLayer = "Outfield";
        public const string GroundDefinitionPath = "Assets/CricketVR/Settings/GroundDefinition.asset";
        public const string FieldConfigPath = "Assets/CricketVR/Settings/FieldConfig.asset";
        const string GeneratedFolder = "Assets/CricketVR/Generated/Ground";
        const string MaterialsFolder = "Assets/CricketVR/Materials/Ground";
        const string UrpAssetPath = "Assets/Settings/Mobile_RPAsset.asset";
        const float ShadowDistance = 25f;

        [MenuItem("CricketVR/Setup/7. Build or Rebuild Ground")]
        public static void BuildMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            var scene = EditorSceneManager.OpenScene(MainSceneBuilder.ScenePath, OpenSceneMode.Single);
            var main = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "Main");
            var environment = main != null ? main.transform.Find("Environment") : null;
            if (environment == null)
            {
                Debug.LogError("[CricketVR] Main/Environment missing; run CricketVR > Setup > 6. Build or Repair Main Scene first.");
                return;
            }
            var created = new List<string>();
            Build(main, environment.gameObject, AssetDatabase.LoadAssetAtPath<GameConfig>(BattingBuilder.ConfigPath), created);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("[CricketVR] Ground built: " + string.Join(", ", created));
        }

        /// <summary>Batch-mode entry: -executeMethod CricketVR.Editor.GroundBuilder.BuildBatch</summary>
        public static void BuildBatch() => BuildMenu();

        public static void Build(GameObject main, GameObject environment, GameConfig config, List<string> created)
        {
            var def = LoadOrCreate<GroundDefinition>(GroundDefinitionPath, created);
            var field = LoadOrCreate<FieldConfig>(FieldConfigPath, created);
            var errors = def.Validate(field);
            if (errors.Count > 0)
            {
                foreach (var e in errors)
                    Debug.LogError("[CricketVR] GroundDefinition/FieldConfig: " + e);
                Debug.LogError("[CricketVR] Ground NOT rebuilt; fix the settings above.");
                return;
            }
            PracticeNetBuilder.EnsureLayers(created, OutfieldLayer);
            if (!AssetDatabase.IsValidFolder(GeneratedFolder) || !AssetDatabase.IsValidFolder(MaterialsFolder))
            {
                Directory.CreateDirectory(GeneratedFolder);
                Directory.CreateDirectory(MaterialsFolder);
                AssetDatabase.Refresh();
            }

            var old = environment.transform.Find("Ground");
            if (old != null)
                Object.DestroyImmediate(old.gameObject);
            var root = new GameObject("Ground");
            root.transform.SetParent(environment.transform, false);
            created.Add("Ground (regenerated)");

            var outfieldTex = SaveTexture("OutfieldStripes", OutfieldTexture(def), TextureWrapMode.Repeat, 8);
            var pitchTex = SaveTexture("PitchStrip", PitchTexture(def), TextureWrapMode.Clamp, 8);
            var crowdTex = SaveTexture("Crowd", CrowdTexture(), TextureWrapMode.Repeat, 4);

            var outfieldMat = GroundMaterial("Outfield", outfieldTex, receiveShadows: true, offsetFactor: 1f, offsetUnits: 1f);
            var pitchMat = GroundMaterial("PitchStrip", pitchTex, receiveShadows: true);
            var markingsMat = GroundMaterial("Markings", null, offsetFactor: -1f, offsetUnits: -2f);
            var plainMat = GroundMaterial("VertexColour", null);
            // The batter reads the ball against the screen behind the machine: keep it black, not fog-grey (GD-2).
            var screenMat = GroundMaterial("SightScreen", null, fogAmount: 0.1f);
            var standsMat = GroundMaterial("Stands", crowdTex);
            var skyMat = Material("Sky", Shader.Find("CricketVR/SkyGradient"));

            Piece(root, "Outfield", Outfield(def), outfieldMat, receiveShadows: true);
            Piece(root, "PitchStrip", PitchStrip(def), pitchMat, receiveShadows: true);
            Piece(root, "CreaseExtensions", CreaseExtensions(def), markingsMat);
            Piece(root, "FieldingCircleMarkers", CircleMarkers(def), markingsMat);
            Piece(root, "BoundaryRope", Rope(def), plainMat);
            Piece(root, "SightScreens", SightScreens(def), screenMat);
            Piece(root, "AdvertisingBoards", Boards(def), plainMat);
            Piece(root, "Stands", Stands(def), standsMat);
            Piece(root, "Sky", SkyDome(def), skyMat);

            var fielders = new GameObject("Fielders");
            fielders.transform.SetParent(root.transform, false);
            var right = Piece(fielders, "RightHanded", Fielders(def, field, false), plainMat);
            var left = Piece(fielders, "LeftHanded", Fielders(def, field, true), plainMat);
            var batT = main.transform.Find("Player/Bat");
            BatController bat = null;
            if (batT != null) batT.TryGetComponent(out bat);
            fielders.AddComponent<FielderDisplay>().Configure(field, bat, right, left);

            EnsureOutfieldCollider(root, def, created);
            root.SetActive(false); // NetMode is the scene default; EnvironmentModeSwitch shows the ground in GroundMode

            Wire(main, environment, root, config, def, created);
            ApplyRenderSettings(environment, def, created);
        }

        // ---------- Wiring ----------

        static void Wire(GameObject main, GameObject environment, GameObject ground, GameConfig config, GroundDefinition def, List<string> created)
        {
            var gameplay = main.transform.Find("Gameplay");
            var ballT = gameplay != null ? gameplay.Find("Ball") : null;
            if (ballT == null || !ballT.TryGetComponent<BallController>(out var ball))
            {
                Debug.LogError("[CricketVR] Gameplay/Ball missing; run the main scene builder.");
                return;
            }
            // TryGetComponent, not "GetComponent ?? Add": in the Editor a missing component is a fake-null object.
            if (!ball.TryGetComponent<BallRollingDrag>(out var drag))
                drag = ball.gameObject.AddComponent<BallRollingDrag>();
            drag.coefficient = def.rollingResistance;
            EditorUtility.SetDirty(drag);

            var monitorT = gameplay.Find("GroundBoundaryMonitor");
            if (monitorT == null)
            {
                monitorT = new GameObject("GroundBoundaryMonitor").transform;
                monitorT.SetParent(gameplay, false);
                created.Add("GroundBoundaryMonitor");
            }
            if (!monitorT.TryGetComponent<GroundBoundaryMonitor>(out var monitor))
                monitor = monitorT.gameObject.AddComponent<GroundBoundaryMonitor>();
            monitor.Configure(def, ball);
            monitorT.gameObject.SetActive(false);
            EditorUtility.SetDirty(monitor);

            var scoreT = gameplay.Find("ScoreManager");
            if (scoreT != null && scoreT.TryGetComponent<ScoreManager>(out var score))
            {
                score.ConfigureGround(monitor);
                EditorUtility.SetDirty(score);
            }

            var fovT = gameplay.Find("Foveation");
            if (fovT == null)
            {
                fovT = new GameObject("Foveation").transform;
                fovT.SetParent(gameplay, false);
                created.Add("Foveation");
            }
            if (!fovT.TryGetComponent<FoveationSetup>(out var foveation))
                foveation = fovT.gameObject.AddComponent<FoveationSetup>();
            foveation.Configure(config);
            EditorUtility.SetDirty(foveation);
            if (!fovT.TryGetComponent<PerformanceProbe>(out var probe))
                probe = fovT.gameObject.AddComponent<PerformanceProbe>();
            probe.Configure(config, foveation);
            EditorUtility.SetDirty(probe);
            PlayerSettings.enableFrameTimingStats = true; // CPU/GPU frame times for the probe and the Profiler

            var env = environment.transform;
            var netObjects = new[] { "Floor", "NetFrameAndVisuals", "NetBoundaryColliders", "Pitch/Markings" }
                .Select(p => env.Find(p)).Where(t => t != null).Select(t => t.gameObject).ToArray();
            var surface = env.Find("Pitch/Surface");
            var netRenderers = surface != null && surface.TryGetComponent<Renderer>(out var surfaceRenderer) ? new[] { surfaceRenderer } : new Renderer[0];
            if (!environment.TryGetComponent<EnvironmentModeSwitch>(out var modeSwitch))
                modeSwitch = environment.AddComponent<EnvironmentModeSwitch>();
            modeSwitch.Configure(config, netObjects, netRenderers, new[] { ground, monitorT.gameObject });
            EditorUtility.SetDirty(modeSwitch);
        }

        static void EnsureOutfieldCollider(GameObject root, GroundDefinition def, List<string> created)
        {
            var go = new GameObject("OutfieldCollider") { layer = LayerMask.NameToLayer(OutfieldLayer) };
            go.transform.SetParent(root.transform, false);
            var c = def.GroundCentre;
            float t = def.outfieldColliderThickness;
            var box = go.AddComponent<BoxCollider>();
            // Top at y = 0, just under the pitch collider's top (0.002), so the pitch decides every bounce on the strip.
            box.center = new Vector3(c.x, -t * 0.5f, c.z);
            box.size = new Vector3(2f * (def.boundaryRadiusX + def.outfieldColliderBeyondRope), t, 2f * (def.boundaryRadiusZ + def.outfieldColliderBeyondRope));
            box.sharedMaterial = OutfieldPhysics(def, created);
        }

        static PhysicsMaterial OutfieldPhysics(GroundDefinition def, List<string> created)
        {
            const string path = "Assets/CricketVR/Settings/Physics/OutfieldGrass.physicMaterial";
            var m = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path);
            if (m == null)
            {
                m = new PhysicsMaterial("OutfieldGrass");
                AssetDatabase.CreateAsset(m, path);
                created.Add("physics material OutfieldGrass");
            }
            // Lower bounce, more grip than the pitch (0.72 / 0.2). Maximum combine: the surface decides, as on the pitch.
            m.bounciness = def.outfieldBounciness;
            m.dynamicFriction = def.outfieldDynamicFriction;
            m.staticFriction = def.outfieldStaticFriction;
            m.bounceCombine = PhysicsMaterialCombine.Maximum;
            m.frictionCombine = PhysicsMaterialCombine.Maximum;
            EditorUtility.SetDirty(m);
            return m;
        }

        static void ApplyRenderSettings(GameObject environment, GroundDefinition def, List<string> created)
        {
            // Linear distance fog: far stands soften instead of shimmering. Nothing in the net scene is beyond 60 m.
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = def.fogStart;
            RenderSettings.fogEndDistance = def.fogEnd;
            RenderSettings.fogColor = def.skyHorizon;

            // One directional light; realtime hard shadows only from the bat, ball, stumps and machine.
            var light = Object.FindObjectsByType<Light>(FindObjectsSortMode.None).FirstOrDefault(l => l.type == LightType.Directional);
            if (light != null)
            {
                light.shadows = LightShadows.Hard;
                light.shadowStrength = 0.75f;
                EditorUtility.SetDirty(light);
            }
            // Outside Environment only the bat and the ball cast; the thin scoreboard, debug markers and controller
            // pointer lines otherwise draw stray shadows across the pitch.
            var main = environment.transform.parent;
            if (main != null)
                foreach (var (rootName, keep) in new[] { ("Player", "Bat"), ("Gameplay", "Ball"), ("UI", "\0") })
                {
                    var branch = main.Find(rootName);
                    if (branch == null) continue;
                    foreach (var r in branch.GetComponentsInChildren<Renderer>(true))
                    {
                        string path = PathOf(r.transform, branch);
                        var mode = (path == keep || path.StartsWith(keep + "/")) && !(r is TrailRenderer) ? ShadowCastingMode.On : ShadowCastingMode.Off;
                        if (r.shadowCastingMode != mode)
                        {
                            r.shadowCastingMode = mode;
                            EditorUtility.SetDirty(r);
                        }
                    }
                }
            foreach (var r in environment.GetComponentsInChildren<Renderer>(true))
            {
                string path = PathOf(r.transform, environment.transform);
                bool caster = path.StartsWith("Stumps") || path.StartsWith("Pitch/NonStrikerStumps") || path.StartsWith("LauncherAnchor");
                var mode = caster ? ShadowCastingMode.On : ShadowCastingMode.Off;
                if (r.shadowCastingMode != mode)
                {
                    r.shadowCastingMode = mode;
                    EditorUtility.SetDirty(r);
                }
            }

            // Fixed foveated rendering needs this OpenXR feature on Android (installed with the OpenXR plugin, off by default).
            var xr = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            var fov = xr != null ? xr.GetFeature<FoveatedRenderingFeature>() : null;
            if (fov == null)
                Debug.LogError("[CricketVR] OpenXR Foveated Rendering feature not found for Android.");
            else if (!fov.enabled)
            {
                fov.enabled = true;
                EditorUtility.SetDirty(fov);
                EditorUtility.SetDirty(xr);
                created.Add("OpenXR Foveated Rendering enabled (Android)");
            }

            var urp = AssetDatabase.LoadMainAssetAtPath(UrpAssetPath);
            if (urp != null)
            {
                var so = new SerializedObject(urp);
                so.FindProperty("m_ShadowDistance").floatValue = ShadowDistance;
                so.FindProperty("m_ShadowCascadeCount").intValue = 1;
                so.FindProperty("m_SoftShadowsSupported").boolValue = false;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            else
            {
                Debug.LogError($"[CricketVR] {UrpAssetPath} not found; set Shadow Distance to {ShadowDistance} m in the URP asset by hand.");
            }
        }

        static string PathOf(Transform t, Transform root)
        {
            var parts = new List<string>();
            for (; t != null && t != root; t = t.parent) parts.Insert(0, t.name);
            return string.Join("/", parts);
        }

        // ---------- Meshes ----------

        static Mesh Outfield(GroundDefinition def)
        {
            var m = new MeshBuilder();
            var c = def.GroundCentre;
            const int segments = 96;
            float[] rings = { 0.2f, 0.45f, 0.7f, 0.9f, 1f };
            float tile = 2f * def.stripeWidth; // one texture repeat = a light and a dark stripe
            Vector2 Uv(Vector3 p) => new Vector2(p.x / tile, p.z / tile);
            Vector3 At(int i, float f)
            {
                float a = i * 2f * Mathf.PI / segments;
                float r = (GroundGeometry.BoundaryRadiusAtAngle(def.BoundaryRadii, a) + def.outfieldBeyondRope) * f;
                return new Vector3(c.x + r * Mathf.Sin(a), 0f, c.z + r * Mathf.Cos(a));
            }
            int centre = m.Vertex(c, Vector3.up, Uv(c), Color.white);
            int[] prev = null;
            foreach (float f in rings)
            {
                var ring = new int[segments];
                for (int i = 0; i < segments; i++)
                {
                    var p = At(i, f);
                    ring[i] = m.Vertex(p, Vector3.up, Uv(p), Color.white);
                }
                for (int i = 0; i < segments; i++)
                {
                    int j = (i + 1) % segments;
                    if (prev == null) m.Triangle(centre, ring[i], ring[j], Vector3.up);
                    else m.Quad(prev[i], ring[i], ring[j], prev[j], Vector3.up);
                }
                prev = ring;
            }
            return m.ToMesh("Outfield");
        }

        static Mesh PitchStrip(GroundDefinition def)
        {
            var m = new MeshBuilder();
            const float y = 0.003f; // 1 mm above the pitch collider top (0.002)
            float hw = def.pitchWidth * 0.5f, length = def.pitchEndZ - def.pitchStartZ;
            const int rows = 23;
            int[] prev = null;
            for (int k = 0; k <= rows; k++)
            {
                float v = (float)k / rows, z = def.pitchStartZ + v * length;
                var row = new[]
                {
                    m.Vertex(new Vector3(-hw, y, z), Vector3.up, new Vector2(0f, v), Color.white),
                    m.Vertex(new Vector3(hw, y, z), Vector3.up, new Vector2(1f, v), Color.white),
                };
                if (prev != null)
                    m.Quad(prev[0], row[0], row[1], prev[1], Vector3.up);
                prev = row;
            }
            return m.ToMesh("PitchStrip");
        }

        /// <summary>The popping creases run 1.83 m either side of middle stump, past the strip edges onto the grass (GD-4).</summary>
        static Mesh CreaseExtensions(GroundDefinition def)
        {
            var m = new MeshBuilder();
            float hw = def.pitchWidth * 0.5f, half = def.poppingCreaseHalfLength, w = def.creaseLineWidth * 0.5f;
            const float y = 0.002f;
            var white = def.creaseColor;
            if (half > hw)
                foreach (float z in new[] { def.poppingCreaseOffset, def.pitchLength - def.poppingCreaseOffset })
                    foreach (float s in new[] { -1f, 1f })
                    {
                        float x0 = s * hw, x1 = s * half;
                        m.FlatQuad(new Vector3(x0, y, z - w), new Vector3(x1, y, z - w), new Vector3(x1, y, z + w), new Vector3(x0, y, z + w), Vector3.up, white);
                    }
            return m.ToMesh("CreaseExtensions");
        }

        static Mesh CircleMarkers(GroundDefinition def)
        {
            var m = new MeshBuilder();
            var points = new List<Vector3>();
            GroundGeometry.FieldingCircle(0f, def.pitchLength, def.fieldingCircleRadius, def.circleMarkerSpacing, points);
            const int sides = 12;
            float r = def.circleMarkerDiameter * 0.5f;
            foreach (var p in points)
            {
                int centre = m.Vertex(p + Vector3.up * 0.004f, Vector3.up, Vector2.zero, def.creaseColor);
                int first = -1, last = -1;
                for (int i = 0; i < sides; i++)
                {
                    float a = i * 2f * Mathf.PI / sides;
                    int v = m.Vertex(p + new Vector3(r * Mathf.Sin(a), 0.004f, r * Mathf.Cos(a)), Vector3.up, Vector2.zero, def.creaseColor);
                    if (last >= 0) m.Triangle(centre, last, v, Vector3.up);
                    else first = v;
                    last = v;
                }
                m.Triangle(centre, last, first, Vector3.up);
            }
            return m.ToMesh("FieldingCircleMarkers");
        }

        static Mesh Rope(GroundDefinition def)
        {
            var m = new MeshBuilder();
            var c = def.GroundCentre;
            int segs = Mathf.Max(32, def.ropeSegments), sides = Mathf.Max(3, def.ropeSides);
            float tr = def.ropeRadius;
            var rings = new int[segs][];
            for (int i = 0; i < segs; i++)
            {
                float a = i * 2f * Mathf.PI / segs;
                var outward = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                float r = GroundGeometry.BoundaryRadiusAtAngle(def.BoundaryRadii, a) - tr; // outer edge on the boundary line
                var axis = new Vector3(c.x, tr, c.z) + outward * r;
                rings[i] = new int[sides];
                for (int k = 0; k < sides; k++)
                {
                    float phi = k * 2f * Mathf.PI / sides;
                    var n = outward * Mathf.Cos(phi) + Vector3.up * Mathf.Sin(phi);
                    rings[i][k] = m.Vertex(axis + n * tr, n, Vector2.zero, def.ropeColor);
                }
            }
            for (int i = 0; i < segs; i++)
            {
                var a = rings[i];
                var b = rings[(i + 1) % segs];
                for (int k = 0; k < sides; k++)
                {
                    int k1 = (k + 1) % sides;
                    var facing = m.NormalOf(a[k]) + m.NormalOf(b[k1]);
                    m.Quad(a[k], b[k], b[k1], a[k1], facing);
                }
            }
            return m.ToMesh("BoundaryRope");
        }

        static Mesh SightScreens(GroundDefinition def)
        {
            var m = new MeshBuilder();
            var c = def.GroundCentre;
            foreach (float a in new[] { 0f, Mathf.PI })
            {
                var basePoint = GroundGeometry.BoundaryPoint(c, def.BoundaryRadii, a, def.sightScreenBeyondRope);
                var toCentre = new Vector3(-Mathf.Sin(a), 0f, -Mathf.Cos(a));
                var tangent = Vector3.Cross(Vector3.up, toCentre).normalized;
                m.Box(basePoint + Vector3.up * def.sightScreenHeight * 0.5f, tangent * def.sightScreenWidth * 0.5f,
                    Vector3.up * def.sightScreenHeight * 0.5f, toCentre * 0.15f, def.sightScreenColor);
            }
            return m.ToMesh("SightScreens");
        }

        /// <summary>True when angle <paramref name="a"/> (about the centre) at <paramref name="radius"/> lies in the open gap behind a sight screen.</summary>
        static bool InScreenGap(GroundDefinition def, float a, float radius)
        {
            float half = Mathf.Atan((def.sightScreenWidth * 0.5f + def.sightScreenGapExtra) / radius);
            float d0 = Mathf.Abs(Mathf.DeltaAngle(a * Mathf.Rad2Deg, 0f)) * Mathf.Deg2Rad;
            float d1 = Mathf.Abs(Mathf.DeltaAngle(a * Mathf.Rad2Deg, 180f)) * Mathf.Deg2Rad;
            return d0 < half || d1 < half;
        }

        static Mesh Boards(GroundDefinition def)
        {
            var m = new MeshBuilder();
            var c = def.GroundCentre;
            const int segs = 420;
            const float lean = 10f * Mathf.Deg2Rad; // tilted back slightly, like real boundary boards
            float arc = 0f;
            for (int i = 0; i < segs; i++)
            {
                float a0 = i * 2f * Mathf.PI / segs, a1 = (i + 1) * 2f * Mathf.PI / segs;
                float r0 = GroundGeometry.BoundaryRadiusAtAngle(def.BoundaryRadii, a0) + def.boardsBeyondRope;
                float r1 = GroundGeometry.BoundaryRadiusAtAngle(def.BoundaryRadii, a1) + def.boardsBeyondRope;
                var d0 = new Vector3(Mathf.Sin(a0), 0f, Mathf.Cos(a0));
                var d1 = new Vector3(Mathf.Sin(a1), 0f, Mathf.Cos(a1));
                var p0 = new Vector3(c.x, 0f, c.z) + d0 * r0;
                var p1 = new Vector3(c.x, 0f, c.z) + d1 * r1;
                float segLength = Vector3.Distance(p0, p1);
                float mid = (a0 + a1) * 0.5f;
                if (!InScreenGap(def, mid, (r0 + r1) * 0.5f) && def.boardColors.Length > 0)
                {
                    int panel = Mathf.FloorToInt((arc + segLength * 0.5f) / Mathf.Max(0.5f, def.boardPanelLength));
                    var color = def.boardColors[panel % def.boardColors.Length];
                    float h = def.boardHeight;
                    var up0 = d0 * (h * Mathf.Sin(lean)) + Vector3.up * (h * Mathf.Cos(lean));
                    var up1 = d1 * (h * Mathf.Sin(lean)) + Vector3.up * (h * Mathf.Cos(lean));
                    var facing = -(d0 + d1) + Vector3.up * 0.2f;
                    m.FlatQuad(p0, p1, p1 + up1, p0 + up0, facing, color);
                }
                arc += segLength;
            }
            return m.ToMesh("AdvertisingBoards");
        }

        static Mesh Stands(GroundDefinition def)
        {
            var m = new MeshBuilder();
            var c = def.GroundCentre;
            const int segs = 160;
            int tiers = Mathf.Max(1, def.standTiers);
            float tread = def.standDepth / tiers;
            float Height(int k) => tiers == 1 ? def.standTopHeight : def.standFrontHeight + (def.standTopHeight - def.standFrontHeight) * k / (tiers - 1);
            var concrete = new Color(0.55f, 0.55f, 0.56f);
            const float tile = 4f;           // crowd texture repeats every 4 m around the ring
            const float crowdV0 = 0.15f, crowdV1 = 0.95f, plainV = 0.03f;
            var rnd = new System.Random(17);

            for (int i = 0; i < segs; i++)
            {
                float a0 = i * 2f * Mathf.PI / segs, a1 = (i + 1) * 2f * Mathf.PI / segs;
                float rs0 = GroundGeometry.BoundaryRadiusAtAngle(def.BoundaryRadii, a0) + def.standsBeyondRope;
                float rs1 = GroundGeometry.BoundaryRadiusAtAngle(def.BoundaryRadii, a1) + def.standsBeyondRope;
                if (InScreenGap(def, (a0 + a1) * 0.5f, (rs0 + rs1) * 0.5f))
                    continue;
                var d0 = new Vector3(Mathf.Sin(a0), 0f, Mathf.Cos(a0));
                var d1 = new Vector3(Mathf.Sin(a1), 0f, Mathf.Cos(a1));
                var o = new Vector3(c.x, 0f, c.z);
                float u0 = a0 * rs0 / tile, u1 = a1 * rs0 / tile;
                // Slight per-section brightness variation so the crowd doesn't look like one repeated tile.
                float shade = 0.85f + 0.15f * (float)rnd.NextDouble();

                for (int k = 0; k < tiers; k++)
                {
                    float r0a = rs0 + k * tread, r0b = rs1 + k * tread;
                    float r1a = rs0 + (k + 1) * tread, r1b = rs1 + (k + 1) * tread;
                    float hPrev = k == 0 ? 0f : Height(k - 1), h = Height(k);
                    var toward = -(d0 + d1);

                    // Riser facing the pitch: the front wall is plain concrete, higher risers are crowd.
                    var b0 = o + d0 * r0a + Vector3.up * hPrev;
                    var b1 = o + d1 * r0b + Vector3.up * hPrev;
                    var t0 = o + d0 * r0a + Vector3.up * h;
                    var t1 = o + d1 * r0b + Vector3.up * h;
                    if (k == 0)
                        m.FlatQuad(b0, b1, t1, t0, toward, concrete, new Vector2(u0, plainV), new Vector2(u1, plainV), new Vector2(u1, plainV), new Vector2(u0, plainV));
                    else
                        m.FlatQuadColours(b0, b1, t1, t0, toward,
                            new[] { Grey(0.7f * shade), Grey(0.7f * shade), Grey(shade), Grey(shade) },
                            new Vector2(u0, crowdV0), new Vector2(u1, crowdV0), new Vector2(u1, crowdV1), new Vector2(u0, crowdV1));

                    // Tread (seating) facing up; darker toward the next riser (baked AO, GD-10).
                    var f0 = o + d0 * r0a + Vector3.up * h;
                    var f1 = o + d1 * r0b + Vector3.up * h;
                    var k0 = o + d0 * r1a + Vector3.up * h;
                    var k1 = o + d1 * r1b + Vector3.up * h;
                    m.FlatQuadColours(f0, f1, k1, k0, Vector3.up,
                        new[] { Grey(shade), Grey(shade), Grey(0.62f * shade), Grey(0.62f * shade) },
                        new Vector2(u0, crowdV0), new Vector2(u1, crowdV0), new Vector2(u1, crowdV1), new Vector2(u0, crowdV1));
                }

                // End caps where a section borders a sight-screen gap (visible from the crease).
                foreach (var (edge, a, rsEdge, side) in new[] { (i, a0, rs0, -1f), (i + 1, a1, rs1, 1f) })
                {
                    float other = side < 0 ? a0 - 2f * Mathf.PI / segs : a1 + 2f * Mathf.PI / segs;
                    if (!InScreenGap(def, (a + other) * 0.5f, rsEdge))
                        continue;
                    var d = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                    var tangent = Vector3.Cross(Vector3.up, d) * side; // points into the gap
                    for (int k = 0; k < tiers; k++)
                    {
                        float ra = rsEdge + k * tread, rb = rsEdge + def.standDepth;
                        float h = Height(k);
                        m.FlatQuad(o + d * ra, o + d * rb, o + d * rb + Vector3.up * h, o + d * ra + Vector3.up * h, tangent, concrete,
                            new Vector2(0f, plainV), new Vector2(0f, plainV), new Vector2(0f, plainV), new Vector2(0f, plainV));
                    }
                }
            }
            return m.ToMesh("Stands");
        }

        static Color Grey(float v) => new Color(v, v, v);

        static Mesh SkyDome(GroundDefinition def)
        {
            var m = new MeshBuilder();
            var c = def.GroundCentre;
            const int az = 32;
            float[] elevations = { -90f, -25f, -5f, 0f, 4f, 10f, 20f, 35f, 55f, 75f, 90f }; // closed: no default skybox shows through
            var rows = new List<int[]>();
            foreach (float e in elevations)
            {
                float er = e * Mathf.Deg2Rad;
                float t = e <= 0f ? 0f : Mathf.Pow(Mathf.Sin(er), 0.6f);
                var col = e < 0f ? def.skyHorizon * 0.92f : Color.Lerp(def.skyHorizon, def.skyZenith, t);
                col.a = 1f;
                var row = new int[az];
                for (int i = 0; i < az; i++)
                {
                    float a = i * 2f * Mathf.PI / az;
                    var dir = new Vector3(Mathf.Cos(er) * Mathf.Sin(a), Mathf.Sin(er), Mathf.Cos(er) * Mathf.Cos(a));
                    row[i] = m.Vertex(c + dir * def.skyDomeRadius, dir, Vector2.zero, col);
                }
                rows.Add(row);
            }
            // Faces point outward; the sky shader culls front faces, so the dome is seen from inside.
            for (int r = 0; r + 1 < rows.Count; r++)
                for (int i = 0; i < az; i++)
                {
                    int j = (i + 1) % az;
                    var facing = m.NormalOf(rows[r][i]) + m.NormalOf(rows[r + 1][j]);
                    m.Quad(rows[r][i], rows[r][j], rows[r + 1][j], rows[r + 1][i], facing);
                }
            return m.ToMesh("Sky");
        }

        static Mesh Fielders(GroundDefinition def, FieldConfig field, bool leftHanded)
        {
            var m = new MeshBuilder();
            var whites = new Color(0.93f, 0.93f, 0.89f);
            var skin = new Color(0.72f, 0.55f, 0.43f);
            var cap = new Color(0.10f, 0.16f, 0.35f);
            foreach (var f in field.fielders)
            {
                var p = GroundGeometry.FielderPosition(f.angleDegrees, f.radius, leftHanded);
                m.Cylinder(p, 0.2f, 1.45f, 8, whites);
                m.Sphere(p + Vector3.up * 1.6f, 0.12f, 8, 5, skin);
                m.Cylinder(p + Vector3.up * 1.66f, 0.125f, 0.07f, 8, cap);
            }
            return m.ToMesh(leftHanded ? "FieldersLeftHanded" : "FieldersRightHanded");
        }

        // ---------- Textures ----------

        /// <summary>One repeat: a light and a dark mowing stripe across U (stripes run down the ground), with seeded noise.</summary>
        static Texture2D OutfieldTexture(GroundDefinition def)
        {
            const int size = 1024;
            var tex = new Texture2D(size, size, TextureFormat.RGB24, true);
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size;
                    // Soft-edged stripes: smooth step over ~3% at each edge.
                    float s = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((Mathf.Abs(u - 0.5f) - 0.235f) / 0.03f));
                    var baseColor = Color.Lerp(def.grassLight, def.grassDark, 1f - s);
                    float n = Noise.Fractal(x, y, size, 7) * 0.08f - 0.04f;
                    px[y * size + x] = (Color32)new Color(baseColor.r + n, baseColor.g + n * 1.2f, baseColor.b + n * 0.6f);
                }
            tex.SetPixels32(px);
            return tex;
        }

        /// <summary>The strip with every crease painted in (no overlapping quads to z-fight). U across, V along the pitch.</summary>
        static Texture2D PitchTexture(GroundDefinition def)
        {
            const int w = 512, h = 4096;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, true);
            var px = new Color32[w * h];
            float width = def.pitchWidth, length = def.pitchEndZ - def.pitchStartZ;
            float lw = def.creaseLineWidth * 0.5f;
            float stumpsFar = def.pitchLength;
            for (int y = 0; y < h; y++)
            {
                float z = def.pitchStartZ + (y + 0.5f) / h * length;
                for (int x = 0; x < w; x++)
                {
                    float xm = -width * 0.5f + (x + 0.5f) / w * width;
                    // Worn, darker ends where bowlers land and batters stand.
                    float wear = Mathf.Max(Wear(z, def.poppingCreaseOffset, xm), Wear(z, stumpsFar - def.poppingCreaseOffset, xm));
                    var col = Color.Lerp(def.pitchColor, def.pitchWornColor, wear);
                    float n = Noise.Fractal(x, y, 512, 11) * 0.07f - 0.035f;
                    col = new Color(col.r + n, col.g + n, col.b + n * 0.8f);

                    float line = 0f;
                    foreach (float stumps in new[] { 0f, stumpsFar })
                    {
                        float toward = stumps == 0f ? 1f : -1f;
                        float popping = stumps + toward * def.poppingCreaseOffset;
                        line = Mathf.Max(line, Line(z, popping, lw));                                                     // popping crease, full strip width
                        if (Mathf.Abs(xm) <= def.bowlingCreaseHalfLength) line = Mathf.Max(line, Line(z, stumps, lw));     // bowling crease through the stumps
                        float back = popping - toward * def.returnCreaseBehindPopping;
                        if (z >= Mathf.Min(popping, back) - lw && z <= Mathf.Max(popping, back) + lw)                      // return creases
                            line = Mathf.Max(line, Mathf.Max(Line(xm, def.returnCreaseX, lw), Line(xm, -def.returnCreaseX, lw)));
                    }
                    px[y * w + x] = (Color32)Color.Lerp(col, def.creaseColor, line);
                }
            }
            tex.SetPixels32(px);
            return tex;
        }

        static float Line(float v, float centre, float halfWidth) => Mathf.Clamp01((halfWidth - Mathf.Abs(v - centre)) / 0.006f + 0.5f);

        static float Wear(float z, float popping, float x)
        {
            float dz = (z - popping) / 2.2f, dx = x / 0.9f;
            return Mathf.Clamp01(1f - (dz * dz + dx * dx)) * 0.6f;
        }

        /// <summary>Seated crowd: shirt-coloured specks with heads on dark seats; the bottom rows are plain concrete.</summary>
        static Texture2D CrowdTexture()
        {
            const int size = 256;
            var tex = new Texture2D(size, size, TextureFormat.RGB24, true);
            var px = new Color32[size * size];
            var rnd = new System.Random(5);
            Color[] shirts =
            {
                new Color(0.85f, 0.85f, 0.85f), new Color(0.15f, 0.25f, 0.6f), new Color(0.75f, 0.2f, 0.15f),
                new Color(0.9f, 0.75f, 0.2f), new Color(0.2f, 0.5f, 0.3f), new Color(0.35f, 0.35f, 0.4f), new Color(0.6f, 0.3f, 0.55f),
            };
            var seat = new Color(0.22f, 0.22f, 0.25f);
            var concrete = new Color(0.55f, 0.55f, 0.56f);
            const int cell = 8;
            for (int cy = 0; cy < size / cell; cy++)
                for (int cx = 0; cx < size / cell; cx++)
                {
                    bool occupied = rnd.NextDouble() < 0.82;
                    var shirt = shirts[rnd.Next(shirts.Length)] * (0.8f + 0.2f * (float)rnd.NextDouble());
                    var skin = new Color(0.55f, 0.4f, 0.3f) * (0.75f + 0.35f * (float)rnd.NextDouble());
                    for (int y = 0; y < cell; y++)
                        for (int x = 0; x < cell; x++)
                        {
                            int gy = cy * cell + y, gx = cx * cell + x;
                            Color c = seat;
                            if (gy < 16) c = concrete;
                            else if (occupied && x >= 1 && x <= 6)
                                c = y <= 4 ? shirt : (y <= 6 && x >= 2 && x <= 5 ? skin : seat);
                            px[gy * size + gx] = (Color32)c;
                        }
                }
            tex.SetPixels32(px);
            return tex;
        }

        static Texture2D SaveTexture(string name, Texture2D tex, TextureWrapMode wrap, int aniso)
        {
            var path = $"{GeneratedFolder}/{name}.png";
            var bytes = tex.EncodeToPNG();
            Object.DestroyImmediate(tex);
            if (!File.Exists(path) || !File.ReadAllBytes(path).SequenceEqual(bytes))
            {
                File.WriteAllBytes(path, bytes);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.mipmapEnabled = true;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = aniso;
            importer.wrapMode = wrap;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings
            {
                name = "Android",
                overridden = true,
                maxTextureSize = 4096,
                format = TextureImporterFormat.ASTC_6x6,
                textureCompression = TextureImporterCompression.Compressed,
            });
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // ---------- Materials and pieces ----------

        static Material GroundMaterial(string name, Texture2D texture, bool receiveShadows = false, float offsetFactor = 0f, float offsetUnits = 0f, float fogAmount = 1f)
        {
            var mat = Material(name, Shader.Find("CricketVR/GroundLit"));
            mat.SetTexture("_BaseMap", texture);
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_DepthOffsetFactor", offsetFactor);
            mat.SetFloat("_DepthOffsetUnits", offsetUnits);
            mat.SetFloat("_FogAmount", fogAmount);
            mat.SetFloat("_ReceiveShadows", receiveShadows ? 1f : 0f);
            if (receiveShadows) mat.EnableKeyword("_CVR_RECEIVE_SHADOWS");
            else mat.DisableKeyword("_CVR_RECEIVE_SHADOWS");
            mat.enableInstancing = false; // SRP Batcher handles these static meshes (GD-11)
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Material Material(string name, Shader shader)
        {
            if (shader == null)
                throw new System.InvalidOperationException($"[CricketVR] Shader for material '{name}' not found; check Assets/CricketVR/Shaders compiled.");
            var path = $"{MaterialsFolder}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            else if (mat.shader != shader)
            {
                mat.shader = shader;
            }
            return mat;
        }

        static GameObject Piece(GameObject parent, string name, Mesh mesh, Material material, bool receiveShadows = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = SaveMesh(mesh);
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = receiveShadows;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
            return go;
        }

        /// <summary>Writes the mesh into its asset, reusing the existing asset (stable GUID) when there is one.</summary>
        static Mesh SaveMesh(Mesh mesh)
        {
            var path = $"{GeneratedFolder}/{mesh.name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(mesh, path);
                return mesh;
            }
            EditorUtility.CopySerialized(mesh, existing);
            Object.DestroyImmediate(mesh);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        static T LoadOrCreate<T>(string path, List<string> created) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null)
                return asset;
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            created.Add(path);
            return asset;
        }

        // ---------- Helpers ----------

        /// <summary>Seeded value noise (no Random state), so textures are byte-identical on every rebuild.</summary>
        static class Noise
        {
            static float Hash(int x, int y, int seed)
            {
                unchecked
                {
                    uint h = (uint)(x * 374761393 + y * 668265263 + seed * 2147483647);
                    h = (h ^ (h >> 13)) * 1274126177u;
                    return (h ^ (h >> 16)) / (float)uint.MaxValue;
                }
            }

            static float Value(float x, float y, int period, int seed)
            {
                int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
                float fx = x - x0, fy = y - y0;
                fx = fx * fx * (3f - 2f * fx);
                fy = fy * fy * (3f - 2f * fy);
                int Wrap(int v) => ((v % period) + period) % period;
                float a = Hash(Wrap(x0), Wrap(y0), seed), b = Hash(Wrap(x0 + 1), Wrap(y0), seed);
                float c = Hash(Wrap(x0), Wrap(y0 + 1), seed), d = Hash(Wrap(x0 + 1), Wrap(y0 + 1), seed);
                return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
            }

            /// <summary>Tileable fractal noise in [0, 1] for a texture of <paramref name="size"/> pixels.</summary>
            public static float Fractal(int x, int y, int size, int seed)
            {
                float sum = 0f, amp = 0.5f, norm = 0f;
                for (int o = 0, period = 8; o < 4; o++, period *= 2)
                {
                    float scale = (float)period / size;
                    sum += amp * Value(x * scale, y * scale, period, seed + o);
                    norm += amp;
                    amp *= 0.5f;
                }
                return sum / norm;
            }
        }

        /// <summary>Small mesh accumulator. Colours are given in sRGB and stored linear (the project uses linear colour).</summary>
        class MeshBuilder
        {
            readonly List<Vector3> _v = new List<Vector3>();
            readonly List<Vector3> _n = new List<Vector3>();
            readonly List<Vector2> _uv = new List<Vector2>();
            readonly List<Color> _c = new List<Color>();
            readonly List<int> _t = new List<int>();

            public int Vertex(Vector3 p, Vector3 n, Vector2 uv, Color c)
            {
                _v.Add(p);
                _n.Add(n.normalized);
                _uv.Add(uv);
                _c.Add(c.linear);
                return _v.Count - 1;
            }

            public Vector3 NormalOf(int i) => _n[i];

            /// <summary>Adds a triangle wound so its front face points along <paramref name="facing"/>.</summary>
            public void Triangle(int a, int b, int c, Vector3 facing)
            {
                var n = Vector3.Cross(_v[b] - _v[a], _v[c] - _v[a]);
                if (Vector3.Dot(n, facing) >= 0f) { _t.Add(a); _t.Add(b); _t.Add(c); }
                else { _t.Add(a); _t.Add(c); _t.Add(b); }
            }

            public void Quad(int a, int b, int c, int d, Vector3 facing)
            {
                Triangle(a, b, c, facing);
                Triangle(a, c, d, facing);
            }

            public void FlatQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 facing, Color color) =>
                FlatQuad(a, b, c, d, facing, color, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);

            public void FlatQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 facing, Color color, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud) =>
                FlatQuadColours(a, b, c, d, facing, new[] { color, color, color, color }, ua, ub, uc, ud);

            public void FlatQuadColours(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 facing, Color[] colors, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
            {
                var n = Vector3.Cross(b - a, c - a).normalized;
                if (Vector3.Dot(n, facing) < 0f) n = -n;
                int ia = Vertex(a, n, ua, colors[0]), ib = Vertex(b, n, ub, colors[1]);
                int ic = Vertex(c, n, uc, colors[2]), id = Vertex(d, n, ud, colors[3]);
                Quad(ia, ib, ic, id, n);
            }

            /// <summary>Axis-aligned-in-its-own-frame box: centre plus half-extent vectors.</summary>
            public void Box(Vector3 centre, Vector3 hx, Vector3 hy, Vector3 hz, Color color)
            {
                foreach (var (axis, u, v) in new[] { (hx, hy, hz), (-hx, hy, hz), (hy, hx, hz), (-hy, hx, hz), (hz, hx, hy), (-hz, hx, hy) })
                {
                    var f = centre + axis;
                    FlatQuad(f - u - v, f + u - v, f + u + v, f - u + v, axis, color);
                }
            }

            public void Cylinder(Vector3 basePoint, float radius, float height, int sides, Color color)
            {
                var top = basePoint + Vector3.up * height;
                int topCentre = Vertex(top, Vector3.up, Vector2.zero, color);
                for (int i = 0; i < sides; i++)
                {
                    float a0 = i * 2f * Mathf.PI / sides, a1 = (i + 1) * 2f * Mathf.PI / sides;
                    var o0 = new Vector3(Mathf.Sin(a0), 0f, Mathf.Cos(a0)) * radius;
                    var o1 = new Vector3(Mathf.Sin(a1), 0f, Mathf.Cos(a1)) * radius;
                    FlatQuad(basePoint + o0, basePoint + o1, top + o1, top + o0, o0 + o1, color);
                    int t0 = Vertex(top + o0, Vector3.up, Vector2.zero, color), t1 = Vertex(top + o1, Vector3.up, Vector2.zero, color);
                    Triangle(topCentre, t0, t1, Vector3.up);
                }
            }

            public void Sphere(Vector3 centre, float radius, int segments, int rings, Color color)
            {
                var rows = new int[rings + 1][];
                for (int r = 0; r <= rings; r++)
                {
                    float lat = Mathf.PI * r / rings - Mathf.PI * 0.5f;
                    rows[r] = new int[segments];
                    for (int i = 0; i < segments; i++)
                    {
                        float a = i * 2f * Mathf.PI / segments;
                        var n = new Vector3(Mathf.Cos(lat) * Mathf.Sin(a), Mathf.Sin(lat), Mathf.Cos(lat) * Mathf.Cos(a));
                        rows[r][i] = Vertex(centre + n * radius, n, Vector2.zero, color);
                    }
                }
                for (int r = 0; r < rings; r++)
                    for (int i = 0; i < segments; i++)
                    {
                        int j = (i + 1) % segments;
                        var facing = _n[rows[r][i]] + _n[rows[r + 1][j]];
                        Quad(rows[r][i], rows[r][j], rows[r + 1][j], rows[r + 1][i], facing);
                    }
            }

            public Mesh ToMesh(string name)
            {
                var mesh = new Mesh { name = name };
                if (_v.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
                mesh.SetVertices(_v);
                mesh.SetNormals(_n);
                mesh.SetUVs(0, _uv);
                mesh.SetColors(_c);
                mesh.SetTriangles(_t, 0);
                mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
