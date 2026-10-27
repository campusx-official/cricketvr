using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using FD = CricketVR.FieldDimensions;

namespace CricketVR.Editor
{
    /// <summary>
    /// M2 environment: pitch, creases, stumps, launcher, net enclosure, stance marker and scoreboard placement.
    /// Called by <see cref="MainSceneBuilder"/>. Each piece is created only if an object with its name is missing.
    /// Visual primitives have their default colliders removed; physics uses explicit, named colliders only.
    /// </summary>
    static class PracticeNetBuilder
    {
        const string MaterialsFolder = "Assets/CricketVR/Materials";

        public const string PitchLayer = "Pitch";
        public const string BoundaryLayer = "Boundary";
        public const string StumpsLayer = "Stumps";

        public static void Build(GameObject main, GameObject environment, List<string> created)
        {
            EnsureLayers(created, PitchLayer, BoundaryLayer, StumpsLayer);
            EnsureGround(environment, created);
            EnsurePitch(environment, created);
            EnsureStumps(environment, created);
            EnsureLauncher(environment, created);
            EnsureNet(environment, created);
            EnsureBattingAnchor(environment, created);
            EnsureScoreboardPlaceholder(main, created);
        }

        // ---------- Layers ----------

        public static void EnsureLayers(List<string> created, params string[] names)
        {
            foreach (var name in names)
            {
                if (LayerMask.NameToLayer(name) >= 0)
                    continue;
                var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
                var layers = tagManager.FindProperty("layers");
                // 0-5 are Unity built-ins; user layers start at 6.
                for (int i = 6; i < layers.arraySize; i++)
                {
                    var slot = layers.GetArrayElementAtIndex(i);
                    if (string.IsNullOrEmpty(slot.stringValue))
                    {
                        slot.stringValue = name;
                        tagManager.ApplyModifiedPropertiesWithoutUndo();
                        created.Add($"layer {i} '{name}'");
                        break;
                    }
                }
            }
        }

        static int Layer(string name)
        {
            int layer = LayerMask.NameToLayer(name);
            if (layer < 0)
                Debug.LogError($"[CricketVR] Layer '{name}' missing; add it in Edit > Project Settings > Tags and Layers.");
            return Mathf.Max(layer, 0);
        }

        // ---------- Ground and pitch ----------

        static void EnsureGround(GameObject environment, List<string> created)
        {
            var floor = environment.transform.Find("Floor");
            if (floor == null)
                return;
            // Migrate the untouched M1 floor (30 m square at origin) so it covers the whole net lane.
            if (floor.localPosition == Vector3.zero && floor.localScale == new Vector3(3f, 1f, 3f))
            {
                floor.localPosition = new Vector3(0f, 0f, 10f);
                floor.localScale = new Vector3(5f, 1f, 5f);
                created.Add("resized Floor to 50 m");
            }
            if (floor.gameObject.layer != Layer(PitchLayer))
                floor.gameObject.layer = Layer(PitchLayer);

            // M1 scale references stand in the delivery lane / swing space; the stumps and pitch now give scale.
            var refs = environment.transform.Find("ScaleReferences");
            if (refs != null && refs.gameObject.activeSelf)
            {
                refs.gameObject.SetActive(false);
                created.Add("deactivated ScaleReferences");
            }
        }

        static void EnsurePitch(GameObject environment, List<string> created)
        {
            if (environment.transform.Find("Pitch") != null)
                return;

            var pitch = new GameObject("Pitch") { layer = Layer(PitchLayer) };
            pitch.transform.SetParent(environment.transform, false);
            created.Add("Pitch");

            // Surface: top face at PitchSurfaceHeight so it sits just above the outfield plane without z-fighting.
            float length = FD.PitchEndZ - FD.PitchStartZ;
            var surface = CreateVisual(PrimitiveType.Cube, "Surface", pitch.transform,
                new Vector3(0f, FD.PitchSurfaceHeight - 0.05f, (FD.PitchStartZ + FD.PitchEndZ) * 0.5f),
                new Vector3(FD.PitchWidth, 0.1f, length), Mat("Pitch", new Color(0.78f, 0.70f, 0.52f)));
            surface.AddComponent<BoxCollider>();
            surface.layer = Layer(PitchLayer);

            var markings = new GameObject("Markings");
            markings.transform.SetParent(pitch.transform, false);
            AddCreaseSet(markings.transform, "StrikerEnd", 0f, +1f);
            AddCreaseSet(markings.transform, "BowlerEnd", FD.PitchLength, -1f);

            // Non-striker stumps: a visual depth cue behind the release point; no collider.
            CreateStumpSet("NonStrikerStumps", pitch.transform, FD.NonStrikerStumpsZ);
        }

