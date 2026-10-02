using System;
using System.Collections.Generic;
using HereToSlay.Net;
using HereToSlay.View;
using UnityEngine;

namespace HereToSlay
{
    /// <summary>
    /// Online play. The host's computer runs the real engine (<see cref="HostSession"/>); everyone else runs a
    /// <see cref="ClientSession"/> whose mirror engine is drawn by the same board and UI as an offline game.
    /// Bots and local hot-seat games are untouched by any of this.
    /// </summary>
    public sealed partial class HereToSlayGame
    {
        private HostSession hostSession;
        private ClientSession clientSession;
        private bool joinPending;
        private string hostNotice = "";
        private int lastActiveShown = -2;
        private string relayNote = "";

        private bool IsClient => clientSession != null && engine != null && engine == clientSession.Mirror;

        // ------------------------------------------------------------------ hosting

        private void HostRequested(string playerName)
        {
            CloseOnline();
            HostSession session = new HostSession { TimeLimit = TimeLimit };
            try
            {
                session.Open(playerName);
            }
            catch (Exception e)
            {
                ui.SetOnlineStatus("Could not host: " + e.Message);
                return;
            }

            hostSession = session;
            hostNotice = "Waiting for players to join... (or add AI bots and start)";
            relayNote = "Getting an internet join code...";
            StartRelayHost(session);
            session.LobbyChanged += RefreshHostLobby;
            session.Notice += message =>
            {
                hostNotice = message;
                if (engine != null && ui.InGame)
                {
                    ui.ShowToast(message, 2.5f);
                }
                else
                {
                    RefreshHostLobby();
                }
            };
            RefreshHostLobby();
        }

        /// <summary>Asks Unity Relay for a 6-character code that works from any network; the LAN code keeps working regardless.</summary>
        private async void StartRelayHost(HostSession session)
        {
            try
            {
                Online.RelayHostTransport relay = await Online.RelayHostTransport.CreateAsync(HostSession.MaxPlayers - 1);
                if (hostSession != session)
                {
                    relay.Stop();
                    return;
                }

                session.AddTransport(relay);
                session.OnlineCode = relay.JoinCode;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Here To Slay] Relay unavailable: " + e);
                if (hostSession != session)
                {
                    return;
                }

                relayNote = "No internet code: " + Online.RelayNet.Explain(e);
            }

            RefreshHostLobby();
        }

        private void RefreshHostLobby()
        {
            if (hostSession == null || hostSession.InGame)
            {
                return;
            }

            ui.ShowHostLobby(hostSession.OnlineCode, relayNote, hostSession.Code, hostSession.Address, hostSession.PlayerNames(), hostSession.AiCount,
                hostSession.RandomLeaders, hostSession.CanStart, hostSession.CanStart ? hostNotice : "Add an AI bot or wait for a friend to join (2-6 players).");
        }

        private void HostOptions(int botDelta, bool toggleLeaders)
        {
            if (hostSession == null || hostSession.InGame)
            {
                return;
            }

            hostSession.SetOptions(hostSession.AiCount + botDelta, toggleLeaders ? !hostSession.RandomLeaders : hostSession.RandomLeaders);
            RefreshHostLobby();
        }

        private void HostStart()
        {
            if (hostSession == null || hostSession.InGame || !hostSession.CanStart)
            {
                return;
            }

            GameConfig config = new GameConfig { randomLeaders = hostSession.RandomLeaders, randomFirstPlayer = true };
            config.seats.AddRange(hostSession.BuildSeats());
            StartGame(config);
        }

        // ------------------------------------------------------------------ joining

        private void JoinRequested(string playerName, string code)
        {
            string relayCode = Online.RelayNet.NormalizeCode(code);
            System.Net.IPEndPoint endPoint = null;
            if (relayCode == null && !JoinCode.TryDecode(code, out endPoint))
            {
                ui.SetOnlineStatus("That code doesn't look right. Internet codes have 6 characters (like K7Q2XM); same-Wi-Fi codes have 10.");
                return;
            }

            CloseOnline();
            ClientSession session = new ClientSession();
            session.LobbyChanged += RefreshClientLobby;
            session.GameStarted += StartClientGame;
            session.LogReceived += line => ui.AddLog(line);
            session.RollReceived += roll => ui.ShowRoll(roll);
            session.RevealReceived += (cards, caption) => ui.ShowToast(caption);
            session.Disconnected += ClientDisconnected;
            clientSession = session;
            joinPending = true;
            if (relayCode != null)
            {
                ui.SetOnlineStatus($"Joining game {relayCode} over the internet...");
                Online.RelayClientLink link = new Online.RelayClientLink();
                link.Connect(relayCode);
                session.Begin(link, playerName);
            }
            else
            {
                ui.SetOnlineStatus($"Connecting to {endPoint}...");
                session.ConnectInBackground(endPoint, playerName);
            }
        }

