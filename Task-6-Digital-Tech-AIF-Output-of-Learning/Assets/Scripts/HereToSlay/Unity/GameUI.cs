using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace HereToSlay.View
{
    public sealed class GameConfig
    {
        public readonly List<SeatConfig> seats = new List<SeatConfig>();
        public bool randomLeaders = true;
        public bool randomFirstPlayer = true;
    }

    /// <summary>
    /// All screen-space UI, built in code: title screen, game creation, settings, pause menu, rules,
    /// the in-game HUD (prompt banner, energy, End Turn, seat plates, dice, log, tooltips) and the
    /// hot-seat "pass the device" confirmation screen.
    /// </summary>
    public sealed class GameUI : MonoBehaviour
    {
        private static readonly Color Gold = new Color(1f, 0.82f, 0.35f);
        private static readonly Color PanelColor = new Color(0.1f, 0.08f, 0.16f, 0.96f);
        private static readonly Color ButtonColor = new Color(0.42f, 0.33f, 0.62f);
        private static readonly Color AccentButton = new Color(0.85f, 0.42f, 0.18f);
        private static readonly Color QuietButton = new Color(0.3f, 0.28f, 0.38f);

        private Font font;
        private Canvas canvas;
        private RectTransform root;

        // screens
        private GameObject titleScreen;
        private GameObject createScreen;
        private GameObject settingsScreen;
        private GameObject rulesScreen;
        private GameObject pauseScreen;
        private GameObject passScreen;
        private GameObject gameOverScreen;
        private GameObject hud;

        // HUD parts
        private Text turnText;
        private Text feedText;
        private GameObject logPanel;
        private Text logText;
        private GameObject banner;
        private Text bannerText;
        private LayoutElement bannerTextLayout;
        private RectTransform bannerButtons;
        private GameObject modal;
        private Text modalTitle;
        private RectTransform modalGrid;
        private RectTransform modalButtons;
        private ScrollRect modalScroll;
        private Button endTurnButton;
        private Image endTurnImage;
        private Text energyText;
        private Text myPlateText;
        private GameObject dicePanel;
        private Text die1Text;
        private Text die2Text;
        private Text diceDetail;
        private float diceHideTime;
        private Text toastText;
        private GameObject countdown;
        private Image countdownFill;
        private Text countdownText;
        private float toastHideTime;
        private GameObject tooltip;
        private Text tooltipTitle;
        private Text tooltipBody;
        private RectTransform badgeLayer;
        private readonly List<Text> badgePool = new List<Text>();
        private RectTransform seatLayer;
        private readonly List<GameObject> seatPool = new List<GameObject>();

        private Text passText;
        private Action passContinue;
        private Text gameOverText;
        private Action settingsBack;

        private ChoiceRequest shownRequest;
        private readonly List<string> logLines = new List<string>();
        private readonly List<string> feedLines = new List<string>();
        private readonly List<float> feedTimes = new List<float>();

        /// <summary>Frame number of the last button click delivered by the EventSystem (used to avoid double clicks).</summary>
        public static int LastClickFrame = -1;
        private static Button lastClickedButton;

        /// <summary>Runs a click once, even if both the EventSystem and the focus fallback deliver it.</summary>
        private static void GuardedClick(Button button, Action onClick)
        {
            if (button == lastClickedButton && Time.frameCount - LastClickFrame <= 2)
            {
                return;
            }

            lastClickedButton = button;
            LastClickFrame = Time.frameCount;
            onClick?.Invoke();
        }

        // callbacks
        public Action<GameConfig> OnStartGame;
        public Action OnQuitGame;
        public Action OnQuitToTitle;
        public Action OnPlayAgain;
        public Action OnPauseChanged;
        public Action<ChoiceRequest, int> OnOptionClicked;
        public Action OnEndTurnClicked;
        public Action OnSettingsChanged;

        public bool IsPaused => pauseScreen.activeSelf || settingsScreen.activeSelf && hud.activeSelf || rulesScreen.activeSelf && hud.activeSelf;
        public bool PassScreenOpen => passScreen.activeSelf;
        public bool ModalOpen => modal.activeSelf;
        public bool InGame => hud.activeSelf;

        // ------------------------------------------------------------------ helpers

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

            BuildHud();
            BuildTitle();
            BuildCreate();
            BuildSettings();
            BuildRules();
            BuildPause();
            BuildPass();
            BuildGameOver();
        }

        private RectTransform NewRect(string name, Transform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.GetComponent<RectTransform>();
        }

        private static void Stretch(RectTransform rect, float inset = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
        }

        private static void Place(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size, Vector2? pivot = null)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot ?? new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private Image NewImage(string name, Transform parent, Color color, Sprite sprite = null, bool sliced = false)
        {
            RectTransform rect = NewRect(name, parent);
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            if (sprite != null)
            {
                image.sprite = sprite;
                image.type = sliced ? Image.Type.Sliced : Image.Type.Simple;
            }

            return image;
        }

        private Image NewPanel(string name, Transform parent, Color color)
        {
            return NewImage(name, parent, color, Art.Panel, true);
        }

        private Text NewText(string name, Transform parent, string text, int size, Color color, TextAnchor alignment = TextAnchor.MiddleCenter, bool outline = false)
        {
            RectTransform rect = NewRect(name, parent);
            Text label = rect.gameObject.AddComponent<Text>();
            label.font = font;
            label.text = text;
            label.fontSize = size;
            label.color = color;
            label.alignment = alignment;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.supportRichText = true;
            label.raycastTarget = false;
            if (outline)
            {
                Outline o = label.gameObject.AddComponent<Outline>();
                o.effectColor = new Color(0, 0, 0, 0.85f);
                o.effectDistance = new Vector2(2, -2);
            }

            return label;
        }

        private Button NewButton(string name, Transform parent, string text, int size, Color color, Action onClick)
        {
            Image background = NewImage(name, parent, color, Art.Button, true);
            Button button = background.gameObject.AddComponent<Button>();
            ColorBlock colors = button.colors;
            colors.highlightedColor = new Color(1.2f, 1.2f, 1.2f, 1f);
            colors.pressedColor = new Color(0.75f, 0.75f, 0.75f, 1f);
            colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.6f);
            button.colors = colors;
            Text label = NewText("Label", background.transform, text, size, Color.white, TextAnchor.MiddleCenter, true);
            Stretch(label.rectTransform);
            label.rectTransform.offsetMin = new Vector2(10, 2);
            label.rectTransform.offsetMax = new Vector2(-10, -2);
            button.onClick.AddListener(() => GuardedClick(button, onClick));
            return button;
        }

        private GameObject MakeScreen(string name, float alpha)
        {
            Image overlay = NewImage(name, root, new Color(0.02f, 0.01f, 0.05f, alpha));
            Stretch(overlay.rectTransform);
            overlay.raycastTarget = true;
            overlay.gameObject.SetActive(false);
            return overlay.gameObject;
        }

        private RectTransform Box(Transform parent, Vector2 size, string title)
        {
            Image box = NewPanel("Box", parent, PanelColor);
            Place(box.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, size);
            Outline outline = box.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(Gold.r, Gold.g, Gold.b, 0.5f);
            outline.effectDistance = new Vector2(2, -2);
            if (title != null)
            {
                Text heading = NewText("Heading", box.transform, title, 40, Gold, TextAnchor.MiddleCenter, true);
                Place(heading.rectTransform, new Vector2(0.5f, 1f), new Vector2(0, -45), new Vector2(size.x - 40, 60));
            }

            return box.rectTransform;
        }

        private void AddHover(GameObject target, Action onEnter, Action onExit = null)
        {
            HoverRelay relay = target.GetComponent<HoverRelay>();
            if (relay == null)
            {
                relay = target.AddComponent<HoverRelay>();
            }

            relay.onEnter = onEnter;
            relay.onExit = onExit;
        }

        // ------------------------------------------------------------------ title

        private void BuildTitle()
        {
            titleScreen = MakeScreen("Title Screen", 0.55f);

            Text title = NewText("Title", titleScreen.transform, "HERE TO SLAY", 120, Gold, TextAnchor.MiddleCenter, true);
            title.fontStyle = FontStyle.Bold;
            Place(title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 250), new Vector2(1400, 160));
            Shadow glow = title.gameObject.AddComponent<Shadow>();
            glow.effectColor = new Color(0.6f, 0.2f, 0.05f, 0.9f);
            glow.effectDistance = new Vector2(5, -6);

            Text subtitle = NewText("Subtitle", titleScreen.transform, "Build a party of heroes. Slay monsters. Backstab your friends.", 28, new Color(0.95f, 0.9f, 1f), TextAnchor.MiddleCenter, true);
            Place(subtitle.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 150), new Vector2(1400, 50));

            string[] labels = { "New Game", "Settings", "How to Play", "Quit" };
            Action[] actions =
            {
                () => { titleScreen.SetActive(false); createScreen.SetActive(true); },
                () => OpenSettings(() => titleScreen.SetActive(true)),
                () => rulesScreen.SetActive(true),
                () => OnQuitGame?.Invoke()
            };
            for (int i = 0; i < labels.Length; i++)
            {
                Button button = NewButton(labels[i], titleScreen.transform, labels[i], 30, i == 0 ? AccentButton : ButtonColor, actions[i]);
                Place(button.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0, 30 - i * 95), new Vector2(380, 78));
            }

            Text credit = NewText("Credit", titleScreen.transform, "A fan-made digital version of Here to Slay by Unstable Games · Made in Unity with C#", 16, new Color(1, 1, 1, 0.6f));
            Place(credit.rectTransform, new Vector2(0.5f, 0f), new Vector2(0, 30), new Vector2(1400, 30));
        }

        public void ShowTitle()
        {
            HideAllScreens();
            hud.SetActive(false);
            titleScreen.SetActive(true);
        }

        private void HideAllScreens()
        {
            titleScreen.SetActive(false);
            createScreen.SetActive(false);
            settingsScreen.SetActive(false);
            rulesScreen.SetActive(false);
            pauseScreen.SetActive(false);
            passScreen.SetActive(false);
            gameOverScreen.SetActive(false);
        }

        // ------------------------------------------------------------------ create game

        private const int MaxSeats = 6;
        private static readonly string[] SeatModes = { "Human", "AI", "Closed" };
        private readonly int[] seatMode = { 0, 1, 1, 2, 2, 2 };
        private readonly Text[] seatModeLabels = new Text[MaxSeats];
        private readonly InputField[] seatNames = new InputField[MaxSeats];
        private readonly Image[] seatRows = new Image[MaxSeats];
        private bool createRandomLeaders = true;
        private bool createRandomFirst = true;
        private Text leaderModeLabel;
        private Text firstModeLabel;
        private Text createError;
        private GameConfig lastConfig;

        private void BuildCreate()
        {
            createScreen = MakeScreen("Create Game", 0.7f);
            RectTransform box = Box(createScreen.transform, new Vector2(1000, 860), "New Game");

            Text hint = NewText("Hint", box, "Set up to 6 seats. Each seat can be a Human (hot-seat on this screen), an AI, or Closed.", 18, new Color(0.85f, 0.85f, 0.95f));
            Place(hint.rectTransform, new Vector2(0.5f, 1f), new Vector2(0, -100), new Vector2(900, 30));

            for (int i = 0; i < MaxSeats; i++)
            {
                int seat = i;
                float y = -160 - i * 72;
                Image row = NewPanel("Seat Row", box, new Color(1, 1, 1, 0.06f));
                Place(row.rectTransform, new Vector2(0.5f, 1f), new Vector2(0, y), new Vector2(880, 62));
                seatRows[i] = row;

                Text label = NewText("Seat", row.transform, $"Seat {i + 1}", 24, Gold, TextAnchor.MiddleLeft);
                Place(label.rectTransform, new Vector2(0f, 0.5f), new Vector2(90, 0), new Vector2(150, 50));

                Button mode = NewButton("Mode", row.transform, SeatModes[seatMode[i]], 22, ButtonColor, () => CycleSeat(seat));
                Place(mode.GetComponent<RectTransform>(), new Vector2(0f, 0.5f), new Vector2(270, 0), new Vector2(170, 50));
                seatModeLabels[i] = mode.GetComponentInChildren<Text>();

                seatNames[i] = NewInput(row.transform, DefaultName(i, seatMode[i]));
                Place(seatNames[i].GetComponent<RectTransform>(), new Vector2(0f, 0.5f), new Vector2(620, 0), new Vector2(460, 50));
            }

            Text leaders = NewText("Leaders Label", box, "Party Leaders", 24, Color.white, TextAnchor.MiddleLeft);
            Place(leaders.rectTransform, new Vector2(0.5f, 0f), new Vector2(-250, 215), new Vector2(360, 50));
            Button leaderButton = NewButton("Leader Mode", box, "", 22, ButtonColor, () =>
            {
                createRandomLeaders = !createRandomLeaders;
                RefreshCreateLabels();
            });
            Place(leaderButton.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(170, 215), new Vector2(480, 54));
            leaderModeLabel = leaderButton.GetComponentInChildren<Text>();

            Text first = NewText("First Label", box, "First player", 24, Color.white, TextAnchor.MiddleLeft);
            Place(first.rectTransform, new Vector2(0.5f, 0f), new Vector2(-250, 150), new Vector2(360, 50));
            Button firstButton = NewButton("First Mode", box, "", 22, ButtonColor, () =>
            {
                createRandomFirst = !createRandomFirst;
                RefreshCreateLabels();
            });
            Place(firstButton.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(170, 150), new Vector2(480, 54));
            firstModeLabel = firstButton.GetComponentInChildren<Text>();

            createError = NewText("Error", box, "", 20, new Color(1f, 0.5f, 0.5f));
            Place(createError.rectTransform, new Vector2(0.5f, 0f), new Vector2(0, 100), new Vector2(900, 30));

            Button back = NewButton("Back", box, "Back", 26, QuietButton, () =>
            {
                createScreen.SetActive(false);
                titleScreen.SetActive(true);
            });
            Place(back.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(-200, 50), new Vector2(260, 64));
            Button start = NewButton("Start", box, "Start Adventure", 26, AccentButton, StartPressed);
            Place(start.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(170, 50), new Vector2(360, 64));

            RefreshCreateLabels();
        }

        private InputField NewInput(Transform parent, string value)
        {
            Image background = NewImage("Name", parent, new Color(0.05f, 0.04f, 0.09f, 1f), Art.Button, true);
            background.color = new Color(0.16f, 0.14f, 0.22f);
            InputField field = background.gameObject.AddComponent<InputField>();
            Text text = NewText("Text", background.transform, "", 22, Color.white, TextAnchor.MiddleLeft);
            text.supportRichText = false;
            Stretch(text.rectTransform);
            text.rectTransform.offsetMin = new Vector2(14, 4);
            text.rectTransform.offsetMax = new Vector2(-14, -4);
            Text placeholder = NewText("Placeholder", background.transform, "Enter a name...", 22, new Color(1, 1, 1, 0.35f), TextAnchor.MiddleLeft);
            placeholder.fontStyle = FontStyle.Italic;
            Stretch(placeholder.rectTransform);
            placeholder.rectTransform.offsetMin = new Vector2(14, 4);
            placeholder.rectTransform.offsetMax = new Vector2(-14, -4);
            field.textComponent = text;
            field.placeholder = placeholder;
            field.characterLimit = 16;
            field.text = value;
            return field;
        }

        private static string DefaultName(int seat, int mode)
        {
            return mode == 1 ? $"Bot {seat + 1}" : $"Player {seat + 1}";
        }

        private void CycleSeat(int seat)
        {
            string oldDefault = DefaultName(seat, seatMode[seat]);
            seatMode[seat] = (seatMode[seat] + 1) % SeatModes.Length;
            if (string.IsNullOrWhiteSpace(seatNames[seat].text) || seatNames[seat].text == oldDefault)
            {
                seatNames[seat].text = DefaultName(seat, seatMode[seat]);
            }

            RefreshCreateLabels();
        }

        private void RefreshCreateLabels()
        {
            for (int i = 0; i < MaxSeats; i++)
            {
                seatModeLabels[i].text = SeatModes[seatMode[i]];
                bool closed = seatMode[i] == 2;
                seatNames[i].interactable = !closed;
                seatNames[i].textComponent.color = closed ? new Color(1, 1, 1, 0.3f) : Color.white;
                seatRows[i].color = closed ? new Color(1, 1, 1, 0.03f) : seatMode[i] == 0 ? new Color(0.4f, 0.8f, 0.5f, 0.12f) : new Color(0.5f, 0.6f, 1f, 0.12f);
            }

            leaderModeLabel.text = createRandomLeaders ? "Randomly assigned" : "Players pick in turn (draft)";
            firstModeLabel.text = createRandomFirst ? "Random" : "Seat 1 goes first";
        }

        private void StartPressed()
        {
            GameConfig config = new GameConfig { randomLeaders = createRandomLeaders, randomFirstPlayer = createRandomFirst };
            HashSet<string> used = new HashSet<string>();
            for (int i = 0; i < MaxSeats; i++)
            {
                if (seatMode[i] == 2)
                {
                    continue;
                }

                string name = string.IsNullOrWhiteSpace(seatNames[i].text) ? DefaultName(i, seatMode[i]) : seatNames[i].text.Trim();
                if (!used.Add(name.ToLowerInvariant()))
                {
                    createError.text = $"Two seats are both called \"{name}\". Give every player a different name.";
                    return;
                }

                config.seats.Add(new SeatConfig(name, seatMode[i] == 0));
            }

            if (config.seats.Count < 2)
            {
                createError.text = "You need at least 2 players.";
                return;
            }

            createError.text = "";
            lastConfig = config;
            createScreen.SetActive(false);
            OnStartGame?.Invoke(config);
        }

        public GameConfig LastConfig => lastConfig;

        // ------------------------------------------------------------------ settings

        private Text speedValue;
        private Text aiValue;
        private Text turnValue;
        private Text choiceValue;
        private Text reactionValue;
        private Text fullscreenValue;
        private Text zoomValue;
        private Text feedValue;

        private static int Step(int index, int delta, int count)
        {
            return Mathf.Clamp(index + delta, 0, count - 1);
        }

        private void BuildSettings()
        {
            settingsScreen = MakeScreen("Settings", 0.75f);
            RectTransform box = Box(settingsScreen.transform, new Vector2(860, 900), "Settings");

            speedValue = SettingRow(box, 0, "Game speed",
                () => { GameSettings.SpeedIndex = Step(GameSettings.SpeedIndex, -1, GameSettings.Speeds.Length); SettingsChanged(); },
                () => { GameSettings.SpeedIndex = Step(GameSettings.SpeedIndex, 1, GameSettings.Speeds.Length); SettingsChanged(); });
            aiValue = SettingRow(box, 1, "AI thinking time",
                () => { GameSettings.AiSpeedIndex = Step(GameSettings.AiSpeedIndex, -1, GameSettings.AiSpeedNames.Length); SettingsChanged(); },
                () => { GameSettings.AiSpeedIndex = Step(GameSettings.AiSpeedIndex, 1, GameSettings.AiSpeedNames.Length); SettingsChanged(); });
            turnValue = SettingRow(box, 2, "Turn timer (per action)",
                () => { GameSettings.TurnTimerIndex = Step(GameSettings.TurnTimerIndex, -1, GameSettings.TurnTimes.Length); SettingsChanged(); },
                () => { GameSettings.TurnTimerIndex = Step(GameSettings.TurnTimerIndex, 1, GameSettings.TurnTimes.Length); SettingsChanged(); });
            choiceValue = SettingRow(box, 3, "Choice timer",
                () => { GameSettings.ChoiceTimerIndex = Step(GameSettings.ChoiceTimerIndex, -1, GameSettings.ChoiceTimes.Length); SettingsChanged(); },
                () => { GameSettings.ChoiceTimerIndex = Step(GameSettings.ChoiceTimerIndex, 1, GameSettings.ChoiceTimes.Length); SettingsChanged(); });
            reactionValue = SettingRow(box, 4, "Challenge / Modifier timer",
                () => { GameSettings.ReactionTimerIndex = Step(GameSettings.ReactionTimerIndex, -1, GameSettings.ReactionTimes.Length); SettingsChanged(); },
                () => { GameSettings.ReactionTimerIndex = Step(GameSettings.ReactionTimerIndex, 1, GameSettings.ReactionTimes.Length); SettingsChanged(); });
            fullscreenValue = SettingRow(box, 5, "Fullscreen", () => { GameSettings.Fullscreen = !GameSettings.Fullscreen; SettingsChanged(); }, null);
            zoomValue = SettingRow(box, 6, "Enlarge cards on hover", () => { GameSettings.HoverZoom = !GameSettings.HoverZoom; SettingsChanged(); }, null);
            feedValue = SettingRow(box, 7, "Show action feed", () => { GameSettings.EventFeed = !GameSettings.EventFeed; SettingsChanged(); }, null);

            Text note = NewText("Note", box, "When the turn timer runs out, your remaining energy is spent drawing cards and your turn ends.", 17, new Color(0.8f, 0.8f, 0.9f));
            Place(note.rectTransform, new Vector2(0.5f, 0f), new Vector2(0, 120), new Vector2(780, 30));

            Button reset = NewButton("Reset", box, "Reset to defaults", 22, QuietButton, () => { GameSettings.ResetToDefaults(); SettingsChanged(); });
            Place(reset.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(-170, 55), new Vector2(280, 60));
            Button back = NewButton("Back", box, "Back", 24, AccentButton, () =>
            {
                settingsScreen.SetActive(false);
                settingsBack?.Invoke();
            });
            Place(back.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(170, 55), new Vector2(280, 60));
            RefreshSettings();
        }

        private Text SettingRow(RectTransform box, int index, string label, Action left, Action right)
        {
            float y = -125 - index * 78;
            Text name = NewText("Label", box, label, 26, Color.white, TextAnchor.MiddleLeft);
            Place(name.rectTransform, new Vector2(0.5f, 1f), new Vector2(-170, y), new Vector2(440, 50));

            Text value;
            if (right == null)
            {
                Button toggle = NewButton("Toggle", box, "", 24, ButtonColor, left);
                Place(toggle.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(200, y), new Vector2(240, 56));
                value = toggle.GetComponentInChildren<Text>();
            }
            else
            {
                Button minus = NewButton("Less", box, "<", 28, QuietButton, left);
                Place(minus.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(95, y), new Vector2(56, 56));
                Button plus = NewButton("More", box, ">", 28, QuietButton, right);
                Place(plus.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(305, y), new Vector2(56, 56));
                value = NewText("Value", box, "", 26, Gold);
                Place(value.rectTransform, new Vector2(0.5f, 1f), new Vector2(200, y), new Vector2(140, 50));
            }

            return value;
        }

        private void SettingsChanged()
        {
            GameSettings.Save();
            GameSettings.Apply();
            RefreshSettings();
            OnSettingsChanged?.Invoke();
        }

        private void RefreshSettings()
        {
            speedValue.text = $"{GameSettings.GameSpeed}x";
            aiValue.text = GameSettings.AiSpeedNames[GameSettings.AiSpeedIndex];
            fullscreenValue.text = GameSettings.Fullscreen ? "On" : "Off";
            zoomValue.text = GameSettings.HoverZoom ? "On" : "Off";
            feedValue.text = GameSettings.EventFeed ? "On" : "Off";
            turnValue.text = GameSettings.SecondsLabel(GameSettings.TurnSeconds);
            choiceValue.text = GameSettings.SecondsLabel(GameSettings.ChoiceSeconds);
            reactionValue.text = GameSettings.SecondsLabel(GameSettings.ReactionSeconds);
        }

        private void OpenSettings(Action onBack)
        {
            titleScreen.SetActive(false);
            pauseScreen.SetActive(false);
            settingsBack = onBack;
            RefreshSettings();
            settingsScreen.SetActive(true);
        }

        // ------------------------------------------------------------------ rules

        private void BuildRules()
        {
            rulesScreen = MakeScreen("How to Play", 0.8f);
            RectTransform box = Box(rulesScreen.transform, new Vector2(1100, 860), "How to Play");
            Text text = NewText("Text", box,
                "<b>Goal:</b> be the first to SLAY 3 Monsters, or to have a Party with all 6 classes (your Party Leader counts).\n\n" +
                "<b>Your turn:</b> draw 1 card for free, then spend your 3 <color=#ff9955>energy</color> (action points):\n" +
                "   • Draw a card (click the deck) - 1\n" +
                "   • Play a Hero, Item or Magic card (click it, or drag it up onto the table) - 1\n" +
                "      A Hero you play may roll for its effect straight away, for free.\n" +
                "   • Roll 2 dice for the effect of a Hero in your Party (click it) - 1, once per Hero each turn\n" +
                "   • Attack a Monster whose requirement your Party meets (click it) - 2\n" +
                "   • Discard your whole hand and draw 5 - 3\n" +
                "Then press <b>End Turn</b>. You must discard down to 7 cards.\n" +
                "<b>Timers:</b> every decision has a timer (Settings). If your turn timer runs out, your remaining energy is spent drawing cards.\n\n" +
                "<b>Challenge:</b> when someone plays a Hero, Item or Magic card you may Challenge it - but be quick, the timer is short (Settings). Both roll; if the challenger rolls equal or higher, the card is discarded.\n" +
                "<b>Modifiers:</b> after ANY roll, anyone may play Modifier cards to change the total - free.\n" +
                "<b>Items</b> equip to Heroes: Masks change a Hero's class. <b>Cursed Items</b> are played on other players' Heroes.\n" +
                "<b>Monsters</b> show a requirement, a roll to slay and a roll that makes you suffer. Slain Monsters give a bonus.\n\n" +
                "<b>Hot-seat:</b> with several humans, a confirmation screen appears whenever control passes to a different player so nobody sees another player's cards.\n" +
                "Hover any card to enlarge it and read it. Press Esc for the menu.",
                21, Color.white, TextAnchor.UpperLeft);
            Place(text.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 20), new Vector2(1000, 660));
            Button close = NewButton("Close", box, "Close", 24, AccentButton, () => rulesScreen.SetActive(false));
            Place(close.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0, 50), new Vector2(240, 60));
        }

        // ------------------------------------------------------------------ pause / pass / game over

        private void BuildPause()
        {
            pauseScreen = MakeScreen("Pause", 0.6f);
            RectTransform box = Box(pauseScreen.transform, new Vector2(520, 560), "Paused");
            string[] labels = { "Resume", "Settings", "How to Play", "Quit to Title" };
            Action[] actions =
            {
                () => SetPaused(false),
                () => OpenSettings(() => pauseScreen.SetActive(true)),
                () => rulesScreen.SetActive(true),
                () => { SetPaused(false); OnQuitToTitle?.Invoke(); }
            };
            for (int i = 0; i < labels.Length; i++)
            {
                Button button = NewButton(labels[i], box, labels[i], 26, i == 0 ? AccentButton : ButtonColor, actions[i]);
                Place(button.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0, -140 - i * 90), new Vector2(340, 70));
            }
        }

        public void SetPaused(bool paused)
        {
            if (!hud.activeSelf)
            {
                return;
            }

            pauseScreen.SetActive(paused);
            if (!paused)
            {
                settingsScreen.SetActive(false);
                rulesScreen.SetActive(false);
            }

            OnPauseChanged?.Invoke();
        }

        public void TogglePause()
        {
            if (settingsScreen.activeSelf && hud.activeSelf)
            {
                settingsScreen.SetActive(false);
                pauseScreen.SetActive(true);
                return;
            }

            if (rulesScreen.activeSelf)
            {
                rulesScreen.SetActive(false);
                return;
            }

            SetPaused(!pauseScreen.activeSelf);
        }

        private void BuildPass()
        {
            passScreen = MakeScreen("Pass Device", 1f);
            passScreen.GetComponent<Image>().color = new Color(0.05f, 0.03f, 0.1f, 1f);
            passText = NewText("Text", passScreen.transform, "", 44, Color.white, TextAnchor.MiddleCenter, true);
            Place(passText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 120), new Vector2(1400, 300));
            Button ok = NewButton("Ready", passScreen.transform, "I'm ready - show my cards", 28, AccentButton, () =>
            {
                passScreen.SetActive(false);
                passContinue?.Invoke();
            });
            Place(ok.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0, -110), new Vector2(520, 80));
        }

        public void ShowPassScreen(string playerName, string reason, Action onContinue)
        {
            passContinue = onContinue;
            passText.text = $"<size=30>{reason}</size>\n\nPass the device to\n<color=#ffd166><b><size=64>{playerName}</size></b></color>\n\n<size=24>Everyone else, look away!</size>";
            HideTooltip();
            passScreen.SetActive(true);
        }

        private void BuildGameOver()
        {
            gameOverScreen = MakeScreen("Game Over", 0.65f);
            RectTransform box = Box(gameOverScreen.transform, new Vector2(760, 380), null);
            gameOverText = NewText("Text", box, "", 44, Gold, TextAnchor.MiddleCenter, true);
            Place(gameOverText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0, -120), new Vector2(700, 200));
            Button again = NewButton("Again", box, "Play Again", 26, AccentButton, () =>
            {
                gameOverScreen.SetActive(false);
                OnPlayAgain?.Invoke();
            });
            Place(again.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(-160, 60), new Vector2(280, 64));
            Button menu = NewButton("Menu", box, "Main Menu", 26, ButtonColor, () =>
            {
                gameOverScreen.SetActive(false);
                OnQuitToTitle?.Invoke();
            });
            Place(menu.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(160, 60), new Vector2(280, 64));
        }

        public void ShowGameOver(string text)
        {
            ClearRequest();
            gameOverText.text = text;
            gameOverScreen.SetActive(true);
        }

        // ------------------------------------------------------------------ HUD

        private void BuildHud()
        {
            hud = NewRect("HUD", root).gameObject;
            Stretch(hud.GetComponent<RectTransform>());

            badgeLayer = NewRect("Badges", hud.transform);
            Stretch(badgeLayer);
            seatLayer = NewRect("Seats", hud.transform);
            Stretch(seatLayer);

            // Top-left: menu + log buttons and the turn line
            Button menu = NewButton("Menu", hud.transform, "Menu", 20, QuietButton, () => SetPaused(true));
            Place(menu.GetComponent<RectTransform>(), new Vector2(0, 1), new Vector2(14, -10), new Vector2(100, 40), new Vector2(0, 1));
            Button log = NewButton("Log", hud.transform, "Log", 20, QuietButton, () => logPanel.SetActive(!logPanel.activeSelf));
            Place(log.GetComponent<RectTransform>(), new Vector2(0, 1), new Vector2(122, -10), new Vector2(80, 40), new Vector2(0, 1));
            turnText = NewText("Turn", hud.transform, "", 18, Color.white, TextAnchor.MiddleRight, true);
            Place(turnText.rectTransform, new Vector2(1, 1), new Vector2(-16, -10), new Vector2(420, 40), new Vector2(1, 1));

            // Action feed: the last few events, just above the prompt banner.
            feedText = NewText("Feed", hud.transform, "", 17, new Color(0.95f, 0.95f, 1f), TextAnchor.LowerCenter, true);
            Place(feedText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0, 452), new Vector2(1000, 70), new Vector2(0.5f, 0f));

            // Energy text over the world-space orb, own plate above it
            energyText = NewText("Energy", hud.transform, "", 48, Color.white, TextAnchor.MiddleCenter, true);
            energyText.fontStyle = FontStyle.Bold;
            Place(energyText.rectTransform, Vector2.zero, Vector2.zero, new Vector2(160, 70));
            myPlateText = NewText("My Plate", hud.transform, "", 18, Color.white, TextAnchor.LowerLeft, true);
            Place(myPlateText.rectTransform, new Vector2(0, 0), new Vector2(20, 205), new Vector2(340, 90), new Vector2(0, 0));

            // End Turn (Slay the Spire style, bottom right)
            endTurnButton = NewButton("End Turn", hud.transform, "End Turn", 30, new Color(0.25f, 0.5f, 0.7f), () => OnEndTurnClicked?.Invoke());
            endTurnImage = endTurnButton.GetComponent<Image>();
            Place(endTurnButton.GetComponent<RectTransform>(), new Vector2(1, 0), new Vector2(-40, 150), new Vector2(230, 84), new Vector2(1, 0));

            // Prompt banner: one compact row (prompt text + answer buttons) just above your Party.
            Image bannerImage = NewPanel("Prompt Banner", hud.transform, new Color(0.06f, 0.04f, 0.1f, 0.9f));
            banner = bannerImage.gameObject;
            RectTransform bannerRect = bannerImage.rectTransform;
            Place(bannerRect, new Vector2(0.5f, 0f), new Vector2(0, 388), new Vector2(900, 60), new Vector2(0.5f, 0f));
            HorizontalLayoutGroup bannerRow = banner.AddComponent<HorizontalLayoutGroup>();
            bannerRow.padding = new RectOffset(18, 14, 6, 6);
            bannerRow.spacing = 14;
            bannerRow.childAlignment = TextAnchor.MiddleCenter;
            bannerRow.childControlWidth = true;
            bannerRow.childControlHeight = true;
            bannerRow.childForceExpandWidth = false;
            bannerRow.childForceExpandHeight = false;
            ContentSizeFitter bannerFit = banner.AddComponent<ContentSizeFitter>();
            bannerFit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            bannerFit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            bannerText = NewText("Prompt", banner.transform, "", 20, new Color(1f, 0.93f, 0.7f), TextAnchor.MiddleLeft, true);
            bannerTextLayout = bannerText.gameObject.AddComponent<LayoutElement>();
            bannerButtons = NewRect("Buttons", banner.transform);
            HorizontalLayoutGroup row = bannerButtons.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 8;
            row.childAlignment = TextAnchor.MiddleCenter;
            row.childControlWidth = true;
            row.childControlHeight = true;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = false;
            banner.SetActive(false);

            // Dice
            Image dice = NewPanel("Dice", hud.transform, new Color(0.08f, 0.05f, 0.14f, 0.94f));
            dicePanel = dice.gameObject;
            Place(dice.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 45), new Vector2(660, 150));
            die1Text = MakeDie(dice.rectTransform, new Vector2(-265, 12));
            die2Text = MakeDie(dice.rectTransform, new Vector2(-180, 12));
            diceDetail = NewText("Detail", dice.transform, "", 18, Color.white, TextAnchor.MiddleLeft);
            diceDetail.rectTransform.anchorMin = Vector2.zero;
            diceDetail.rectTransform.anchorMax = Vector2.one;
            diceDetail.rectTransform.offsetMin = new Vector2(240, 8);
            diceDetail.rectTransform.offsetMax = new Vector2(-12, -8);
            dicePanel.SetActive(false);

            // Challenge countdown: a shrinking bar just under the prompt banner.
            Image countdownBack = NewPanel("Challenge Timer", hud.transform, new Color(0f, 0f, 0f, 0.75f));
            countdown = countdownBack.gameObject;
            Place(countdownBack.rectTransform, new Vector2(0.5f, 0f), new Vector2(0, 360), new Vector2(460, 26), new Vector2(0.5f, 0f));
            countdownFill = NewImage("Fill", countdownBack.transform, new Color(1f, 0.55f, 0.2f), Art.White);
            countdownFill.type = Image.Type.Filled;
            countdownFill.fillMethod = Image.FillMethod.Horizontal;
            countdownFill.fillOrigin = 0;
            countdownFill.raycastTarget = false;
            Stretch(countdownFill.rectTransform, 4f);
            countdownText = NewText("Seconds", countdownBack.transform, "", 16, Color.white, TextAnchor.MiddleCenter, true);
            countdownText.fontStyle = FontStyle.Bold;
            Stretch(countdownText.rectTransform);
            countdown.SetActive(false);

            toastText = NewText("Toast", hud.transform, "", 30, new Color(1f, 0.95f, 0.7f), TextAnchor.MiddleCenter, true);
            Place(toastText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 40), new Vector2(1200, 60));
            toastText.gameObject.SetActive(false);

            // Card choice modal (Slay the Spire "choose a card" screen)
            Image modalImage = NewImage("Card Choice", hud.transform, new Color(0.02f, 0.01f, 0.05f, 0.82f));
            modal = modalImage.gameObject;
            Stretch(modalImage.rectTransform);
            modalTitle = NewText("Title", modal.transform, "", 34, Gold, TextAnchor.MiddleCenter, true);
            Place(modalTitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0, -80), new Vector2(1500, 80));
            RectTransform scrollRect = NewRect("Scroll", modal.transform);
            Place(scrollRect, new Vector2(0.5f, 0.5f), new Vector2(0, 10), new Vector2(1600, 720));
            modalScroll = scrollRect.gameObject.AddComponent<ScrollRect>();
            modalScroll.horizontal = false;
            modalScroll.scrollSensitivity = 40f;
            Image viewport = NewImage("Viewport", scrollRect, new Color(0, 0, 0, 0.01f));
            Stretch(viewport.rectTransform);
            viewport.gameObject.AddComponent<RectMask2D>();
            modalGrid = NewRect("Grid", viewport.transform);
            modalGrid.anchorMin = new Vector2(0, 1);
            modalGrid.anchorMax = new Vector2(1, 1);
            modalGrid.pivot = new Vector2(0.5f, 1f);
            modalGrid.offsetMin = Vector2.zero;
            modalGrid.offsetMax = Vector2.zero;
            GridLayoutGroup grid = modalGrid.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(220, 330);
            grid.spacing = new Vector2(26, 20);
            grid.childAlignment = TextAnchor.UpperCenter;
            grid.padding = new RectOffset(10, 10, 10, 10);
            ContentSizeFitter gridFit = modalGrid.gameObject.AddComponent<ContentSizeFitter>();
            gridFit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            modalScroll.viewport = viewport.rectTransform;
            modalScroll.content = modalGrid;
            modalButtons = NewRect("Buttons", modal.transform);
            Place(modalButtons, new Vector2(0.5f, 0f), new Vector2(0, 60), new Vector2(1500, 70));
            HorizontalLayoutGroup modalRow = modalButtons.gameObject.AddComponent<HorizontalLayoutGroup>();
            modalRow.spacing = 16;
            modalRow.childAlignment = TextAnchor.MiddleCenter;
            modalRow.childControlWidth = false;
            modalRow.childControlHeight = false;
            modalRow.childForceExpandWidth = false;
            modal.SetActive(false);

            // Tooltip (keyword box next to an enlarged card)
            Image tip = NewPanel("Tooltip", hud.transform, new Color(0.08f, 0.06f, 0.12f, 0.95f));
            tooltip = tip.gameObject;
            tip.raycastTarget = false;
            tip.rectTransform.pivot = new Vector2(0f, 1f);
            tip.rectTransform.anchorMin = tip.rectTransform.anchorMax = Vector2.zero;
            tip.rectTransform.sizeDelta = new Vector2(330, 210);
            tooltipTitle = NewText("Title", tip.transform, "", 22, Gold, TextAnchor.UpperLeft, true);
            tooltipTitle.rectTransform.anchorMin = new Vector2(0, 1);
            tooltipTitle.rectTransform.anchorMax = new Vector2(1, 1);
            tooltipTitle.rectTransform.pivot = new Vector2(0.5f, 1f);
            tooltipTitle.rectTransform.offsetMin = new Vector2(14, -40);
            tooltipTitle.rectTransform.offsetMax = new Vector2(-14, -10);
            tooltipBody = NewText("Body", tip.transform, "", 17, Color.white, TextAnchor.UpperLeft);
            tooltipBody.rectTransform.anchorMin = Vector2.zero;
            tooltipBody.rectTransform.anchorMax = Vector2.one;
            tooltipBody.rectTransform.offsetMin = new Vector2(14, 10);
            tooltipBody.rectTransform.offsetMax = new Vector2(-14, -44);
            tooltip.SetActive(false);

            // Log drawer
            Image logImage = NewPanel("Log Panel", hud.transform, new Color(0.04f, 0.03f, 0.07f, 0.95f));
            logPanel = logImage.gameObject;
            Place(logImage.rectTransform, new Vector2(0, 0.5f), new Vector2(15, 0), new Vector2(520, 900), new Vector2(0, 0.5f));
            Text logTitle = NewText("Title", logPanel.transform, "Game Log", 26, Gold, TextAnchor.MiddleLeft, true);
            Place(logTitle.rectTransform, new Vector2(0, 1), new Vector2(20, -30), new Vector2(300, 40), new Vector2(0, 0.5f));
            Button closeLog = NewButton("Close", logPanel.transform, "Close", 18, QuietButton, () => logPanel.SetActive(false));
            Place(closeLog.GetComponent<RectTransform>(), new Vector2(1, 1), new Vector2(-15, -12), new Vector2(100, 40), new Vector2(1, 1));
            logText = NewText("Lines", logPanel.transform, "", 16, new Color(0.85f, 0.87f, 0.95f), TextAnchor.LowerLeft);
            logText.verticalOverflow = VerticalWrapMode.Truncate;
            logText.rectTransform.anchorMin = Vector2.zero;
            logText.rectTransform.anchorMax = Vector2.one;
            logText.rectTransform.offsetMin = new Vector2(18, 14);
            logText.rectTransform.offsetMax = new Vector2(-18, -60);
            logPanel.SetActive(false);

            hud.SetActive(false);
        }

        private Text MakeDie(RectTransform parent, Vector2 position)
        {
            Image die = NewImage("Die", parent, new Color(0.97f, 0.95f, 0.9f), Art.Button, true);
            Place(die.rectTransform, new Vector2(0.5f, 0.5f), position, new Vector2(76, 76));
            Text value = NewText("Value", die.transform, "6", 46, new Color(0.3f, 0.08f, 0.15f), TextAnchor.MiddleCenter);
            Stretch(value.rectTransform);
            value.fontStyle = FontStyle.Bold;
            return value;
        }

        public void ShowHud()
        {
            HideAllScreens();
            hud.SetActive(true);
            logPanel.SetActive(false);
            ClearRequest();
            ClearLog();
        }

        public void SetTurnText(string text)
        {
            turnText.text = text;
        }

        public void SetEnergy(string text, Vector3 world, Camera cam, bool active)
        {
            energyText.rectTransform.position = cam.WorldToScreenPoint(world);
            energyText.text = text;
            energyText.color = active ? Color.white : new Color(0.8f, 0.8f, 0.8f, 0.8f);
        }

        public void SetMyPlate(string text)
        {
            myPlateText.text = text;
        }

        public void SetEndTurn(bool interactable, bool urgent)
        {
            endTurnButton.interactable = interactable;
            float pulse = urgent ? 0.15f * Mathf.Sin(Time.time * 6f) : 0f;
            endTurnImage.color = interactable ? new Color(0.25f + pulse, 0.5f + pulse, 0.7f + pulse) : new Color(0.3f, 0.3f, 0.35f);
        }

        // ----- log & feed

        public void AddLog(string line)
        {
            logLines.Add(line);
            if (logLines.Count > 200)
            {
                logLines.RemoveAt(0);
            }

            logText.text = string.Join("\n", logLines.Skip(Math.Max(0, logLines.Count - 40)));

            feedLines.Add(line);
            feedTimes.Add(Time.time);
            while (feedLines.Count > 3)
            {
                feedLines.RemoveAt(0);
                feedTimes.RemoveAt(0);
            }
        }

        public void ClearLog()
        {
            logLines.Clear();
            feedLines.Clear();
            feedTimes.Clear();
            logText.text = "";
            feedText.text = "";
        }

        private void UpdateFeed()
        {
            if (!GameSettings.EventFeed || logPanel.activeSelf)
            {
                feedText.text = "";
                return;
            }

            List<string> lines = new List<string>();
            for (int i = 0; i < feedLines.Count; i++)
            {
                float age = Time.time - feedTimes[i];
                if (age > 7f)
                {
                    continue;
                }

                int alpha = Mathf.Clamp(Mathf.RoundToInt(255 * Mathf.Clamp01((7f - age) / 2f)), 0, 255);
                lines.Add($"<color=#FFFFFF{alpha:X2}>{Escape(feedLines[i])}</color>");
            }

            feedText.text = string.Join("\n", lines);
        }

        private static string Escape(string text)
        {
            return text.Replace("<", "(").Replace(">", ")");
        }

        public void ShowToast(string text, float seconds = 2.5f)
        {
            toastText.text = text;
            toastText.gameObject.SetActive(true);
            toastHideTime = Time.time + seconds;
        }

        // ----- dice

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
                    goal = roll.reversed ? $"slay {roll.slayOn}- / fail {roll.failOn}+" : $"slay {roll.slayOn}+ / fail {roll.failOn}-";
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

            diceDetail.text = $"<size=16>{Escape(roll.description)}</size>\n<b><size=34>Total {roll.Total}</size></b>  <size=17>({goal})</size>\n<size=15>{extras}</size>";
            diceHideTime = Time.time + 3.5f;
        }

        // ----- tooltip

        public void ShowTooltip(CardDefinition definition, Rect worldRect, Camera cam)
        {
            if (definition == null)
            {
                HideTooltip();
                return;
            }

            tooltip.SetActive(true);
            tooltipTitle.text = definition.displayName;
            tooltipBody.text = HereToSlayCardDatabase.FullDescription(definition);
            float scale = canvas.scaleFactor;
            float height = Mathf.Max(170f, tooltipBody.preferredHeight + 60f);
            RectTransform rect = tooltip.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(330, height);

            Vector3 topRight = cam.WorldToScreenPoint(new Vector3(worldRect.xMax, worldRect.yMax, 0));
            Vector3 topLeft = cam.WorldToScreenPoint(new Vector3(worldRect.xMin, worldRect.yMax, 0));
            float width = 330 * scale;
            float x = topRight.x + 12 * scale;
            if (x + width > UnityEngine.Screen.width)
            {
                x = topLeft.x - width - 12 * scale;
            }

            float y = Mathf.Clamp(topRight.y, height * scale + 10, UnityEngine.Screen.height - 10);
            rect.position = new Vector3(x, y, 0);
        }

        public void ShowTooltipAtPointer(CardDefinition definition, Vector2 pointer)
        {
            tooltip.SetActive(true);
            tooltipTitle.text = definition.displayName;
            tooltipBody.text = HereToSlayCardDatabase.FullDescription(definition);
            float scale = canvas.scaleFactor;
            float height = Mathf.Max(170f, tooltipBody.preferredHeight + 60f);
            RectTransform rect = tooltip.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(330, height);
            float x = pointer.x + 20 * scale;
            if (x + 330 * scale > UnityEngine.Screen.width)
            {
                x = pointer.x - 350 * scale;
            }

            rect.position = new Vector3(x, Mathf.Clamp(pointer.y, height * scale + 10, UnityEngine.Screen.height - 10), 0);
        }

        public void HideTooltip()
        {
            if (tooltip != null)
            {
                tooltip.SetActive(false);
            }
        }

        // ----- badges & seat plates

        public void RenderBadges(IList<WorldLabel> labels, Camera cam)
        {
            for (int i = 0; i < labels.Count; i++)
            {
                if (i >= badgePool.Count)
                {
                    Image circle = NewImage("Badge", badgeLayer, new Color(0.75f, 0.15f, 0.12f), Art.Circle);
                    circle.raycastTarget = false;
                    circle.rectTransform.sizeDelta = new Vector2(40, 40);
                    Text number = NewText("Number", circle.transform, "", 18, Color.white, TextAnchor.MiddleCenter, true);
                    number.fontStyle = FontStyle.Bold;
                    Stretch(number.rectTransform);
                    badgePool.Add(number);
                }

                Text text = badgePool[i];
                GameObject badge = text.transform.parent.gameObject;
                badge.SetActive(true);
                text.text = labels[i].text;
                badge.GetComponent<RectTransform>().position = cam.WorldToScreenPoint(labels[i].position);
            }

            for (int i = labels.Count; i < badgePool.Count; i++)
            {
                badgePool[i].transform.parent.gameObject.SetActive(false);
            }
        }

        private const int SeatFanSize = 9;

        /// <summary>UNO-style seat plates: avatar, name, progress and a fan of face-down cards with a count bubble.</summary>
        public void RenderSeats(IList<SeatInfo> seats, Camera cam)
        {
            for (int i = 0; i < seats.Count; i++)
            {
                if (i >= seatPool.Count)
                {
                    Image plate = NewPanel("Seat", seatLayer, new Color(0.08f, 0.06f, 0.12f, 0.94f));
                    plate.rectTransform.sizeDelta = new Vector2(310, 72);
                    Outline outline = plate.gameObject.AddComponent<Outline>();
                    outline.effectDistance = new Vector2(3, -3);
                    Image avatar = NewImage("Avatar", plate.transform, Color.white, Art.Circle);
                    Place(avatar.rectTransform, new Vector2(0, 0.5f), new Vector2(36, 0), new Vector2(54, 54));
                    Text initial = NewText("Initial", avatar.transform, "", 26, Color.white, TextAnchor.MiddleCenter, true);
                    initial.fontStyle = FontStyle.Bold;
                    Stretch(initial.rectTransform);
                    Text name = NewText("Name", plate.transform, "", 18, Color.white, TextAnchor.UpperLeft, true);
                    name.rectTransform.anchorMin = Vector2.zero;
                    name.rectTransform.anchorMax = Vector2.one;
                    name.rectTransform.offsetMin = new Vector2(70, 6);
                    name.rectTransform.offsetMax = new Vector2(-100, -6);

                    RectTransform fanRect = NewRect("Fan", plate.transform);
                    Place(fanRect, new Vector2(1, 0.5f), new Vector2(-52, -2), new Vector2(90, 60));
                    for (int c = 0; c < SeatFanSize; c++)
                    {
                        Image back = NewImage("Back", fanRect, Color.white, Art.CardBack);
                        back.preserveAspect = true;
                        back.raycastTarget = false;
                        back.rectTransform.sizeDelta = new Vector2(30, 42);
                    }

                    Image bubbleImage = NewImage("Count", plate.transform, new Color(0.8f, 0.15f, 0.12f), Art.Circle);
                    bubbleImage.raycastTarget = false;
                    Place(bubbleImage.rectTransform, new Vector2(1, 1), new Vector2(-8, -6), new Vector2(34, 34));
                    Text count = NewText("Number", bubbleImage.transform, "", 17, Color.white, TextAnchor.MiddleCenter, true);
                    count.fontStyle = FontStyle.Bold;
                    Stretch(count.rectTransform);
                    seatPool.Add(plate.gameObject);
                }

                GameObject go = seatPool[i];
                SeatInfo seat = seats[i];
                PlayerState p = seat.player;
                go.SetActive(true);
                go.GetComponent<RectTransform>().position = cam.WorldToScreenPoint(seat.plateWorld);
                go.GetComponent<Outline>().effectColor = seat.isCurrent ? new Color(1f, 0.85f, 0.3f, 0.5f + 0.4f * Mathf.Sin(Time.time * 5f)) : new Color(0, 0, 0, 0.6f);
                Image avatarImage = go.transform.Find("Avatar").GetComponent<Image>();
                avatarImage.color = p.leader != null ? Art.ClassColor(p.leader.def.heroClass) : Color.gray;
                avatarImage.GetComponentInChildren<Text>().text = string.IsNullOrEmpty(p.name) ? "?" : p.name.Substring(0, 1).ToUpperInvariant();
                string tag = p.isHuman ? "" : " <color=#9ad0ff>AI</color>";
                go.transform.Find("Name").GetComponent<Text>().text =
                    $"<b>{Escape(p.name)}</b>{tag}\n<size=14>Classes {p.DistinctClassCount()}/6\nSlain {p.slainMonsters.Count}/3</size>";

                // Fan of face-down cards
                Transform fan = go.transform.Find("Fan");
                int shown = Mathf.Min(p.hand.Count, SeatFanSize);
                float spread = Mathf.Min(9f, 60f / Mathf.Max(1, shown));
                for (int c = 0; c < SeatFanSize; c++)
                {
                    RectTransform back = (RectTransform)fan.GetChild(c);
                    bool on = c < shown;
                    back.gameObject.SetActive(on);
                    if (!on)
                    {
                        continue;
                    }

                    float t = c - (shown - 1) / 2f;
                    back.anchoredPosition = new Vector2(t * spread, -Mathf.Abs(t) * 0.8f);
                    back.localRotation = Quaternion.Euler(0, 0, -t * 6f);
                }

                Transform bubble = go.transform.Find("Count");
                bubble.gameObject.SetActive(p.hand.Count > 0);
                bubble.GetComponentInChildren<Text>().text = p.hand.Count.ToString();
            }

            for (int i = seats.Count; i < seatPool.Count; i++)
            {
                seatPool[i].SetActive(false);
            }
        }

        // ----- requests

        public bool IsShowing(ChoiceRequest request)
        {
            return shownRequest == request;
        }

        public void ClearRequest()
        {
            shownRequest = null;
            banner.SetActive(false);
            modal.SetActive(false);
            ClearChildren(bannerButtons);
            ClearChildren(modalGrid);
            ClearChildren(modalButtons);
        }

        private static void ClearChildren(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                Destroy(parent.GetChild(i).gameObject);
            }
        }

        /// <summary>Shows (remaining &gt; 0) or hides the Challenge countdown bar.</summary>
        public void SetCountdown(float remaining, float total, string label = "Challenge?")
        {
            if (remaining <= 0f || total <= 0f)
            {
                if (countdown.activeSelf)
                {
                    countdown.SetActive(false);
                }

                return;
            }

            if (!countdown.activeSelf)
            {
                countdown.SetActive(true);
                countdown.transform.SetAsLastSibling();
            }

            float fraction = Mathf.Clamp01(remaining / total);
            countdownFill.fillAmount = fraction;
            countdownFill.color = Color.Lerp(new Color(0.9f, 0.2f, 0.15f), new Color(1f, 0.7f, 0.2f), fraction);
            countdownText.text = $"{label} {remaining:0.0}s";
        }

        public void ShowWaiting(string text)
        {
            if (shownRequest != null)
            {
                ClearRequest();
            }

            SetBannerText(text);
            ClearChildren(bannerButtons);
            banner.SetActive(true);
        }

        /// <summary>
        /// Shows a request. Cards already visible on the table are picked by clicking them (they glow);
        /// other choices appear as buttons in the banner, or as a big card-choice screen for cards you can't see.
        /// </summary>
        public void ShowRequest(ChoiceRequest request, Func<CardInstance, bool> visibleOnBoard)
        {
            if (shownRequest == request)
            {
                return;
            }

            ClearRequest();
            shownRequest = request;

            List<int> boardPickable = new List<int>();
            List<int> offBoardCards = new List<int>();
            List<int> plain = new List<int>();
            for (int i = 0; i < request.options.Count; i++)
            {
                ChoiceOption option = request.options[i];
                if (option.card == null)
                {
                    plain.Add(i);
                }
                else if (visibleOnBoard(option.card) && request.options.Count(o => o.card == option.card) == 1 && request.kind != ChoiceKind.Modifier)
                {
                    boardPickable.Add(i);
                }
                else
                {
                    offBoardCards.Add(i);
                }
            }

            if (request.kind == ChoiceKind.YesNo || request.kind == ChoiceKind.Challenge)
            {
                // Simple questions: every answer is a button (a card involved still glows on the table).
                plain = Enumerable.Range(0, request.options.Count).ToList();
                boardPickable.Clear();
                offBoardCards.Clear();
            }

            bool useModal = request.kind == ChoiceKind.Info
                || (request.kind != ChoiceKind.Modifier && offBoardCards.Count > 0 && offBoardCards.Any(i => !visibleOnBoard(request.options[i].card)))
                || offBoardCards.Count > 6
                || (request.revealed.Count > 0 && request.revealed.Any(c => !visibleOnBoard(c)) && request.kind != ChoiceKind.Challenge && request.kind != ChoiceKind.YesNo);

            if (request.kind == ChoiceKind.MainAction)
            {
                // "End turn" lives on the End Turn button and drawing on the deck; the rest go in the banner.
                plain = plain.Where(i => request.options[i].action != "end" && request.options[i].action != "draw").ToList();
            }

            if (useModal)
            {
                ShowModal(request, offBoardCards.Concat(boardPickable).ToList(), plain);
                return;
            }

            banner.SetActive(true);
            string hint = boardPickable.Count > 0
                ? (request.kind == ChoiceKind.MainAction ? "\n<size=15><color=#b8f5c0>Click or drag a glowing card · click the deck to draw · End Turn when done</color></size>" : "\n<size=15><color=#b8f5c0>Click a glowing card</color></size>")
                : "";
            SetBannerText(Escape(request.prompt) + hint);

            foreach (int i in offBoardCards.Concat(plain))
            {
                int index = i;
                ChoiceOption option = request.options[i];
                Button button = NewButton("Option", bannerButtons, Escape(option.label), 19, OptionColor(option), () => OnOptionClicked?.Invoke(request, index));
                LayoutElement element = button.gameObject.AddComponent<LayoutElement>();
                element.preferredHeight = 46;
                element.minWidth = 150;
                Text label = button.GetComponentInChildren<Text>();
                element.preferredWidth = Mathf.Max(150, label.preferredWidth + 40 + (option.card != null ? 40 : 0));
                if (option.card != null)
                {
                    Image thumb = NewImage("Thumb", button.transform, Color.white, Art.CardFace(option.card.def));
                    thumb.preserveAspect = true;
                    thumb.raycastTarget = false;
                    thumb.rectTransform.anchorMin = new Vector2(0, 0);
                    thumb.rectTransform.anchorMax = new Vector2(0, 1);
                    thumb.rectTransform.offsetMin = new Vector2(6, 3);
                    thumb.rectTransform.offsetMax = new Vector2(38, -3);
                    label.rectTransform.offsetMin = new Vector2(44, 2);
                    CardDefinition def = option.card.def;
                    AddHover(button.gameObject, () => ShowTooltipAtPointer(def, PointerPosition()), HideTooltip);
                }
            }
        }

        private void SetBannerText(string text)
        {
            bannerText.text = text;
            bannerTextLayout.preferredWidth = Mathf.Min(bannerText.preferredWidth + 4, 760f);
        }

        private void ShowModal(ChoiceRequest request, List<int> cardOptions, List<int> plainOptions)
        {
            modal.SetActive(true);
            modalTitle.text = Escape(request.prompt);

            List<CardInstance> shownCards = new List<CardInstance>();
            foreach (int i in cardOptions)
            {
                int index = i;
                ChoiceOption option = request.options[i];
                shownCards.Add(option.card);
                AddModalCard(option.card, option.label != option.card.Name ? option.label : null, () => OnOptionClicked?.Invoke(request, index));
            }

            foreach (CardInstance card in request.revealed)
            {
                if (!shownCards.Contains(card))
                {
                    AddModalCard(card, null, null);
                }
            }

            foreach (int i in plainOptions)
            {
                int index = i;
                ChoiceOption option = request.options[i];
                Button button = NewButton("Option", modalButtons, Escape(option.label), 22, OptionColor(option), () => OnOptionClicked?.Invoke(request, index));
                button.GetComponent<RectTransform>().sizeDelta = new Vector2(Mathf.Max(220, button.GetComponentInChildren<Text>().preferredWidth + 50), 62);
            }

            modalScroll.verticalNormalizedPosition = 1f;
        }

        private void AddModalCard(CardInstance card, string caption, Action onClick)
        {
            RectTransform cell = NewRect("Card", modalGrid);
            Image image = NewImage("Face", cell, Color.white, Art.CardFace(card.def));
            image.preserveAspect = true;
            image.rectTransform.anchorMin = Vector2.zero;
            image.rectTransform.anchorMax = Vector2.one;
            image.rectTransform.offsetMin = new Vector2(0, caption != null ? 0 : 0);
            image.rectTransform.offsetMax = new Vector2(0, caption != null ? -28 : 0);
            image.raycastTarget = true;
            if (onClick != null)
            {
                Button button = image.gameObject.AddComponent<Button>();
                ColorBlock colors = button.colors;
                colors.highlightedColor = new Color(1f, 1f, 0.85f);
                colors.pressedColor = new Color(0.8f, 0.8f, 0.8f);
                button.colors = colors;
                button.onClick.AddListener(() => GuardedClick(button, onClick));
                Outline outline = image.gameObject.AddComponent<Outline>();
                outline.effectColor = new Color(0.45f, 1f, 0.55f, 0.8f);
                outline.effectDistance = new Vector2(4, -4);
            }
            else
            {
                image.color = new Color(0.8f, 0.8f, 0.8f);
            }

            if (caption != null)
            {
                Text text = NewText("Owner", cell, Escape(caption), 17, Gold, TextAnchor.MiddleCenter, true);
                text.rectTransform.anchorMin = new Vector2(0, 1);
                text.rectTransform.anchorMax = new Vector2(1, 1);
                text.rectTransform.pivot = new Vector2(0.5f, 1f);
                text.rectTransform.offsetMin = new Vector2(0, -26);
                text.rectTransform.offsetMax = Vector2.zero;
            }

            CardDefinition def = card.def;
            AddHover(image.gameObject, () =>
            {
                RectTransform rt = image.rectTransform;
                Vector3[] corners = new Vector3[4];
                rt.GetWorldCorners(corners);
                ShowTooltipAtPointer(def, new Vector2(corners[2].x, corners[2].y));
            }, HideTooltip);
        }

        /// <summary>Read-only view of the whole discard pile.</summary>
        public void ShowDiscardPile(IList<CardInstance> cards)
        {
            if (shownRequest != null || cards.Count == 0)
            {
                return;
            }

            modal.SetActive(true);
            modalTitle.text = $"Discard pile ({cards.Count} cards)";
            ClearChildren(modalGrid);
            ClearChildren(modalButtons);
            for (int i = cards.Count - 1; i >= 0; i--)
            {
                AddModalCard(cards[i], null, null);
            }

            Button close = NewButton("Close", modalButtons, "Close", 22, AccentButton, () =>
            {
                modal.SetActive(false);
                ClearChildren(modalGrid);
                ClearChildren(modalButtons);
                HideTooltip();
            });
            close.GetComponent<RectTransform>().sizeDelta = new Vector2(220, 62);
            modalScroll.verticalNormalizedPosition = 1f;
        }

        private static Vector2 PointerPosition()
        {
            return UnityEngine.InputSystem.Mouse.current != null ? UnityEngine.InputSystem.Mouse.current.position.ReadValue() : Vector2.zero;
        }

        private static Color OptionColor(ChoiceOption option)
        {
            string label = option.label;
            if (label == "Pass" || label.StartsWith("Let it") || label.StartsWith("No") || label.StartsWith("Keep") || label.StartsWith("Don't") || label.StartsWith("Stop") || label.StartsWith("Not now"))
            {
                return QuietButton;
            }

            if (label.StartsWith("Challenge") || label.StartsWith("Roll"))
            {
                return AccentButton;
            }

            return ButtonColor;
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

            if (hud != null && hud.activeSelf)
            {
                UpdateFeed();
            }
        }
    }

    /// <summary>Pointer enter/exit only, so scroll and drag events still reach ScrollRects.</summary>
    public sealed class HoverRelay : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public Action onEnter;
        public Action onExit;

        public void OnPointerEnter(PointerEventData eventData)
        {
            onEnter?.Invoke();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            onExit?.Invoke();
        }
    }
}
