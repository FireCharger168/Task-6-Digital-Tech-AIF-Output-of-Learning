using System;
using System.Collections.Generic;
using System.Linq;
using HereToSlay.Net;
using HereToSlay.View;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace HereToSlay
{
    /// <summary>
    /// Entry point. Builds the camera, the layered board and the UI, runs the title screen / game creation / settings flow,
    /// and drives the rules engine: human decisions come from clicks and drags, AI decisions from <see cref="AIBrain"/>.
    /// It boots itself in any scene, so pressing Play in SampleScene is enough.
    /// </summary>
    public sealed partial class HereToSlayGame : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoBoot()
        {
            if (FindAnyObjectByType<HereToSlayGame>() == null)
            {
                new GameObject("Here To Slay").AddComponent<HereToSlayGame>();
            }
        }

        private static readonly List<RaycastResult> UiHits = new List<RaycastResult>();

        /// <summary>Raycasts the UI directly (works even when the window is not focused).</summary>
        public static bool PointerOverUI()
        {
            return RaycastUI() != null;
        }

        private static GameObject RaycastUI()
        {
            if (EventSystem.current == null || Mouse.current == null)
            {
                return null;
            }

            PointerEventData data = new PointerEventData(EventSystem.current) { position = Mouse.current.position.ReadValue() };
            UiHits.Clear();
            EventSystem.current.RaycastAll(data, UiHits);
            return UiHits.Count > 0 ? UiHits[0].gameObject : null;
        }

        private static GameObject pressedUiButton;

        /// <summary>
        /// Safety net for UI buttons: if the EventSystem missed a click (it ignores input while the window
        /// is not focused, e.g. right after alt-tabbing back), deliver it ourselves. Runs in LateUpdate so a
        /// click the EventSystem did deliver this frame is never repeated.
        /// </summary>
        private void LateUpdate()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null)
            {
                return;
            }

            if (mouse.leftButton.wasPressedThisFrame)
            {
                GameObject hit = RaycastUI();
                UnityEngine.UI.Button button = hit != null ? hit.GetComponentInParent<UnityEngine.UI.Button>() : null;
                pressedUiButton = button != null ? button.gameObject : null;
            }

            if (mouse.leftButton.wasReleasedThisFrame && pressedUiButton != null)
            {
                GameObject pressed = pressedUiButton;
                pressedUiButton = null;
                if (GameUI.LastClickFrame == Time.frameCount)
                {
                    return;
                }

                GameObject hit = RaycastUI();
                UnityEngine.UI.Button button = hit != null ? hit.GetComponentInParent<UnityEngine.UI.Button>() : null;
                if (button != null && button.gameObject == pressed && button.IsActive() && button.IsInteractable())
                {
                    button.onClick.Invoke();
                }
            }
        }

        private Camera cam;
        private BoardView board;
        private GameUI ui;

        private GameEngine engine;
        private EngineRunner runner;
        private readonly System.Random aiRandom = new System.Random();
        private readonly BoardFrame frame = new BoardFrame();

        private object waitingOn;
        private float pauseTimer;
        private float aiTimer;
        private bool requestNeedsShowing;
        private ChoiceRequest timedRequest;
        private float timerLeft;
        private PlayerState autoDrawPlayer;
        private int autoDrawTurn = -1;
        private bool gameOverShown;

        // hot-seat privacy
        private List<PlayerState> humans = new List<PlayerState>();
        private PlayerState viewer;
        private bool viewerConfirmed;

        // pointer
        private CardInstance pressedCard;
        private Vector3 pressWorld;
        private bool dragging;

        private void Awake()
        {
            Application.targetFrameRate = 60;
            Application.runInBackground = true;
            GameSettings.Load();
            GameSettings.Apply();
            SetupCamera();

            board = new GameObject("Board").AddComponent<BoardView>();
            board.transform.SetParent(transform, false);
            board.Build(cam);

            ui = new GameObject("UI").AddComponent<GameUI>();
            ui.transform.SetParent(transform, false);
            ui.Build();
            ui.OnStartGame = StartGame;
            ui.OnPlayAgain = () =>
            {
                if (hostSession != null || clientSession != null)
                {
                    BackToLobby();
                }
                else if (ui.LastConfig != null)
                {
                    StartGame(ui.LastConfig);
                }
                else
                {
                    QuitToTitle();
                }
            };
            ui.OnQuitToTitle = QuitToTitle;
            ui.OnQuitGame = QuitApplication;
            ui.OnOptionClicked = OnOptionClicked;
            ui.OnEndTurnClicked = EndTurnClicked;
            ui.OnHostRequested = HostRequested;
            ui.OnJoinRequested = JoinRequested;
            ui.OnHostOptions = HostOptions;
            ui.OnHostStart = HostStart;
            ui.OnLeaveOnline = () =>
            {
                CloseOnline();
                ui.ShowOnlineMenu("");
            };
            ui.ShowTitle();
        }

        private void SetupCamera()
        {
            cam = Camera.main;
            if (cam == null)
            {
                GameObject go = new GameObject("Main Camera");
                go.tag = "MainCamera";
                cam = go.AddComponent<Camera>();
            }

            cam.orthographic = true;
            cam.transform.position = new Vector3(0f, 0f, -10f);
            cam.backgroundColor = new Color(0.09f, 0.08f, 0.13f);
            cam.clearFlags = CameraClearFlags.SolidColor;
            FitCamera();
        }

        private void FitCamera()
        {
            // Always show at least 19.2 x 10.8 world units.
            cam.orthographicSize = Mathf.Max(5.4f, 9.6f / Mathf.Max(0.1f, cam.aspect));
        }

        private static void QuitApplication()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ------------------------------------------------------------------ game lifetime

        private void StartGame(GameConfig config)
        {
            engine = new GameEngine(config.seats, null, config.randomLeaders, config.randomFirstPlayer);
            engine.OnLog += line => ui.AddLog(line);
            engine.OnRoll += roll => ui.ShowRoll(roll);
            engine.OnReveal += (who, cards, caption) =>
            {
                if (who == viewer && who.isHuman && HandFaceUp())
                {
                    ui.ShowToast(caption);
                }
            };

            if (hostSession != null)
            {
                hostSession.BeginGame(engine);
            }

            runner = new EngineRunner(engine.Run());
            waitingOn = null;
            requestNeedsShowing = false;
            gameOverShown = false;
            pressedCard = null;
            dragging = false;

            humans = engine.players.Where(p => p.isHuman && !p.isRemote).ToList();
            viewer = humans.Count > 0 ? humans[0] : engine.players[0];
            viewerConfirmed = humans.Count <= 1;

            board.SetVisible(true);
            board.SyncEmpty();
            ui.ShowHud();
        }

        private void QuitToTitle()
        {
            CloseOnline();
            engine = null;
            runner = null;
            waitingOn = null;
            board.SyncEmpty();
            ui.HideTooltip();
            ui.ShowTitle();
        }

        private bool HandFaceUp()
        {
            if (humans.Count <= 1)
            {
                return true;
            }

            if (!viewerConfirmed || ui.PassScreenOpen)
            {
                return false;
            }

            return engine.Current == viewer || (waitingOn is ChoiceRequest request && request.chooser == viewer);
        }

        // ------------------------------------------------------------------ frame loop

        private void Update()
        {
            FitCamera();
            PollNetwork();

            if (engine == null)
            {
                board.SyncEmpty();
                return;
            }

            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame && !ui.PassScreenOpen)
            {
                ui.TogglePause();
            }

            if (IsClient)
            {
                if (!gameOverShown)
                {
                    ClientAdvance();
                }
            }
            else if ((!ui.IsPaused || hostSession != null) && !gameOverShown)
            {
                // Online games keep running while the host looks at the pause menu: other people are playing.
                AdvanceEngine();
            }

            if (hostSession != null && hostSession.InGame)
            {
                hostSession.Poll(waitingOn as ChoiceRequest);
            }

            if (humans.Count == 0)
            {
                viewer = engine.Current;
            }

            BuildFrame();
            board.Sync(frame);

            if (requestNeedsShowing && waitingOn is ChoiceRequest pending && !ui.PassScreenOpen)
            {
                requestNeedsShowing = false;
                ui.ShowRequest(pending, board.IsVisibleFaceUp);
            }

            RenderHud();
            HandlePointer();
            UpdateDecisionTimer();

            if (engine.GameOver && !gameOverShown)
            {
                gameOverShown = true;
                runner = null;
                waitingOn = null;
                PlayerState winner = engine.winner;
                string reason = winner.slainMonsters.Count >= HereToSlayCardDatabase.MonstersRequiredToWin
                    ? "Three Monsters slain!"
                    : "A Party of all six classes!";
                ui.ShowGameOver($"{winner.name} wins!\n<size=26>{reason}</size>", hostSession != null || clientSession != null ? "Back to Lobby" : "Play Again");
            }
        }

        private void AdvanceEngine()
        {
            if (runner == null)
            {
                return;
            }

            float speed = GameSettings.GameSpeed;
            for (int guard = 0; guard < 50; guard++)
            {
                if (waitingOn is Pause)
                {
                    pauseTimer -= Time.deltaTime * speed;
                    if (pauseTimer > 0f)
                    {
                        return;
                    }

                    runner.ClearPause();
                    waitingOn = null;
                }
                else if (waitingOn is ChoiceRequest request)
                {
                    if (!request.Resolved)
                    {
                        if (!request.chooser.isHuman)
                        {
                            aiTimer -= Time.deltaTime * speed;
                            if (aiTimer <= 0f)
                            {
                                request.Select(AIBrain.Choose(request, aiRandom));
                            }
                        }

                        if (!request.Resolved)
                        {
                            return;
                        }
                    }

                    waitingOn = null;
                    ui.ClearRequest();
                }

                object next;
                try
                {
                    next = runner.Step();
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                    ui.AddLog("Engine error: " + exception.Message);
                    runner = null;
                    return;
                }

                if (next == null)
                {
                    runner = null;
                    return;
                }

                waitingOn = next;
                if (next is Pause pause)
                {
                    pauseTimer = pause.seconds;
                }
                else if (next is ChoiceRequest choice)
                {
                    OnNewRequest(choice);
                }
            }
        }

        private void OnNewRequest(ChoiceRequest request)
        {
            aiTimer = request.kind == ChoiceKind.MainAction ? GameSettings.AiThinkTime * 1.4f : GameSettings.AiThinkTime;

            if (request.chooser.isRemote)
            {
                ui.ShowWaiting($"{request.chooser.name} is choosing...");
                return;
            }

            if (!request.chooser.isHuman)
            {
                ui.ShowWaiting($"{request.chooser.name} is thinking...");
                return;
            }

            if (humans.Count > 1 && (request.chooser != viewer || !viewerConfirmed))
            {
                // Hot-seat: hide everything until the right player confirms they have the device.
                viewer = request.chooser;
                viewerConfirmed = false;
                ui.ClearRequest();
                string reason = request.prompt.Contains("Party Leader")
                    ? $"{request.chooser.name} chooses a Party Leader"
                    : request.kind == ChoiceKind.MainAction && engine.Current == request.chooser
                    ? "Next turn"
                    : request.kind == ChoiceKind.Challenge || request.kind == ChoiceKind.Modifier
                        ? $"{request.chooser.name} may respond"
                        : $"{request.chooser.name} needs to make a choice";
                ui.ShowPassScreen(request.chooser.name, reason, () =>
                {
                    viewerConfirmed = true;
                    requestNeedsShowing = true;
                    AnnounceTurn(request);
                });
                return;
            }

            viewer = request.chooser;
            requestNeedsShowing = true;
            AnnounceTurn(request);
        }

        private int announcedTurn = -1;

        private void AnnounceTurn(ChoiceRequest request)
        {
            if (request.kind == ChoiceKind.MainAction && engine.Current == request.chooser && announcedTurn != engine.turnNumber)
            {
                announcedTurn = engine.turnNumber;
                ui.ShowToast(humans.Count > 1 ? $"{request.chooser.name}'s turn!" : "Your turn!", 1.4f);
            }
        }

        private ChoiceRequest HumanRequest()
        {
            if (waitingOn is ChoiceRequest request && !request.Resolved && request.chooser.isHuman && request.chooser == viewer
                && viewerConfirmed && !ui.PassScreenOpen && !ui.IsPaused && ui.IsShowing(request))
            {
                return request;
            }

            return null;
        }

        private void BuildFrame()
        {
            frame.game = engine;
            frame.viewer = viewer;
            frame.handFaceUp = HandFaceUp();
            frame.hoverZoom = GameSettings.HoverZoom;
            frame.selectable.Clear();
            frame.deckSelectable = false;

            ChoiceRequest request = HumanRequest();
            if (request != null && !ui.ModalOpen)
            {
                foreach (ChoiceOption option in request.options)
                {
                    if (option.card != null && request.options.Count(o => o.card == option.card) == 1)
                    {
                        frame.selectable.Add(option.card);
                    }

                    if (option.action == "draw")
                    {
                        frame.deckSelectable = true;
                    }
                }
            }

            frame.dragging = dragging ? pressedCard : null;
            frame.dragWorld = board.PointerWorld();
        }

        private void RenderHud()
        {
            ui.RenderBadges(board.labels, cam);
            ui.RenderSeats(board.seats, cam);

            PlayerState current = engine.Current;
            if (current != null)
            {
                ui.SetTurnText(engine.turnNumber == 0 ? "Choosing Party Leaders..." : $"Turn {engine.turnNumber}  ·  <color=#ffd166>{current.name}</color>'s turn");
            }

            if (viewer != null)
            {
                bool myTurn = current == viewer;
                int ap = myTurn ? viewer.actionPoints : 0;
                ui.SetEnergy($"{ap}/{viewer.MaxActionPoints()}", board.OrbPosition, cam, myTurn && ap > 0);
                string leader = viewer.leader != null ? viewer.leader.Name : "no leader yet";
                string who = humans.Count == 0 ? "Watching" : "You";
                ui.SetMyPlate($"<b><size=22>{viewer.name}</size></b>  <color=#aaaaaa>({who})</color>\n{leader}\nClasses {viewer.DistinctClassCount()}/6  ·  Slain {viewer.slainMonsters.Count}/3");
            }

            ChoiceRequest request = HumanRequest();
            bool canEnd = request != null && request.kind == ChoiceKind.MainAction;
            ui.SetEndTurn(canEnd, canEnd && request.options.Count(o => o.action != "end") == 0);

            // Tooltip for enlarged board / hand cards (the UI handles its own hovers).
            if (!PointerOverUI())
            {
                if (board.ZoomCard != null && GameSettings.HoverZoom && !dragging)
                {
                    ui.ShowTooltip(board.ZoomCard.def, board.ZoomRect, cam);
                }
                else
                {
                    ui.HideTooltip();
                }
            }
        }

        /// <summary>
        /// Every human decision is on a clock (Settings): turn actions, other choices, and Challenge/Modifier reactions.
        /// The clock starts once the prompt is on screen (after any pass-the-device screen) and stops while paused.
        /// When it runs out: on your turn the remaining energy is spent drawing cards and the turn ends;
        /// reactions pass; other choices are made for you by the AI heuristics.
        /// </summary>
        private void UpdateDecisionTimer()
        {
            ChoiceRequest request = HumanRequest();
            if (request == null)
            {
                // Keep the remaining time while paused / passing the device; forget it once the question is gone.
                if (!(waitingOn is ChoiceRequest waiting && waiting == timedRequest && !waiting.Resolved))
                {
                    timedRequest = null;
                }

                ui.SetCountdown(0f, 0f);
                return;
            }

            // A timed-out turn keeps drawing until the energy is gone, then ends.
            if (request.kind == ChoiceKind.MainAction && autoDrawPlayer == request.chooser && autoDrawTurn == engine.turnNumber)
            {
                int draw = request.options.FindIndex(o => o.action == "draw");
                int end = request.options.FindIndex(o => o.action == "end");
                OnOptionClicked(request, draw >= 0 ? draw : end);
                return;
            }

            float seconds = TimeLimit(request);
            if (seconds <= 0f)
            {
                ui.SetCountdown(0f, 0f);
                return;
            }

            if (timedRequest != request)
            {
                timedRequest = request;
                timerLeft = seconds;
            }

            timerLeft -= Time.unscaledDeltaTime;
            ui.SetCountdown(Mathf.Max(0.01f, timerLeft), seconds, TimerLabel(request));
            if (timerLeft > 0f)
            {
                return;
            }

            timedRequest = null;
            ui.SetCountdown(0f, 0f);
            OnOptionClicked(request, TimeoutChoice(request));
        }

        private static float TimeLimit(ChoiceRequest request)
        {
            // Online clients use the host's clock.
            return request.timeLimit >= 0f ? request.timeLimit : TimeLimit(request.kind);
        }

        private static float TimeLimit(ChoiceKind kind)
        {
            switch (kind)
            {
                case ChoiceKind.MainAction:
                    return GameSettings.TurnSeconds;
                case ChoiceKind.Challenge:
                case ChoiceKind.Modifier:
                    return GameSettings.ReactionSeconds;
                default:
                    return GameSettings.ChoiceSeconds;
            }
        }

        private static string TimerLabel(ChoiceRequest request)
        {
            switch (request.kind)
            {
                case ChoiceKind.MainAction:
                    return "Turn";
                case ChoiceKind.Challenge:
                    return "Challenge?";
                case ChoiceKind.Modifier:
                    return "Modifier?";
                default:
                    return "Choose";
            }
        }

        private int TimeoutChoice(ChoiceRequest request)
        {
            switch (request.kind)
            {
                case ChoiceKind.MainAction:
                {
                    ui.ShowToast("Time's up! Drawing cards with your remaining energy.", 2f);
                    autoDrawPlayer = request.chooser;
                    autoDrawTurn = engine.turnNumber;
                    int draw = request.options.FindIndex(o => o.action == "draw");
                    return draw >= 0 ? draw : request.options.FindIndex(o => o.action == "end");
                }

                case ChoiceKind.Challenge:
                case ChoiceKind.Modifier:
                {
                    ui.ShowToast(request.kind == ChoiceKind.Challenge ? "Too slow - no Challenge!" : "Too slow - no Modifier.", 1.5f);
                    int pass = request.options.FindIndex(o => o.card == null);
                    return pass >= 0 ? pass : request.options.Count - 1;
                }

                default:
                    ui.ShowToast("Time's up - a choice was made for you.", 1.5f);
                    return AIBrain.Choose(request, aiRandom);
            }
        }

        // ------------------------------------------------------------------ input

        private void HandlePointer()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null)
            {
                return;
            }

            Vector3 world = board.PointerWorld();
            ChoiceRequest request = HumanRequest();

            if (mouse.leftButton.wasPressedThisFrame && !PointerOverUI())
            {
                pressedCard = null;
                dragging = false;

                if (request != null && !ui.ModalOpen)
                {
                    CardView hovered = board.Hovered;
                    if (hovered != null && hovered.card != null && frame.selectable.Contains(hovered.card))
                    {
                        if (board.HoveredInHand)
                        {
                            pressedCard = hovered.card;
                            pressWorld = world;
                        }
                        else
                        {
                            SelectCard(request, hovered.card);
                        }

                        return;
                    }

                    if (board.HoveringDeck && frame.deckSelectable)
                    {
                        int draw = request.options.FindIndex(o => o.action == "draw");
                        if (draw >= 0)
                        {
                            OnOptionClicked(request, draw);
                        }

                        return;
                    }
                }

                if (board.HoveringDiscard && !ui.ModalOpen && (request == null || !frame.selectable.Contains(engine.discardPile.LastOrDefault())))
                {
                    ui.ShowDiscardPile(engine.discardPile);
                }
            }

            if (pressedCard != null && mouse.leftButton.isPressed)
            {
                if (!dragging && (world - pressWorld).magnitude > 0.3f)
                {
                    dragging = true;
                }
            }

            if (pressedCard != null && mouse.leftButton.wasReleasedThisFrame)
            {
                CardInstance card = pressedCard;
                bool wasDragging = dragging;
                pressedCard = null;
                dragging = false;

                if (request == null)
                {
                    return;
                }

                if (!wasDragging || world.y > BoardView.PlayLineY)
                {
                    SelectCard(request, card);
                }
            }
        }

        private void SelectCard(ChoiceRequest request, CardInstance card)
        {
            int index = request.options.FindIndex(o => o.card == card);
            if (index >= 0)
            {
                OnOptionClicked(request, index);
            }
        }

        private void EndTurnClicked()
        {
            ChoiceRequest request = HumanRequest();
            if (request == null || request.kind != ChoiceKind.MainAction)
            {
                return;
            }

            int index = request.options.FindIndex(o => o.action == "end");
            if (index >= 0)
            {
                OnOptionClicked(request, index);
            }
        }

        private void OnOptionClicked(ChoiceRequest request, int index)
        {
            if (request == null || request.Resolved || !ReferenceEquals(waitingOn, request))
            {
                return;
            }

            request.Select(index);
            ui.ClearRequest();
            ui.HideTooltip();
        }
    }
}
