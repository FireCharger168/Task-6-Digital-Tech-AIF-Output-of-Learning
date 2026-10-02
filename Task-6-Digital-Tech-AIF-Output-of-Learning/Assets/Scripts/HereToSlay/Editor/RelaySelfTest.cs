using System;
using System.IO;
using System.Text;
using HereToSlay.Net;
using HereToSlay.Online;
using UnityEditor;
using UnityEngine;

namespace HereToSlay.EditorTools
{
    /// <summary>
    /// End-to-end check of internet play without opening the game: hosts a game through Unity Relay, joins it
    /// through Relay from a second (in-editor) player, and plays real turns (bots decide for both).
    /// Run it with the menu Here To Slay > Test Internet Play, or by creating Builds/relaytest.request.
    /// The result is written to Builds/relay_test.txt.
    /// </summary>
    [InitializeOnLoad]
    public static class RelaySelfTest
    {
        private const string RequestFile = "Builds/relaytest.request";
        private const string ResultFile = "Builds/relay_test.txt";
        /// <summary>Unity Services only run in Play Mode, so the test enters Play Mode and continues after the reload.</summary>
        private const string PendingFile = "Builds/relaytest.pending";
        private static double nextPoll;
        private static Runner running;

        static RelaySelfTest()
        {
            EditorApplication.update += Update;
        }

        [MenuItem("Here To Slay/Test Internet Play (Relay)")]
        public static void RunFromMenu()
        {
            if (running != null)
            {
                return;
            }

            Directory.CreateDirectory("Builds");
            if (EditorApplication.isPlaying)
            {
                running = new Runner(false);
                running.Start();
                return;
            }

            File.WriteAllText(PendingFile, "exit");
            EditorApplication.EnterPlaymode();
        }

        private static void Update()
        {
            if (running != null)
            {
                if (!running.Tick())
                {
                    running = null;
                }

                return;
            }

            if (EditorApplication.timeSinceStartup < nextPoll)
            {
                return;
            }

            nextPoll = EditorApplication.timeSinceStartup + 1.0;
            if (EditorApplication.isCompiling)
            {
                return;
            }

            if (File.Exists(PendingFile) && EditorApplication.isPlaying)
            {
                bool exitAfter = File.ReadAllText(PendingFile).Contains("exit");
                File.Delete(PendingFile);
                running = new Runner(exitAfter);
                running.Start();
                return;
            }

            if (File.Exists(RequestFile) && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                File.Delete(RequestFile);
                RunFromMenu();
            }
        }

        private sealed class Runner
        {
            private const double TimeoutSeconds = 240;
            private const int AnswersForSuccess = 40;

            private readonly StringBuilder log = new StringBuilder();
            private readonly System.Random rng = new System.Random(7);
            private double started;
            private bool connecting = true;
            private bool finished;
            private HostSession host;
            private ClientSession client;
            private GameEngine engine;
            private EngineRunner runner;
            private object waiting;
            private int answers;
            private int largestSnapshot;
            private readonly bool exitPlayModeWhenDone;

            public Runner(bool exitPlayModeWhenDone)
            {
                this.exitPlayModeWhenDone = exitPlayModeWhenDone;
            }

            public async void Start()
            {
                started = EditorApplication.timeSinceStartup;
                Write("Relay self-test started.");
                try
                {
                    Write("Signing in and creating a Relay allocation...");
                    RelayHostTransport relay = await RelayHostTransport.CreateAsync(5);
                    Write("Got internet join code " + relay.JoinCode);

                    host = new HostSession();
                    host.Open("EditorHost", 7790);
                    host.AddTransport(relay);

                    client = new ClientSession();
                    RelayClientLink link = new RelayClientLink();
                    link.Connect(relay.JoinCode);
                    client.Begin(link, "RelayTester");
                    connecting = false;
                }
                catch (Exception e)
                {
                    Finish("FAILED while setting up Relay: " + RelayNet.Explain(e) + "\n" + e);
                }
            }

