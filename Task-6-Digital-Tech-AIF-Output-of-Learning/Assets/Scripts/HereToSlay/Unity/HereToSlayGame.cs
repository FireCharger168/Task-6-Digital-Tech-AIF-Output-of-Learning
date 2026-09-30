using System;
using System.Collections.Generic;
using System.Linq;
using HereToSlay.View;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace HereToSlay
{
    /// <summary>
    /// Entry point. Builds the camera, layered scenery, table and UI, then drives the rules engine:
    /// human decisions come from clicks, AI decisions from <see cref="AIBrain"/>.
    /// It boots itself in any scene, so pressing Play in SampleScene is enough.
    /// </summary>
    public sealed class HereToSlayGame : MonoBehaviour
    {
        private static readonly float[] Speeds = { 1f, 2f, 4f, 0.5f };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoBoot()
        {
            if (FindAnyObjectByType<HereToSlayGame>() == null)
            {
                new GameObject("Here To Slay").AddComponent<HereToSlayGame>();
            }
        }

        public static bool PointerOverUI()
        {
            return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        }

        [Tooltip("Seconds an AI waits before answering, at 1x speed.")]
        public float aiThinkTime = 0.55f;

        private Camera cam;
        private SceneryLayers scenery;
        private BoardView board;
        private GameUI ui;

        private GameEngine engine;
        private EngineRunner runner;
        private readonly System.Random aiRandom = new System.Random();

        private object waitingOn;
        private float pauseTimer;
        private float aiTimer;
        private int speedIndex;

        private PlayerState viewer;
        private bool handVisible = true;
        private bool gameOverShown;
        private readonly HashSet<CardInstance> selectable = new HashSet<CardInstance>();

        private float Speed => Speeds[speedIndex];

        private void Awake()
        {
            Application.targetFrameRate = 60;
            SetupCamera();

            scenery = new GameObject("Scenery (parallax layers)").AddComponent<SceneryLayers>();
            scenery.transform.SetParent(transform, false);
            scenery.Build(cam);

            board = new GameObject("Board").AddComponent<BoardView>();
            board.transform.SetParent(transform, false);
            board.Build(cam);

            ui = new GameObject("UI").AddComponent<GameUI>();
            ui.transform.SetParent(transform, false);
            ui.Build();
            ui.OnOptionClicked = OnOptionClicked;
            ui.OnSpeedClicked = CycleSpeed;
            ui.OnRestartClicked = () => ui.ShowMenu(StartGame);
            ui.ShowMenu(StartGame);
            ui.SetStatus("Choose who is playing, then press Start.");
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
            cam.orthographicSize = 5.4f;
            cam.transform.position = new Vector3(0f, 0f, -10f);
            cam.backgroundColor = new Color(0.07f, 0.06f, 0.15f);
            cam.clearFlags = CameraClearFlags.SolidColor;
        }

        private void StartGame(List<SeatConfig> seats)
        {
            engine = new GameEngine(seats);
            engine.OnLog += line => ui.AddLog(line);
            engine.OnRoll += roll => ui.ShowRoll(roll);
            engine.OnReveal += (who, cards, caption) =>
            {
                if (who == viewer && who.isHuman && handVisible)
                {
                    ui.ShowToast(caption);
                }
            };

            runner = new EngineRunner(engine.Run());
            waitingOn = null;
            gameOverShown = false;
            ui.ClearLog();
            ui.ClearPrompt();

            List<PlayerState> humans = engine.players.Where(p => p.isHuman).ToList();
            viewer = humans.Count > 0 ? humans[0] : engine.players[0];
            handVisible = humans.Count <= 1 || humans.Count == 0;
            if (humans.Count > 1)
            {
                ui.ShowPassScreen(viewer.name, () => handVisible = true);
            }
        }

        private void CycleSpeed()
        {
            speedIndex = (speedIndex + 1) % Speeds.Length;
            ui.SetSpeedLabel($"Speed {Speed}x");
        }

        private void Update()
        {
            if (engine == null)
            {
                board.SyncEmpty(ui.PanelWidthPixels);
                ui.RenderLabels(board.labels, cam);
                return;
            }

            AdvanceEngine();
            UpdateSelectable();
            board.Sync(engine, viewer, handVisible, selectable, ui.PanelWidthPixels);
            ui.RenderLabels(board.labels, cam);
            HandlePointer();
            UpdateStatus();

            if (engine.GameOver && !gameOverShown)
            {
                gameOverShown = true;
                runner = null;
                waitingOn = null;
                ui.ClearPrompt();
                ui.ShowGameOver($"{engine.winner.name} wins!\n<size=22>{WinReason(engine.winner)}</size>");
            }
        }

        private static string WinReason(PlayerState p)
        {
            return p.slainMonsters.Count >= HereToSlayCardDatabase.MonstersRequiredToWin
                ? "Three Monsters slain."
                : "A Party of all six classes.";
        }

        private void AdvanceEngine()
        {
            if (runner == null || ui.MenuOpen)
            {
                return;
            }

            // Several engine steps can run in one frame; stop as soon as we need to wait for something.
            for (int guard = 0; guard < 50; guard++)
            {
                if (waitingOn is Pause)
                {
                    pauseTimer -= Time.deltaTime * Speed;
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
                            aiTimer -= Time.deltaTime * Speed;
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
                    ui.ClearPrompt();
                }

                object next;
                try
                {
                    next = runner.Step();
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                    ui.AddLog("<color=#ff8080>Engine error: " + exception.Message + "</color>");
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
            aiTimer = request.kind == ChoiceKind.MainAction ? aiThinkTime * 1.4f : aiThinkTime;
            int humanCount = engine.players.Count(p => p.isHuman);

            if (request.chooser.isHuman)
            {
                if (request.chooser != viewer)
                {
                    viewer = request.chooser;
                    if (humanCount > 1)
                    {
                        handVisible = false;
                        ui.ShowPassScreen(viewer.name, () => handVisible = true);
                    }
                }

                ui.ShowRequest(request);
            }
            else
            {
                if (humanCount == 0)
                {
                    viewer = engine.Current;
                }

                ui.ShowWaiting($"{request.chooser.name} is thinking...");
            }
        }

        private ChoiceRequest HumanRequest()
        {
            if (waitingOn is ChoiceRequest request && !request.Resolved && request.chooser.isHuman && request.chooser == viewer && handVisible && !ui.PassScreenOpen)
            {
                return request;
            }

            return null;
        }

        private void UpdateSelectable()
        {
            selectable.Clear();
            ChoiceRequest request = HumanRequest();
            if (request == null)
            {
                return;
            }

            foreach (ChoiceOption option in request.options)
            {
                if (option.card != null)
                {
                    selectable.Add(option.card);
                }
            }
        }

        private void HandlePointer()
        {
            if (board.Hovered != null && board.Hovered.card != null)
            {
                ui.ShowPreview(board.Hovered.card.def);
            }

            Mouse mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame || PointerOverUI())
            {
                return;
            }

            ChoiceRequest request = HumanRequest();
            if (request == null || board.Hovered == null)
            {
                return;
            }

            CardInstance clicked = board.Hovered.card;
            int index = request.options.FindIndex(o => o.card == clicked);
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
            ui.ClearPrompt();
        }

        private void UpdateStatus()
        {
            PlayerState current = engine.Current;
            if (current == null)
            {
                return;
            }

            ui.SetStatus($"Turn {engine.turnNumber}: <b>{current.name}</b>{(current.isHuman ? "" : " (AI)")}\nAction points: <b>{current.actionPoints}</b> / {current.MaxActionPoints()}");
        }
    }
}
