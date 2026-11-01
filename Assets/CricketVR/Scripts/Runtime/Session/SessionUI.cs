using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CricketVR
{
    /// <summary>
    /// Display only (CLAUDE.md §6: a UI refresh never changes the session). Two surfaces:
    ///   the near panel, about 0.75 m in front of the stance, for anything with buttons (menu, pause, end screen,
    ///   bat setup) so the ray and the bat can both reach it;
    ///   the far scoreboard, off the delivery sight line, for the live session (countdown, ball, last result).
    /// Buttons report their GameObject name through <see cref="ButtonPressed"/>; SessionManager decides what happens.
    /// </summary>
    public class SessionUI : MonoBehaviour
    {
        [Header("Near panel")]
        [SerializeField] GameObject nearPanel;
        [SerializeField] TMP_Text title;
        [SerializeField] TMP_Text body;
        [SerializeField] TMP_Text hint;
        [SerializeField] GameObject menuGroup;
        [SerializeField] GameObject netOptions;
        [SerializeField] GameObject matchOptions;
        [SerializeField] GameObject endGroup;
        [SerializeField] GameObject pauseGroup;
        [SerializeField] Button[] buttons;

        [Header("Far scoreboard")]
        [SerializeField] TMP_Text board;
        [Tooltip("The old debug readout on the scoreboard; hidden while the session UI owns the board.")]
        [SerializeField] GameObject debugReadout;

        static readonly Color Normal = new Color(0.16f, 0.22f, 0.36f);
        static readonly Color Selected = new Color(0.95f, 0.62f, 0.12f);

        public event Action<string> ButtonPressed;

        public Button[] Buttons => buttons;
        public bool NearPanelVisible => nearPanel != null && nearPanel.activeSelf;

        void Awake()
        {
            if (buttons == null)
                return;
            foreach (var b in buttons)
            {
                if (b == null) continue;
                string name = b.name;
                b.onClick.AddListener(() => ButtonPressed?.Invoke(name));
            }
            if (debugReadout != null)
                debugReadout.SetActive(false);
        }

        /// <summary>Puts the near panel in front of the player's stance, facing them, tilted back slightly.</summary>
        public void PlaceNear(Transform xrOrigin)
        {
            if (nearPanel == null || xrOrigin == null)
                return;
            var p = xrOrigin.position + new Vector3(0f, 1.2f, 0.75f);
            nearPanel.transform.SetPositionAndRotation(p, Quaternion.Euler(20f, 0f, 0f));
        }

        public void Select(string buttonName, bool selected)
        {
            if (buttons == null)
                return;
            foreach (var b in buttons)
                if (b != null && b.name == buttonName && b.targetGraphic != null)
                    b.targetGraphic.color = selected ? Selected : Normal;
        }

        public void ShowMenu(bool netPractice, string hintText)
        {
            Near(true, "CricketVR", "", hintText);
            Groups(menu: true);
            if (netOptions != null) netOptions.SetActive(netPractice);
            if (matchOptions != null) matchOptions.SetActive(!netPractice);
            Board("Choose a mode on the panel in front of you.");
        }

        public void ShowBatSetup(string instructions, string hintText = "Tap A when done", string titleText = "Bat setup")
        {
            Near(true, titleText, instructions, hintText);
            Groups();
            Board(titleText);
        }

        public void ShowPaused(string text)
        {
            Near(true, "Paused", text, "Point and pull the trigger, or tap a button with the bat");
            Groups(pause: true);
        }

        public void ShowEnd(string heading, string summary, bool newScenario)
        {
            Near(true, heading, summary, "");
            Groups(end: true);
            if (buttons != null)
                foreach (var b in buttons)
                if (b != null && b.name == "NewScenario")
                    b.gameObject.SetActive(newScenario);
        }

        /// <summary>Live session: the near panel is hidden so it can't block the ball; the board shows progress.</summary>
        public void ShowPlaying(string boardText)
        {
            Near(false, "", "", "");
            Board(boardText);
        }

        public void Board(string text)
        {
            if (board != null)
                board.text = text;
        }

        void Near(bool visible, string titleText, string bodyText, string hintText)
        {
            if (nearPanel != null) nearPanel.SetActive(visible);
            if (title != null) title.text = titleText;
            if (body != null) body.text = bodyText;
            if (hint != null) hint.text = hintText;
        }

        void Groups(bool menu = false, bool end = false, bool pause = false)
        {
            if (menuGroup != null) menuGroup.SetActive(menu);
            if (endGroup != null) endGroup.SetActive(end);
            if (pauseGroup != null) pauseGroup.SetActive(pause);
        }

        public void Configure(GameObject panel, TMP_Text titleText, TMP_Text bodyText, TMP_Text hintText, GameObject menu, GameObject net,
            GameObject match, GameObject end, GameObject pause, Button[] allButtons, TMP_Text boardText, GameObject readout)
        {
            nearPanel = panel;
            title = titleText;
            body = bodyText;
            hint = hintText;
            menuGroup = menu;
            netOptions = net;
            matchOptions = match;
            endGroup = end;
            pauseGroup = pause;
            buttons = allButtons;
            board = boardText;
            debugReadout = readout;
        }
    }
}