            /// <summary>Returns false once the test is over.</summary>
            public bool Tick()
            {
                if (finished)
                {
                    return false;
                }

                if (EditorApplication.timeSinceStartup - started > TimeoutSeconds)
                {
                    Finish($"FAILED: timed out (joined: {host?.HumanCount == 2}, answers over Relay: {answers}).");
                    return false;
                }

                if (connecting || host == null)
                {
                    return true;
                }

                host.Poll(waiting as ChoiceRequest);
                client.Poll();

                if (client.Status == ClientStatus.Failed)
                {
                    Finish("FAILED: the joining player could not connect: " + client.Error);
                    return false;
                }

                if (engine == null)
                {
                    if (host.HumanCount == 2)
                    {
                        Write($"Player joined through Relay after {EditorApplication.timeSinceStartup - started:0.0}s. Starting a game with 1 bot...");
                        host.SetOptions(1, true);
                        engine = new GameEngine(host.BuildSeats(), 11, true, true) { usePauses = false };
                        host.BeginGame(engine);
                        runner = new EngineRunner(engine.Run());
                    }

                    return true;
                }

                // Host side: run the engine until a remote player has to answer.
                for (int guard = 0; guard < 500; guard++)
                {
                    if (waiting == null || waiting is ChoiceRequest done && done.Resolved)
                    {
                        waiting = runner.Step();
                        if (waiting == null)
                        {
                            break;
                        }
                    }

                    if (waiting is Pause)
                    {
                        runner.ClearPause();
                        waiting = null;
                        continue;
                    }

                    ChoiceRequest request = (ChoiceRequest)waiting;
                    if (request.chooser.isRemote)
                    {
                        break;
                    }

                    request.Select(AIBrain.Choose(request, rng));
                }

                host.Poll(waiting as ChoiceRequest);
                largestSnapshot = Math.Max(largestSnapshot, NetProtocol.State(engine, 1, -1, ChoiceKind.Info).Length);

                // Joining player: answer with the AI, as a person would through the UI.
                if (client.Pending != null && !client.Pending.Resolved)
                {
                    PlayerState me = client.Me;
                    if (me.hand.Count != engine.players[1].hand.Count)
                    {
                        Finish($"FAILED: the joined player's hand ({me.hand.Count}) does not match the host's ({engine.players[1].hand.Count}).");
                        return false;
                    }

                    client.Pending.Select(AIBrain.Choose(client.Pending, rng));
                    client.SendAnswerIfReady();
                    answers++;
                }

                bool mirroredWin = engine.GameOver && client.Mirror != null && client.Mirror.winner != null;
                if (mirroredWin || answers >= AnswersForSuccess && !engine.GameOver)
                {
                    Finish($"PASSED: {answers} decisions sent over Relay, turn {engine.turnNumber}" +
                           (engine.GameOver ? $", game over ({engine.winner.name} won) and the result reached the joined player" : "") +
                           $". Largest table snapshot {largestSnapshot} bytes. Took {EditorApplication.timeSinceStartup - started:0.0}s.");
                    return false;
                }

                return true;
            }

            private void Write(string line)
            {
                log.AppendLine(DateTime.Now.ToString("HH:mm:ss") + " " + line);
                try
                {
                    Directory.CreateDirectory("Builds");
                    File.WriteAllText(ResultFile, log.ToString());
                }
                catch (Exception)
                {
                    // best effort
                }
            }

            private void Finish(string line)
            {
                if (finished)
                {
                    return;
                }

                finished = true;
                Write(line);
                Debug.Log("[Here To Slay] " + line);
                try
                {
                    client?.Stop();
                    host?.Stop();
                }
                catch (Exception e)
                {
                    Write("Cleanup error: " + e.Message);
                }

                if (exitPlayModeWhenDone && EditorApplication.isPlaying)
                {
                    EditorApplication.ExitPlaymode();
                }
            }
        }
    }
}
