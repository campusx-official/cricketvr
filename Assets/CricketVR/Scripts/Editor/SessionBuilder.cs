using System.Collections.Generic;
using System.Linq;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace CricketVR.Editor
{
    /// <summary>
    /// M7 session framework (plan.md §16): SessionManager, the near panel (start menu, pause, end screen, bat setup),
    /// the session text on the far scoreboard, the XRI event system, the right-controller menu ray and the bat-tap
    /// fallback. The near panel is generated content and is rebuilt on every run; shared objects are only wired.
    /// </summary>
    static class SessionBuilder
    {
        const float CanvasScale = 0.001f; // 1 canvas unit = 1 mm
        static readonly Vector2 PanelSize = new Vector2(820f, 660f);
        static readonly Color PanelColor = new Color(0.05f, 0.08f, 0.16f, 1f);
        static readonly Color ButtonColor = new Color(0.16f, 0.22f, 0.36f);
        static readonly Color StartColor = new Color(0.13f, 0.50f, 0.25f);

        public static void Build(GameObject main, GameConfig config, List<string> created)
        {
            var gameplay = main.transform.Find("Gameplay");
            var uiRoot = main.transform.Find("UI");
            if (gameplay == null || uiRoot == null)
            {
                Debug.LogError("[CricketVR] Main/Gameplay or Main/UI missing; run the earlier builder steps first.");
                return;
            }

            EnsureEventSystem(uiRoot, created);
            var (panel, title, body, hint, menu, net, match, end, pause, buttons) = BuildNearPanel(uiRoot, created);
            var boardText = EnsureBoardText(uiRoot, created);
            var readout = uiRoot.Find("LiveScoreboard/DebugReadout")?.gameObject;

            var go = gameplay.Find("SessionManager")?.gameObject;
            if (go == null)
            {
                go = new GameObject("SessionManager");
                go.transform.SetParent(gameplay, false);
                created.Add("SessionManager");
            }
            var ui = GetOrAdd<SessionUI>(go);
            ui.Configure(panel, title, body, hint, menu, net, match, end, pause, buttons, boardText, readout);
            var pointer = GetOrAdd<MenuPointer>(go);
            var (ray, off) = FindInteractors();
            pointer.Configure(ray, off);
            var log = GetOrAdd<BallLogWriter>(go);
            log.Configure(config);

            var bat = Find<BatController>(main, "Player/Bat");
            GetOrAdd<BatTapButtons>(go).Configure(bat, ui);

            var session = GetOrAdd<SessionManager>(go);
            session.Configure(config,
                Find<BowlingMachine>(main, "Gameplay/BowlingMachine"),
                Find<BallController>(main, "Gameplay/Ball"),
                Find<ScoreManager>(main, "Gameplay/ScoreManager"),
                Find<BatBallContactResolver>(main, "Gameplay/BatBallContact"),
                bat,
                Object.FindAnyObjectByType<HandednessSetup>(FindObjectsInactive.Include),
                Find<EnvironmentModeSwitch>(main, "Environment"),
                Find<BowlingDebugControls>(main, "Gameplay/BowlingMachine"),
                Object.FindAnyObjectByType<BatSetupControls>(FindObjectsInactive.Include),
                ui, pointer, log,
                Object.FindAnyObjectByType<XROrigin>(FindObjectsInactive.Include)?.transform);
            session.ConfigureWicket(main.GetComponentInChildren<WicketReaction>(true));
            foreach (var c in go.GetComponents<MonoBehaviour>())
                EditorUtility.SetDirty(c);
            if (ray == null)
                Debug.LogError("[CricketVR] Right controller Near-Far Interactor not found under the XR Origin; the menu ray won't work (bat-tap still will).");
        }

        // ---------- XRI ----------

        static void EnsureEventSystem(Transform uiRoot, List<string> created)
        {
            var existing = Object.FindAnyObjectByType<EventSystem>(FindObjectsInactive.Include);
            var go = existing != null ? existing.gameObject : null;
            if (go == null)
            {
                go = new GameObject("EventSystem");
                go.transform.SetParent(uiRoot, false);
                go.AddComponent<EventSystem>();
                created.Add("EventSystem");
            }
            // XRI's input module drives world-space UI from tracked controllers (and still supports mouse in the Editor).
            foreach (var other in go.GetComponents<BaseInputModule>())
                if (!(other is XRUIInputModule))
                    Object.DestroyImmediate(other);
            if (!go.TryGetComponent<XRUIInputModule>(out _))
                go.AddComponent<XRUIInputModule>();
        }

        /// <summary>The Starter Assets rig: right Near-Far ray for menus; every other interactor stays off.</summary>
        static (GameObject ray, GameObject[] off) FindInteractors()
        {
            var origin = Object.FindAnyObjectByType<XROrigin>(FindObjectsInactive.Include);
            if (origin == null)
                return (null, new GameObject[0]);
            var all = origin.GetComponentsInChildren<Transform>(true);
            GameObject Under(string controller, string interactor) => all.FirstOrDefault(t => t.name == interactor && HasAncestor(t, controller))?.gameObject;
            var ray = Under("Right Controller", "Near-Far Interactor");
            var off = new[]
            {
                Under("Left Controller", "Near-Far Interactor"),
                Under("Left Controller", "Poke Interactor"), Under("Right Controller", "Poke Interactor"),
                Under("Left Controller", "Teleport Interactor"), Under("Right Controller", "Teleport Interactor"),
            }.Where(g => g != null).ToArray();
            return (ray, off);
        }

        static bool HasAncestor(Transform t, string name)
        {
            for (var p = t.parent; p != null; p = p.parent)
                if (p.name == name) return true;
            return false;
        }

        // ---------- Near panel ----------

        static (GameObject, TMP_Text, TMP_Text, TMP_Text, GameObject, GameObject, GameObject, GameObject, GameObject, Button[])
            BuildNearPanel(Transform uiRoot, List<string> created)
        {
            // Rebuilt in place (stable object IDs): an unchanged layout leaves the scene file unchanged.
            var tree = new GeneratedTree(uiRoot, "NearPanel", typeof(RectTransform), typeof(Canvas), typeof(TrackedDeviceGraphicRaycaster));
            var panel = tree.Root;
            if (tree.CreatedRoot)
                created.Add("NearPanel");
            var rt = (RectTransform)panel.transform;
            rt.sizeDelta = PanelSize;
            rt.localScale = Vector3.one * CanvasScale;
            rt.position = FieldDimensions.RightHandedStance + new Vector3(0f, 1.2f, 0.75f); // SessionUI re-places it at runtime
            rt.rotation = Quaternion.Euler(20f, 0f, 0f);
            var canvas = panel.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = Camera.main;
            Image(tree, panel.transform, "Background", Vector2.zero, PanelSize, PanelColor);

            var title = Text(tree, panel.transform, "Title", new Vector2(0f, 280f), new Vector2(760f, 70f), 46f, FontStyles.Bold);
            var body = Text(tree, panel.transform, "Body", new Vector2(0f, 40f), new Vector2(760f, 400f), 30f, FontStyles.Normal);
            body.enableAutoSizing = true;
            body.fontSizeMin = 14f;
            body.fontSizeMax = 30f;
            var hint = Text(tree, panel.transform, "Hint", new Vector2(0f, -295f), new Vector2(780f, 50f), 22f, FontStyles.Italic);

            var buttons = new List<Button>();
            var menu = Group(tree, panel.transform, "MenuGroup");
            buttons.Add(MakeButton(tree, menu, "NetPractice", "Net Practice", new Vector2(-200f, 195f), new Vector2(370f, 80f)));
            buttons.Add(MakeButton(tree, menu, "MatchSim", "Match Simulation", new Vector2(200f, 195f), new Vector2(370f, 80f)));

            // Rows 65 apart (buttons 60 high): mode, overs/difficulty, bowler, wicket (Net Practice), batting, bat, Start.
            var net = Group(tree, menu, "NetOptions");
            Label(tree, net, "Overs", new Vector2(-300f, 120f));
            int i = 0;
            foreach (var o in new[] { 1, 2, 3, 5 })
                buttons.Add(MakeButton(tree, net, "Overs" + o, o.ToString(), new Vector2(-140f + 120f * i++, 120f), new Vector2(100f, 60f)));
            // Bowler (owner request 2026-10-27): pace (Stock) or one spin type for the whole session.
            Label(tree, net, "Bowler", new Vector2(-300f, 55f));
            i = 0;
            foreach (var bowler in NetBowler.All)
                buttons.Add(MakeButton(tree, net, bowler.Button, bowler.Label, new Vector2(-110f + 170f * i++, 55f), new Vector2(155f, 60f)));
            // Wicket (owner request 2026-10-27): a right-arm bowler's release, over or around the wicket.
            Label(tree, net, "Wicket", new Vector2(-300f, -10f));
            i = 0;
            foreach (var side in WicketSide.All)
                buttons.Add(MakeButton(tree, net, side.Button, side.Label, new Vector2(-110f + 170f * i++, -10f), new Vector2(155f, 60f)));

            var match = Group(tree, menu, "MatchOptions");
            Label(tree, match, "Difficulty", new Vector2(-300f, 120f));
            i = 0;
            foreach (var d in new[] { "Easy", "Medium", "Hard" })
                buttons.Add(MakeButton(tree, match, d, d, new Vector2(-110f + 170f * i++, 120f), new Vector2(155f, 60f)));

            Label(tree, menu, "Batting", new Vector2(-300f, -75f));
            buttons.Add(MakeButton(tree, menu, "RightHanded", "Right-handed", new Vector2(-120f, -75f), new Vector2(190f, 60f)));
            buttons.Add(MakeButton(tree, menu, "LeftHanded", "Left-handed", new Vector2(85f, -75f), new Vector2(190f, 60f)));
            // Bat (owner request 2026-11-01): the controller in the hand, or strapped to a real bat (bat mode).
            Label(tree, menu, "Bat", new Vector2(-300f, -140f));
            buttons.Add(MakeButton(tree, menu, "BatInHand", "In hand", new Vector2(-120f, -140f), new Vector2(190f, 60f)));
            buttons.Add(MakeButton(tree, menu, "BatStrapped", "Real bat", new Vector2(85f, -140f), new Vector2(190f, 60f)));
            buttons.Add(MakeButton(tree, menu, "BatSetup", "Bat setup", new Vector2(290f, -140f), new Vector2(190f, 60f)));
            buttons.Add(MakeButton(tree, menu, "Start", "Start", new Vector2(0f, -222f), new Vector2(420f, 80f), StartColor));

            var end = Group(tree, panel.transform, "EndGroup");
            buttons.Add(MakeButton(tree, end, "PlayAgain", "Play again", new Vector2(-260f, -215f), new Vector2(240f, 85f), StartColor));
            buttons.Add(MakeButton(tree, end, "Menu", "Menu", new Vector2(0f, -215f), new Vector2(240f, 85f)));
            buttons.Add(MakeButton(tree, end, "NewScenario", "New scenario", new Vector2(260f, -215f), new Vector2(240f, 85f)));

            var pause = Group(tree, panel.transform, "PauseGroup");
            buttons.Add(MakeButton(tree, pause, "Resume", "Resume", new Vector2(-140f, -215f), new Vector2(260f, 85f), StartColor));
            buttons.Add(MakeButton(tree, pause, "Menu", "Menu", new Vector2(140f, -215f), new Vector2(260f, 85f)));

            end.gameObject.SetActive(false);
            pause.gameObject.SetActive(false);
            match.gameObject.SetActive(false);
            // Hidden in the saved scene: it stands in the delivery sight line and is shown only while a menu is open.
            panel.SetActive(false);
            tree.RemoveUnused();
            return (panel, title, body, hint, menu.gameObject, net.gameObject, match.gameObject, end.gameObject, pause.gameObject, buttons.ToArray());
        }

        static Transform Group(GeneratedTree tree, Transform parent, string name)
        {
            var go = tree.Child(parent, name, typeof(RectTransform));
            ((RectTransform)go.transform).sizeDelta = PanelSize;
            return go.transform;
        }

        static Image Image(GeneratedTree tree, Transform parent, string name, Vector2 pos, Vector2 size, Color color)
        {
            var go = tree.Child(parent, name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var img = go.GetComponent<Image>();
            img.color = color;
            return img;
        }

        static TextMeshProUGUI Text(GeneratedTree tree, Transform parent, string name, Vector2 pos, Vector2 size, float fontSize, FontStyles style)
        {
            var go = tree.Child(parent, name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            var rt = (RectTransform)go.transform;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var t = go.GetComponent<TextMeshProUGUI>();
            t.fontSize = fontSize;
            t.fontStyle = style;
            t.alignment = TextAlignmentOptions.Center;
            t.color = Color.white;
            t.raycastTarget = false;
            t.text = "";
            return t;
        }

        static void Label(GeneratedTree tree, Transform parent, string text, Vector2 pos)
        {
            var t = Text(tree, parent, "Label " + text, pos, new Vector2(150f, 60f), 28f, FontStyles.Normal);
            t.alignment = TextAlignmentOptions.Left;
            t.text = text;
        }

        static Button MakeButton(GeneratedTree tree, Transform parent, string name, string label, Vector2 pos, Vector2 size, Color? color = null)
        {
            var img = Image(tree, parent, name, pos, size, color ?? ButtonColor);
            var button = GetOrAdd<Button>(img.gameObject);
            button.targetGraphic = img;
            var colors = button.colors;
            colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f);
            colors.pressedColor = new Color(0.75f, 0.75f, 0.75f);
            button.colors = colors;
            var t = Text(tree, img.transform, "Label", Vector2.zero, size - new Vector2(16f, 8f), 30f, FontStyles.Bold);
            t.enableAutoSizing = true;
            t.fontSizeMin = 16f;
            t.fontSizeMax = 30f;
            t.text = label;
            return button;
        }

        /// <summary>Session text on the far scoreboard, beside the M4 debug readout (which SessionUI hides).</summary>
        static TMP_Text EnsureBoardText(Transform uiRoot, List<string> created)
        {
            var board = uiRoot.Find("LiveScoreboard");
            if (board == null)
                return null;
            var t = board.Find("SessionBoard");
            if (t == null)
            {
                var go = new GameObject("SessionBoard");
                go.transform.SetParent(board, false);
                go.AddComponent<TextMeshPro>();
                created.Add("SessionBoard");
                t = go.transform;
            }
            // Same placement rules as the debug readout: undo the board's scale; its -Z face points at the player.
            var s = board.localScale;
            t.localScale = new Vector3(1f / s.x, 1f / s.y, 1f / s.z);
            t.localPosition = new Vector3(0f, 0f, -0.6f);
            t.localRotation = Quaternion.identity;
            var text = t.GetComponent<TextMeshPro>();
            text.rectTransform.sizeDelta = new Vector2(s.x * 0.92f, s.y * 0.88f);
            text.enableAutoSizing = true;
            text.fontSizeMin = 0.05f;
            text.fontSizeMax = 0.5f;
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;
            text.text = "";
            EditorUtility.SetDirty(text);
            return text;
        }

        // ---------- Helpers ----------

        static T GetOrAdd<T>(GameObject go) where T : Component
        {
            if (!go.TryGetComponent<T>(out var c))
                c = go.AddComponent<T>();
            return c;
        }

        static T Find<T>(GameObject main, string path) where T : Component
        {
            var t = main.transform.Find(path);
            return t != null && t.TryGetComponent<T>(out var c) ? c : null;
        }
    }
}