        /// <param name="towardPitch">+1 if the popping crease lies at +Z from the stumps, -1 if at -Z.</param>
        static void AddCreaseSet(Transform parent, string name, float stumpsZ, float towardPitch)
        {
            var set = new GameObject(name);
            set.transform.SetParent(parent, false);
            var white = Mat("CreaseWhite", Color.white);
            const float y = 0.003f, h = 0.001f;
            float w = FD.CreaseLineWidth;
            float poppingZ = stumpsZ + towardPitch * FD.PoppingCreaseOffset;

            CreateVisual(PrimitiveType.Cube, "BowlingCrease", set.transform,
                new Vector3(0f, y, stumpsZ), new Vector3(FD.BowlingCreaseHalfLength * 2f, h, w), white);
            CreateVisual(PrimitiveType.Cube, "PoppingCrease", set.transform,
                new Vector3(0f, y, poppingZ), new Vector3(FD.PoppingCreaseHalfLength * 2f, h, w), white);

            // Return creases run from the popping crease back past the stumps.
            float backZ = stumpsZ - towardPitch * FD.PoppingCreaseOffset;
            float midZ = (poppingZ + backZ) * 0.5f, len = Mathf.Abs(poppingZ - backZ);
            CreateVisual(PrimitiveType.Cube, "ReturnCreaseLeft", set.transform,
                new Vector3(-FD.ReturnCreaseX, y, midZ), new Vector3(w, h, len), white);
            CreateVisual(PrimitiveType.Cube, "ReturnCreaseRight", set.transform,
                new Vector3(FD.ReturnCreaseX, y, midZ), new Vector3(w, h, len), white);
        }

        // ---------- Stumps ----------

        static void EnsureStumps(GameObject environment, List<string> created)
        {
            if (environment.transform.Find("Stumps") != null)
                return;

            var stumps = CreateStumpSet("Stumps", environment.transform, 0f);
            stumps.layer = Layer(StumpsLayer);
            // One box covering the three stumps: cheap, and a ball touching any stump hits it (wicket detection in M6).
            var box = stumps.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, FD.StumpHeight * 0.5f, 0f);
            box.size = new Vector3(FD.StumpSetWidth, FD.StumpHeight, FD.StumpDiameter);
            created.Add("Stumps");
        }

        static GameObject CreateStumpSet(string name, Transform parent, float z)
        {
            var set = new GameObject(name);
            set.transform.SetParent(parent, false);
            set.transform.localPosition = new Vector3(0f, 0f, z);
            var wood = Mat("StumpWood", new Color(0.96f, 0.92f, 0.80f));

            float spacing = (FD.StumpSetWidth - FD.StumpDiameter) * 0.5f;
            foreach (var (label, x) in new[] { ("Off", spacing), ("Middle", 0f), ("Leg", -spacing) })
            {
                // Cylinder primitive is 2 m tall and 1 m wide at scale 1.
                CreateVisual(PrimitiveType.Cylinder, label + "Stump", set.transform,
                    new Vector3(x, FD.StumpHeight * 0.5f, 0f),
                    new Vector3(FD.StumpDiameter, FD.StumpHeight * 0.5f, FD.StumpDiameter), wood);
            }
            float bailY = FD.StumpHeight + FD.BailHeightAboveStumps * 0.5f;
            foreach (var (label, x) in new[] { ("OffBail", spacing * 0.5f), ("LegBail", -spacing * 0.5f) })
            {
                var bail = CreateVisual(PrimitiveType.Cylinder, label, set.transform,
                    new Vector3(x, bailY, 0f), new Vector3(0.012f, FD.BailLength * 0.5f, 0.012f), wood);
                bail.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            }
            return set;
        }

        // ---------- Launcher ----------

        static void EnsureLauncher(GameObject environment, List<string> created)
        {
            if (environment.transform.Find("LauncherAnchor") != null)
                return;

            // The anchor IS the release point; the machine body sits behind it. No colliders: returning balls pass through.
            var anchor = new GameObject("LauncherAnchor");
            anchor.transform.SetParent(environment.transform, false);
            anchor.transform.localPosition = FD.DefaultReleasePoint;
            created.Add("LauncherAnchor");

            var dark = Mat("LauncherDark", new Color(0.12f, 0.13f, 0.15f));
            var visual = new GameObject("LauncherVisual");
            visual.transform.SetParent(anchor.transform, false);
            CreateVisual(PrimitiveType.Cube, "Body", visual.transform,
                new Vector3(0f, 0f, 0.3f), new Vector3(0.36f, 0.36f, 0.6f), dark);
            CreateVisual(PrimitiveType.Cylinder, "Mouth", visual.transform,
                new Vector3(0f, 0f, 0.02f), new Vector3(0.12f, 0.02f, 0.12f), Mat("LauncherMouth", new Color(0.9f, 0.35f, 0.1f)))
                .transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            float standHeight = FD.DefaultReleasePoint.y - 0.18f;
            CreateVisual(PrimitiveType.Cylinder, "Stand", visual.transform,
                new Vector3(0f, -FD.DefaultReleasePoint.y + standHeight * 0.5f, 0.3f),
                new Vector3(0.08f, standHeight * 0.5f, 0.08f), dark);
        }

