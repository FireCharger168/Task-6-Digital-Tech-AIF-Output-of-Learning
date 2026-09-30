using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace HereToSlay.View
{
    /// <summary>All screen-space UI, built in code: side panel (prompt, card preview, log), dice, menus and world labels.</summary>
    public sealed class GameUI : MonoBehaviour
    {
        public const float SidePanelWidth = 390f;

        private Font font;
        private Canvas canvas;
        private RectTransform root;

        private Text statusText;
        private Text speedLabel;
        private Text promptText;
        private RectTransform optionsContent;
        private ScrollRect optionsScroll;
        private Image previewImage;
        private Text previewTitle;
        private Text previewBody;
        private Text logText;

        private RectTransform labelLayer;
        private readonly List<Text> labelPool = new List<Text>();

        private GameObject dicePanel;
        private Text die1Text;
        private Text die2Text;
        private Text diceDetail;
        private float diceHideTime;

        private Text toastText;
        private float toastHideTime;

        private GameObject menuOverlay;
        private GameObject passOverlay;
        private Text passText;
        private GameObject gameOverOverlay;
        private Text gameOverText;
        private GameObject rulesOverlay;

        private ChoiceRequest shownRequest;
        private readonly List<string> logLines = new List<string>();

        public Action<ChoiceRequest, int> OnOptionClicked;
        public Action OnSpeedClicked;
        public Action OnRestartClicked;

        public float PanelWidthPixels => canvas == null ? SidePanelWidth : SidePanelWidth * canvas.scaleFactor;

        // ------------------------------------------------------------------ construction

        public void Build()
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            if (FindAnyObjectByType<EventSystem>() == null)
            {
                GameObject es = new GameObject("EventSystem");
                es.AddComponent<EventSystem>();
                es.AddComponent<InputSystemUIInputModule>();
            }

            GameObject canvasGo = new GameObject("HereToSlay UI");
            canvasGo.transform.SetParent(transform, false);
            canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();
            root = canvasGo.GetComponent<RectTransform>();

            labelLayer = NewRect("World Labels", root);
            Stretch(labelLayer);

            BuildSidePanel();
            BuildDice();
            BuildToast();
            BuildMenu();
            BuildPassOverlay();
            BuildGameOver();
            BuildRules();
        }

        private RectTransform NewRect(string name, Transform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.GetComponent<RectTransform>();
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void Anchor(RectTransform rect, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
        {
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }

        private Image NewImage(string name, Transform parent, Color color)
        {
            RectTransform rect = NewRect(name, parent);
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            return image;
        }

        private Text NewText(string name, Transform parent, string text, int size, Color color, TextAnchor alignment = TextAnchor.UpperLeft)
        {
            RectTransform rect = NewRect(name, parent);
            Text label = rect.gameObject.AddComponent<Text>();
            label.font = font;
            label.text = text;
            label.fontSize = size;
            label.color = color;
            label.alignment = alignment;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            label.supportRichText = true;
            label.raycastTarget = false;
            return label;
        }

        private Button NewButton(string name, Transform parent, string text, int size, Color color, Action onClick)
        {
            Image background = NewImage(name, parent, color);
            Button button = background.gameObject.AddComponent<Button>();
            ColorBlock colors = button.colors;
            colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            button.colors = colors;
            Text label = NewText("Label", background.transform, text, size, Color.white, TextAnchor.MiddleCenter);
            Stretch(label.rectTransform);
            label.rectTransform.offsetMin = new Vector2(8, 2);
            label.rectTransform.offsetMax = new Vector2(-8, -2);
            button.onClick.AddListener(() => onClick?.Invoke());
            return button;
        }

        private void BuildSidePanel()
        {
            Image panel = NewImage("Side Panel", root, new Color(0.07f, 0.06f, 0.11f, 0.94f));
            panel.raycastTarget = true;
            RectTransform rect = panel.rectTransform;
            Anchor(rect, new Vector2(1, 0), new Vector2(1, 1), new Vector2(-SidePanelWidth, 0), Vector2.zero);

            Text title = NewText("Title", rect, "HERE TO SLAY", 26, new Color(1f, 0.82f, 0.35f), TextAnchor.MiddleLeft);
            Anchor(title.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(16, -44), new Vector2(-150, -8));

            Button speed = NewButton("Speed", rect, "Speed 1x", 15, new Color(0.25f, 0.22f, 0.35f), () => OnSpeedClicked?.Invoke());
            Anchor(speed.GetComponent<RectTransform>(), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-140, -42), new Vector2(-50, -12));
            speedLabel = speed.GetComponentInChildren<Text>();

            Button rules = NewButton("Rules", rect, "?", 18, new Color(0.25f, 0.22f, 0.35f), () => rulesOverlay.SetActive(true));
            Anchor(rules.GetComponent<RectTransform>(), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-44, -42), new Vector2(-12, -12));

            statusText = NewText("Status", rect, "", 16, new Color(0.85f, 0.95f, 1f));
            Anchor(statusText.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(16, -100), new Vector2(-16, -50));

            // Prompt + options
            Image promptBg = NewImage("Prompt", rect, new Color(0.16f, 0.13f, 0.24f, 1f));
            Anchor(promptBg.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(10, -470), new Vector2(-10, -104));
            promptText = NewText("Prompt Text", promptBg.transform, "", 17, new Color(1f, 0.93f, 0.7f));
            Anchor(promptText.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(10, -66), new Vector2(-10, -6));

            RectTransform scrollRect = NewRect("Options", promptBg.transform);
            Anchor(scrollRect, Vector2.zero, Vector2.one, new Vector2(6, 6), new Vector2(-6, -70));
            optionsScroll = scrollRect.gameObject.AddComponent<ScrollRect>();
            optionsScroll.horizontal = false;
            optionsScroll.scrollSensitivity = 25f;
            Image viewportImage = NewImage("Viewport", scrollRect, new Color(0, 0, 0, 0.2f));
            Stretch(viewportImage.rectTransform);
            viewportImage.gameObject.AddComponent<RectMask2D>();
            optionsContent = NewRect("Content", viewportImage.transform);
            optionsContent.anchorMin = new Vector2(0, 1);
            optionsContent.anchorMax = new Vector2(1, 1);
            optionsContent.pivot = new Vector2(0.5f, 1f);
            optionsContent.offsetMin = Vector2.zero;
            optionsContent.offsetMax = Vector2.zero;
            VerticalLayoutGroup layout = optionsContent.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 4;
            layout.padding = new RectOffset(4, 4, 4, 4);
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            ContentSizeFitter fitter = optionsContent.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            optionsScroll.viewport = viewportImage.rectTransform;
            optionsScroll.content = optionsContent;

            // Card preview
            Image previewBg = NewImage("Preview", rect, new Color(0.11f, 0.1f, 0.17f, 1f));
            Anchor(previewBg.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(10, -760), new Vector2(-10, -478));
            previewImage = NewImage("Card", previewBg.transform, Color.white);
            previewImage.preserveAspect = true;
            Anchor(previewImage.rectTransform, new Vector2(0, 0), new Vector2(0, 1), new Vector2(8, 8), new Vector2(178, -8));
            previewTitle = NewText("Name", previewBg.transform, "Hover a card", 18, new Color(1f, 0.85f, 0.45f));
            Anchor(previewTitle.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(186, -34), new Vector2(-8, -6));
            previewBody = NewText("Body", previewBg.transform, "Move the mouse over any card to read it here.", 14, new Color(0.9f, 0.9f, 0.95f));
            Anchor(previewBody.rectTransform, new Vector2(0, 0), new Vector2(1, 1), new Vector2(186, 8), new Vector2(-8, -38));
            previewImage.gameObject.SetActive(false);

            // Log
            Image logBg = NewImage("Log", rect, new Color(0.05f, 0.05f, 0.08f, 1f));
            Anchor(logBg.rectTransform, new Vector2(0, 0), new Vector2(1, 1), new Vector2(10, 10), new Vector2(-10, -768));
            logText = NewText("Log Text", logBg.transform, "", 13, new Color(0.8f, 0.82f, 0.9f), TextAnchor.LowerLeft);
            logText.verticalOverflow = VerticalWrapMode.Truncate;
            Anchor(logText.rectTransform, Vector2.zero, Vector2.one, new Vector2(8, 6), new Vector2(-8, -6));
        }

        private void BuildDice()
        {
            Image panel = NewImage("Dice", root, new Color(0.08f, 0.05f, 0.14f, 0.92f));
            dicePanel = panel.gameObject;
            RectTransform rect = panel.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(620, 150);
            rect.anchoredPosition = new Vector2(-SidePanelWidth / 2f, -40);

            die1Text = MakeDie(rect, new Vector2(-250, 18));
            die2Text = MakeDie(rect, new Vector2(-165, 18));
            diceDetail = NewText("Detail", rect, "", 18, Color.white, TextAnchor.MiddleLeft);
            Anchor(diceDetail.rectTransform, new Vector2(0, 0), new Vector2(1, 1), new Vector2(230, 8), new Vector2(-12, -8));
            dicePanel.SetActive(false);
        }

        private Text MakeDie(RectTransform parent, Vector2 position)
        {
            Image die = NewImage("Die", parent, new Color(0.97f, 0.95f, 0.9f));
            die.rectTransform.anchorMin = die.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            die.rectTransform.sizeDelta = new Vector2(72, 72);
            die.rectTransform.anchoredPosition = position;
            Text value = NewText("Value", die.transform, "6", 44, new Color(0.25f, 0.1f, 0.2f), TextAnchor.MiddleCenter);
            Stretch(value.rectTransform);
            value.fontStyle = FontStyle.Bold;
            return value;
        }

        private void BuildToast()
        {
            toastText = NewText("Toast", root, "", 20, new Color(1f, 0.95f, 0.7f), TextAnchor.MiddleCenter);
            RectTransform rect = toastText.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(900, 50);
            rect.anchoredPosition = new Vector2(-SidePanelWidth / 2f, 60);
            Outline outline = toastText.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0, 0, 0, 0.9f);
            toastText.gameObject.SetActive(false);
        }

        private GameObject Overlay(string name, float alpha)
        {
            Image overlay = NewImage(name, root, new Color(0.03f, 0.02f, 0.07f, alpha));
            Stretch(overlay.rectTransform);
            overlay.raycastTarget = true;
            return overlay.gameObject;
        }

        private Image CenterBox(Transform parent, Vector2 size)
        {
            Image box = NewImage("Box", parent, new Color(0.13f, 0.1f, 0.2f, 0.98f));
            box.rectTransform.anchorMin = box.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            box.rectTransform.sizeDelta = size;
            return box;
        }

        private readonly string[] seatModes = { "Human", "AI", "Empty" };
        private readonly int[] seatChoice = { 0, 1, 1, 2 };
        private readonly Text[] seatLabels = new Text[4];
        private Action<List<SeatConfig>> onStart;
        private Text menuError;

        private void BuildMenu()
        {
            menuOverlay = Overlay("Menu", 0.55f);
            Image box = CenterBox(menuOverlay.transform, new Vector2(620, 600));
            Text title = NewText("Title", box.transform, "HERE TO SLAY", 54, new Color(1f, 0.82f, 0.35f), TextAnchor.MiddleCenter);
            Anchor(title.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -90), new Vector2(0, -20));
            Text subtitle = NewText("Subtitle", box.transform, "Build a party of heroes, slay monsters, and backstab your friends.\nWin with 3 slain Monsters or a Party of all 6 classes.", 16, new Color(0.85f, 0.85f, 0.95f), TextAnchor.MiddleCenter);
            Anchor(subtitle.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(20, -150), new Vector2(-20, -92));

            for (int i = 0; i < 4; i++)
            {
                int seat = i;
                Text label = NewText("Seat", box.transform, $"Seat {i + 1}", 22, Color.white, TextAnchor.MiddleLeft);
                Anchor(label.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(90, -205 - i * 62), new Vector2(260, -160 - i * 62));
                Button toggle = NewButton("Mode", box.transform, seatModes[seatChoice[i]], 20, new Color(0.3f, 0.25f, 0.45f), () =>
                {
                    seatChoice[seat] = (seatChoice[seat] + 1) % seatModes.Length;
                    seatLabels[seat].text = seatModes[seatChoice[seat]];
                });
                Anchor(toggle.GetComponent<RectTransform>(), new Vector2(0, 1), new Vector2(0, 1), new Vector2(290, -205 - i * 62), new Vector2(530, -162 - i * 62));
                seatLabels[i] = toggle.GetComponentInChildren<Text>();
            }

            menuError = NewText("Error", box.transform, "", 16, new Color(1f, 0.5f, 0.5f), TextAnchor.MiddleCenter);
            Anchor(menuError.rectTransform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(20, 100), new Vector2(-20, 130));

            Button start = NewButton("Start", box.transform, "START ADVENTURE", 24, new Color(0.75f, 0.35f, 0.15f), StartPressed);
            Anchor(start.GetComponent<RectTransform>(), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(-170, 30), new Vector2(170, 90));
            menuOverlay.SetActive(false);
        }

        private void StartPressed()
        {
            List<SeatConfig> seats = new List<SeatConfig>();
            int humans = 0;
            int bots = 0;
            for (int i = 0; i < 4; i++)
            {
                if (seatChoice[i] == 0)
                {
                    humans++;
                    seats.Add(new SeatConfig($"Player {humans}", true));
                }
                else if (seatChoice[i] == 1)
                {
                    bots++;
                    seats.Add(new SeatConfig($"Bot {bots}", false));
                }
            }

            if (seats.Count < 2)
            {
                menuError.text = "You need at least 2 seats filled.";
                return;
            }

            menuOverlay.SetActive(false);
            onStart?.Invoke(seats);
        }

        private Action passContinue;

        private void BuildPassOverlay()
        {
            passOverlay = Overlay("Pass Device", 0.97f);
            passText = NewText("Text", passOverlay.transform, "", 34, Color.white, TextAnchor.MiddleCenter);
            Anchor(passText.rectTransform, new Vector2(0, 0.5f), new Vector2(1, 0.5f), new Vector2(0, 0), new Vector2(0, 160));
            Button ok = NewButton("Ready", passOverlay.transform, "I'm ready - show my cards", 22, new Color(0.3f, 0.45f, 0.3f), () =>
            {
                passOverlay.SetActive(false);
                passContinue?.Invoke();
            });
            Anchor(ok.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-190, -90), new Vector2(190, -30));
            passOverlay.SetActive(false);
        }

        private void BuildGameOver()
        {
            gameOverOverlay = Overlay("Game Over", 0.6f);
            Image box = CenterBox(gameOverOverlay.transform, new Vector2(640, 300));
            gameOverText = NewText("Text", box.transform, "", 36, new Color(1f, 0.85f, 0.4f), TextAnchor.MiddleCenter);
            Anchor(gameOverText.rectTransform, new Vector2(0, 0.35f), new Vector2(1, 1), new Vector2(20, 0), new Vector2(-20, -20));
            Button again = NewButton("Again", box.transform, "Play again", 22, new Color(0.75f, 0.35f, 0.15f), () => OnRestartClicked?.Invoke());
            Anchor(again.GetComponent<RectTransform>(), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(-130, 30), new Vector2(130, 85));
            gameOverOverlay.SetActive(false);
        }

        private void BuildRules()
        {
            rulesOverlay = Overlay("Rules", 0.8f);
            Image box = CenterBox(rulesOverlay.transform, new Vector2(900, 720));
            Text text = NewText("Text", box.transform,
                "<b><size=30>How to play</size></b>\n\n" +
                "<b>Goal:</b> slay 3 Monsters, OR have a Party with all 6 classes (your Party Leader counts).\n\n" +
                "<b>Your turn:</b> draw 1 card for free, then spend 3 action points (AP):\n" +
                "  • Draw a card - 1 AP\n" +
                "  • Play a Hero, Item or Magic card - 1 AP (a new Hero may roll for its effect right away)\n" +
                "  • Roll 2d6 to use a Hero's effect in your Party - 1 AP (once per Hero per turn)\n" +
                "  • Attack a Monster whose requirement your Party meets - 2 AP\n" +
                "  • Discard your hand and draw 5 - 3 AP\n\n" +
                "<b>Challenge cards:</b> when someone plays a Hero, Item or Magic card, you may challenge. Both roll; if the challenger rolls equal or higher, the card is discarded.\n\n" +
                "<b>Modifier cards:</b> play them on ANY roll, yours or anyone else's, at no cost.\n\n" +
                "<b>Masks</b> change a Hero's class. <b>Cursed Items</b> go on other players' Heroes.\n" +
                "Hand limit is 7 at the end of your turn.\n\n" +
                "<b>Controls:</b> click glowing cards on the table, or pick from the list on the right. Hover any card to read it.",
                17, Color.white);
            Anchor(text.rectTransform, Vector2.zero, Vector2.one, new Vector2(30, 80), new Vector2(-30, -24));
            Button close = NewButton("Close", box.transform, "Close", 20, new Color(0.3f, 0.25f, 0.45f), () => rulesOverlay.SetActive(false));
            Anchor(close.GetComponent<RectTransform>(), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(-90, 20), new Vector2(90, 66));
            rulesOverlay.SetActive(false);
        }

        // ------------------------------------------------------------------ public API

        public bool MenuOpen => menuOverlay.activeSelf;
        public bool PassScreenOpen => passOverlay.activeSelf;

        public void ShowMenu(Action<List<SeatConfig>> startCallback)
        {
            onStart = startCallback;
            menuError.text = "";
            gameOverOverlay.SetActive(false);
            menuOverlay.SetActive(true);
            ClearPrompt();
        }

        public void ShowPassScreen(string playerName, Action onContinue)
        {
            passContinue = onContinue;
            passText.text = $"Pass the device to\n<b>{playerName}</b>";
            passOverlay.SetActive(true);
        }

        public void ShowGameOver(string text)
        {
            gameOverText.text = text;
            gameOverOverlay.SetActive(true);
        }

        public void SetSpeedLabel(string text)
        {
            speedLabel.text = text;
        }

        public void SetStatus(string text)
        {
            statusText.text = text;
        }

        public void AddLog(string line)
        {
            logLines.Add(line);
            if (logLines.Count > 60)
            {
                logLines.RemoveAt(0);
            }

            logText.text = string.Join("\n", logLines.Skip(Math.Max(0, logLines.Count - 22)));
        }

        public void ClearLog()
        {
            logLines.Clear();
            logText.text = "";
        }

        public void ShowToast(string text, float seconds = 2.5f)
        {
            toastText.text = text;
            toastText.gameObject.SetActive(true);
            toastHideTime = Time.time + seconds;
        }

        public void ShowRoll(RollContext roll)
        {
            dicePanel.SetActive(true);
            die1Text.text = roll.die1.ToString();
            die2Text.text = roll.die2.ToString();
            string goal;
            switch (roll.kind)
            {
                case RollKind.HeroEffect:
                    goal = $"need {roll.target}+";
                    break;
                case RollKind.AttackMonster:
                    goal = $"slay {roll.slayOn}+ / fail {roll.failOn}-";
                    break;
                default:
                    goal = roll.opposing != null && roll.opposing.die1 > 0 ? $"vs {roll.opposing.Total}" : "challenge roll";
                    break;
            }

            string extras = "";
            if (roll.passiveBonus != 0)
            {
                extras += $"  bonus {(roll.passiveBonus > 0 ? "+" : "")}{roll.passiveBonus}";
            }

            if (roll.modifierTotal != 0)
            {
                extras += $"  modifiers {(roll.modifierTotal > 0 ? "+" : "")}{roll.modifierTotal}";
            }

            diceDetail.text = $"<size=15>{roll.description}</size>\n<b><size=30>Total {roll.Total}</size></b>  <size=16>({goal})</size>\n<size=14>{extras}</size>";
            diceHideTime = Time.time + 3.2f;
        }

        public void ShowPreview(CardDefinition definition)
        {
            if (definition == null)
            {
                return;
            }

            previewImage.gameObject.SetActive(true);
            previewImage.sprite = Art.CardFace(definition);
            previewTitle.text = definition.displayName;
            previewBody.text = HereToSlayCardDatabase.FullDescription(definition);
        }

        public void ClearPrompt()
        {
            shownRequest = null;
            promptText.text = "";
            for (int i = optionsContent.childCount - 1; i >= 0; i--)
            {
                Destroy(optionsContent.GetChild(i).gameObject);
            }
        }

        public void ShowWaiting(string text)
        {
            if (shownRequest != null)
            {
                ClearPrompt();
            }

            promptText.text = text;
        }

        public void ShowRequest(ChoiceRequest request)
        {
            if (shownRequest == request)
            {
                return;
            }

            ClearPrompt();
            shownRequest = request;
            promptText.text = request.prompt + (request.pickOnBoard ? "\n<size=13><color=#bbbbbb>Click a glowing card, or choose below.</color></size>" : "");

            if (request.revealed.Count > 0)
            {
                RectTransform row = NewRect("Revealed", optionsContent);
                HorizontalLayoutGroup h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
                h.spacing = 4;
                h.childControlWidth = false;
                h.childControlHeight = false;
                h.childForceExpandWidth = false;
                LayoutElement rowLayout = row.gameObject.AddComponent<LayoutElement>();
                rowLayout.preferredHeight = 100;
                foreach (CardInstance card in request.revealed.Take(8))
                {
                    Image thumb = NewImage("Card", row, Color.white);
                    thumb.sprite = Art.CardFace(card.def);
                    thumb.preserveAspect = true;
                    thumb.rectTransform.sizeDelta = new Vector2(68, 96);
                    thumb.raycastTarget = true;
                    CardDefinition def = card.def;
                    AddHover(thumb.gameObject, () => ShowPreview(def));
                }
            }

            for (int i = 0; i < request.options.Count; i++)
            {
                int index = i;
                ChoiceOption option = request.options[i];
                Color color = OptionColor(request, option);
                Button button = NewButton("Option", optionsContent, option.label, 15, color, () => OnOptionClicked?.Invoke(request, index));
                Text label = button.GetComponentInChildren<Text>();
                label.alignment = TextAnchor.MiddleLeft;
                LayoutElement element = button.gameObject.AddComponent<LayoutElement>();
                element.preferredHeight = option.card != null ? 46 : 38;

                if (option.card != null)
                {
                    Image thumb = NewImage("Thumb", button.transform, Color.white);
                    thumb.sprite = Art.CardFace(option.card.def);
                    thumb.preserveAspect = true;
                    thumb.raycastTarget = false;
                    Anchor(thumb.rectTransform, new Vector2(0, 0), new Vector2(0, 1), new Vector2(4, 3), new Vector2(34, -3));
                    label.rectTransform.offsetMin = new Vector2(42, 2);
                    CardDefinition def = option.card.def;
                    AddHover(button.gameObject, () => ShowPreview(def));
                }
            }

            optionsScroll.verticalNormalizedPosition = 1f;
        }

        private static Color OptionColor(ChoiceRequest request, ChoiceOption option)
        {
            switch (option.action)
            {
                case "end":
                    return new Color(0.45f, 0.2f, 0.2f);
                case "attack":
                    return new Color(0.55f, 0.18f, 0.25f);
                case "hero":
                    return new Color(0.2f, 0.35f, 0.5f);
                case "play":
                    return new Color(0.22f, 0.42f, 0.3f);
            }

            if (option.label == "Pass" || option.label.StartsWith("Let it") || option.label.StartsWith("No") || option.label.StartsWith("Keep") || option.label.StartsWith("Don't") || option.label.StartsWith("Stop"))
            {
                return new Color(0.3f, 0.28f, 0.36f);
            }

            return new Color(0.33f, 0.27f, 0.5f);
        }

        private void AddHover(GameObject target, Action onEnter)
        {
            HoverRelay relay = target.GetComponent<HoverRelay>();
            if (relay == null)
            {
                relay = target.AddComponent<HoverRelay>();
            }

            relay.onEnter = onEnter;
        }

        public void RenderLabels(IList<WorldLabel> labels, Camera cam)
        {
            for (int i = 0; i < labels.Count; i++)
            {
                if (i >= labelPool.Count)
                {
                    Text text = NewText("Label", labelLayer, "", 12, Color.white, TextAnchor.MiddleCenter);
                    text.horizontalOverflow = HorizontalWrapMode.Overflow;
                    text.verticalOverflow = VerticalWrapMode.Overflow;
                    text.rectTransform.sizeDelta = new Vector2(10, 10);
                    Shadow shadow = text.gameObject.AddComponent<Shadow>();
                    shadow.effectColor = new Color(0, 0, 0, 0.85f);
                    shadow.effectDistance = new Vector2(1.5f, -1.5f);
                    text.supportRichText = false;
                    labelPool.Add(text);
                }

                Text label = labelPool[i];
                label.gameObject.SetActive(true);
                label.text = labels[i].text;
                label.fontSize = labels[i].fontSize + 2;
                label.color = labels[i].color;
                label.rectTransform.position = cam.WorldToScreenPoint(labels[i].position);
            }

            for (int i = labels.Count; i < labelPool.Count; i++)
            {
                labelPool[i].gameObject.SetActive(false);
            }
        }

        private void Update()
        {
            if (dicePanel != null && dicePanel.activeSelf && Time.time > diceHideTime)
            {
                dicePanel.SetActive(false);
            }

            if (toastText != null && toastText.gameObject.activeSelf && Time.time > toastHideTime)
            {
                toastText.gameObject.SetActive(false);
            }
        }
    }

    /// <summary>Only listens for pointer-enter, so scroll and drag events still reach the ScrollRect.</summary>
    public sealed class HoverRelay : MonoBehaviour, IPointerEnterHandler
    {
        public Action onEnter;

        public void OnPointerEnter(PointerEventData eventData)
        {
            onEnter?.Invoke();
        }
    }
}