        private void RefreshClientLobby()
        {
            if (clientSession == null || IsClient)
            {
                return;
            }

            ui.ShowClientLobby(clientSession.HostName, clientSession.LobbyNames, clientSession.LobbyAiCount, clientSession.LobbyRandomLeaders,
                "Waiting for the host to start the game...");
        }

        private void StartClientGame(GameEngine mirror)
        {
            engine = mirror;
            runner = null;
            waitingOn = null;
            requestNeedsShowing = false;
            gameOverShown = false;
            pressedCard = null;
            dragging = false;
            timedRequest = null;
            autoDrawPlayer = null;
            lastActiveShown = -2;
            PlayerState me = clientSession.Me;
            humans = new List<PlayerState> { me };
            viewer = me;
            viewerConfirmed = true;
            board.SetVisible(true);
            board.SyncEmpty();
            ui.HideTooltip();
            ui.ShowHud();
        }

        private void ClientDisconnected(string reason)
        {
            bool wasPlaying = IsClient;
            CloseOnline();
            if (wasPlaying)
            {
                engine = null;
                runner = null;
                waitingOn = null;
                board.SyncEmpty();
                ui.HideTooltip();
            }

            ui.ShowOnlineMenu(reason);
        }

        /// <summary>Client side of the engine loop: show the host's questions and send back the answers.</summary>
        private void ClientAdvance()
        {
            if (waitingOn is ChoiceRequest answered && answered.Resolved)
            {
                clientSession.SendAnswerIfReady();
                waitingOn = null;
                ui.ClearRequest();
            }

            ChoiceRequest pending = clientSession.Pending;
            if (!ReferenceEquals(waitingOn, pending))
            {
                if (waitingOn != null)
                {
                    ui.ClearRequest();
                }

                waitingOn = pending;
                if (pending != null)
                {
                    lastActiveShown = -2;
                    OnNewRequest(pending);
                }
            }

            if (pending == null)
            {
                int active = clientSession.ActiveChooser;
                if (active != lastActiveShown)
                {
                    lastActiveShown = active;
                    if (active >= 0 && active != clientSession.MySeat && active < engine.players.Count)
                    {
                        PlayerState who = engine.players[active];
                        ui.ShowWaiting(who.isHuman ? $"{who.name} is choosing..." : $"{who.name} is thinking...");
                    }
                    else
                    {
                        ui.ClearRequest();
                    }
                }
            }
        }

        // ------------------------------------------------------------------ shared

        private void PollNetwork()
        {
            if (clientSession != null)
            {
                ClientSession session = clientSession;
                if (joinPending)
                {
                    if (session.Status == ClientStatus.Failed)
                    {
                        joinPending = false;
                        clientSession = null;
                        ui.SetOnlineStatus("Could not connect: " + session.Error + "\nCheck the code and that the host is still in their lobby.");
                        return;
                    }

                    if (session.Status == ClientStatus.Connected)
                    {
                        joinPending = false;
                        RefreshClientLobby();
                    }
                }

                session.Poll();
            }

            if (hostSession != null && !hostSession.InGame)
            {
                hostSession.Poll(null);
            }
        }

        /// <summary>After a game: the host returns everyone to the lobby; a client waits there for the next game.</summary>
        private void BackToLobby()
        {
            engine = null;
            runner = null;
            waitingOn = null;
            board.SyncEmpty();
            ui.HideTooltip();
            if (hostSession != null)
            {
                hostSession.EndGame();
                hostNotice = "Ready for another round!";
                RefreshHostLobby();
            }
            else if (clientSession != null)
            {
                RefreshClientLobby();
            }
        }

        private void CloseOnline()
        {
            hostSession?.Stop();
            hostSession = null;
            clientSession?.Stop();
            clientSession = null;
            joinPending = false;
        }

        private void OnApplicationQuit()
        {
            CloseOnline();
        }
    }
}