        // ---------- Net ----------

        static void EnsureNet(GameObject environment, List<string> created)
        {
            float length = FD.NetFrontZ - FD.NetRearZ;
            float midZ = (FD.NetRearZ + FD.NetFrontZ) * 0.5f;
            float width = FD.NetHalfWidth * 2f;
            float t = FD.NetWallThickness;

            if (environment.transform.Find("NetBoundaryColliders") == null)
            {
                var colliders = new GameObject("NetBoundaryColliders") { layer = Layer(BoundaryLayer) };
                colliders.transform.SetParent(environment.transform, false);
                // Inner faces sit exactly on the documented net planes; thickness extends outward.
                AddWall(colliders.transform, "LeftWall", new Vector3(-FD.NetHalfWidth - t / 2f, FD.NetSideHeight / 2f, midZ), new Vector3(t, FD.NetSideHeight, length + 2f * t));
                AddWall(colliders.transform, "RightWall", new Vector3(FD.NetHalfWidth + t / 2f, FD.NetSideHeight / 2f, midZ), new Vector3(t, FD.NetSideHeight, length + 2f * t));
                AddWall(colliders.transform, "RearWall", new Vector3(0f, FD.NetRearHeight / 2f, FD.NetRearZ - t / 2f), new Vector3(width, FD.NetRearHeight, t));
                AddWall(colliders.transform, "FrontWall", new Vector3(0f, FD.NetFrontHeight / 2f, FD.NetFrontZ + t / 2f), new Vector3(width, FD.NetFrontHeight, t));
                created.Add("NetBoundaryColliders");
            }

            if (environment.transform.Find("NetFrameAndVisuals") == null)
            {
                var visuals = new GameObject("NetFrameAndVisuals");
                visuals.transform.SetParent(environment.transform, false);
                // Tinted translucent panels instead of a fine mesh texture: a thin grid aliases badly on Quest.
                var panel = TransparentMat("NetPanel", new Color(0.08f, 0.10f, 0.12f, 0.35f));
                AddPanel(visuals.transform, "LeftPanel", new Vector3(-FD.NetHalfWidth, FD.NetSideHeight / 2f, midZ), Quaternion.Euler(0f, 90f, 0f), new Vector3(length, FD.NetSideHeight, 1f), panel);
                AddPanel(visuals.transform, "RightPanel", new Vector3(FD.NetHalfWidth, FD.NetSideHeight / 2f, midZ), Quaternion.Euler(0f, 90f, 0f), new Vector3(length, FD.NetSideHeight, 1f), panel);
                AddPanel(visuals.transform, "RearPanel", new Vector3(0f, FD.NetRearHeight / 2f, FD.NetRearZ), Quaternion.identity, new Vector3(width, FD.NetRearHeight, 1f), panel);
                AddPanel(visuals.transform, "FrontPanel", new Vector3(0f, FD.NetFrontHeight / 2f, FD.NetFrontZ), Quaternion.identity, new Vector3(width, FD.NetFrontHeight, 1f), panel);

                var frame = Mat("NetFrame", new Color(0.2f, 0.2f, 0.22f));
                const float poleDiameter = 0.06f;
                const int sections = 6;
                for (int i = 0; i <= sections; i++)
                {
                    float z = FD.NetRearZ + length * i / sections;
                    float h = i == sections ? FD.NetFrontHeight : FD.NetSideHeight;
                    foreach (var x in new[] { -FD.NetHalfWidth, FD.NetHalfWidth })
                        CreateVisual(PrimitiveType.Cylinder, $"Pole_{(x < 0 ? "L" : "R")}{i}", visuals.transform,
                            new Vector3(x, h / 2f, z), new Vector3(poleDiameter, h / 2f, poleDiameter), frame);
                }
                foreach (var x in new[] { -FD.NetHalfWidth, FD.NetHalfWidth })
                    CreateVisual(PrimitiveType.Cube, $"TopRail_{(x < 0 ? "L" : "R")}", visuals.transform,
                        new Vector3(x, FD.NetSideHeight, midZ), new Vector3(0.04f, 0.04f, length), frame);
                CreateVisual(PrimitiveType.Cube, "TopRail_Front", visuals.transform,
                    new Vector3(0f, FD.NetFrontHeight, FD.NetFrontZ), new Vector3(width, 0.04f, 0.04f), frame);
                CreateVisual(PrimitiveType.Cube, "TopRail_Rear", visuals.transform,
                    new Vector3(0f, FD.NetRearHeight, FD.NetRearZ), new Vector3(width, 0.04f, 0.04f), frame);
                created.Add("NetFrameAndVisuals");
            }
        }

        static void AddWall(Transform parent, string name, Vector3 center, Vector3 size)
        {
            var go = new GameObject(name) { layer = parent.gameObject.layer };
            go.transform.SetParent(parent, false);
            go.transform.localPosition = center;
            go.AddComponent<BoxCollider>().size = size;
        }

        static void AddPanel(Transform parent, string name, Vector3 pos, Quaternion rot, Vector3 scale, Material mat)
        {
            var quad = CreateVisual(PrimitiveType.Quad, name, parent, pos, scale, mat);
            quad.transform.localRotation = rot;
            var r = quad.GetComponent<MeshRenderer>();
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        // ---------- Batting anchor and scoreboard ----------

        static void EnsureBattingAnchor(GameObject environment, List<string> created)
        {
            if (environment.transform.Find("BattingAnchor") != null)
                return;

            // BattingAnchor marks the middle of the striker's popping crease; stance markers show where to stand.
            var anchor = new GameObject("BattingAnchor");
            anchor.transform.SetParent(environment.transform, false);
            anchor.transform.localPosition = new Vector3(0f, 0f, FD.PoppingCreaseOffset);
            created.Add("BattingAnchor");

            AddStanceMarker(anchor.transform, "StanceMarker_RightHanded", FD.RightHandedStance, Mat("StanceRight", new Color(1f, 0.85f, 0.1f)));
            AddStanceMarker(anchor.transform, "StanceMarker_LeftHanded", FD.LeftHandedStance, Mat("StanceLeft", new Color(0.2f, 0.8f, 1f)));
        }

        static void AddStanceMarker(Transform anchor, string name, Vector3 worldFloorPoint, Material mat)
        {
            // Thin disc, 0.35 m across, just above the pitch surface.
            var disc = CreateVisual(PrimitiveType.Cylinder, name, anchor, Vector3.zero, new Vector3(0.35f, 0.001f, 0.35f), mat);
            disc.transform.position = new Vector3(worldFloorPoint.x, 0.004f, worldFloorPoint.z);
        }

        static void EnsureScoreboardPlaceholder(GameObject main, List<string> created)
        {
            var ui = main.transform.Find("UI");
            if (ui == null)
            {
                ui = new GameObject("UI").transform;
                ui.SetParent(main.transform, false);
                created.Add("UI");
            }
            if (ui.Find("LiveScoreboard") != null)
                return;

            // Placement only (text arrives in M7): about 2.4 m from a right-hander's stance, 45 degrees toward
            // the off side, a little above standing eye height. Off the line from the stance to the release point.
            var head = FD.RightHandedStance + Vector3.up * 1.6f;
            var dir = new Vector3(Mathf.Sin(45f * Mathf.Deg2Rad), 0f, Mathf.Cos(45f * Mathf.Deg2Rad));
            var pos = new Vector3(FD.RightHandedStance.x, 1.9f, FD.RightHandedStance.z) + dir * 2.4f;

            var board = CreateVisual(PrimitiveType.Cube, "LiveScoreboard", ui, pos, new Vector3(1.0f, 0.6f, 0.03f),
                Mat("ScoreboardBack", new Color(0.05f, 0.08f, 0.18f)));
            board.transform.rotation = Quaternion.LookRotation(pos - head);
            created.Add("LiveScoreboard (placeholder)");
        }

        // ---------- Helpers ----------

        public static GameObject CreateVisual(PrimitiveType type, string name, Transform parent, Vector3 localPos, Vector3 localScale, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        public static Material Mat(string name, Color color)
        {
            var path = $"{MaterialsFolder}/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
                return existing;
            Directory.CreateDirectory(MaterialsFolder);
            var mat = new Material(Shader.Find("Universal Render Pipeline/Simple Lit")) { color = color };
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        static Material TransparentMat(string name, Color color)
        {
            var path = $"{MaterialsFolder}/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
                return existing;
            Directory.CreateDirectory(MaterialsFolder);
            var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            // URP Unlit transparent, alpha-blended, double-sided (panels are seen from inside and outside).
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_Cull", (float)CullMode.Off);
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            mat.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)RenderQueue.Transparent;
            mat.SetColor("_BaseColor", color);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }
    }
}
