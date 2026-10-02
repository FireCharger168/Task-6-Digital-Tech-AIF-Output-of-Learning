using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace HereToSlay.View
{
    /// <summary>Online screens: host or join menu, the host's lobby (join code, bots, start) and the joined player's lobby.</summary>
    public sealed partial class GameUI
    {
        private const string NamePref = "hts2.netName";
        private const string CodePref = "hts2.netCode";

        private GameObject onlineScreen;
        private GameObject hostLobbyScreen;
        private GameObject clientLobbyScreen;
        private InputField onlineName;
        private InputField joinCodeField;
        private Text onlineStatus;
        private Text hostCodeText;
        private Text hostAddressText;
        private Text hostPlayersText;
        private Text hostBotsText;
        private Text hostLeadersText;
        private Text hostStatus;
        private Button hostStartButton;
        private Text clientTitle;
        private Text clientPlayersText;
        private Text clientStatus;

        /// <summary>(player name)</summary>
        public Action<string> OnHostRequested;
        /// <summary>(player name, join code)</summary>
        public Action<string, string> OnJoinRequested;
        /// <summary>(bot count change, toggle leader mode)</summary>
        public Action<int, bool> OnHostOptions;
        public Action OnHostStart;
        public Action OnLeaveOnline;

        public bool OnlineMenuOpen => onlineScreen.activeSelf || hostLobbyScreen.activeSelf || clientLobbyScreen.activeSelf;

        private void BuildOnline()
        {
            // ---------------------------------------------------------- host or join
            onlineScreen = MakeScreen("Online", 0.7f);
            RectTransform box = Box(onlineScreen.transform, new Vector2(1000, 820), "Play Online");

            Text nameLabel = NewText("Name Label", box, "Your name", 24, Color.white, TextAnchor.MiddleLeft);
            Place(nameLabel.rectTransform, new Vector2(0.5f, 1f), new Vector2(-300, -125), new Vector2(240, 50));
            onlineName = NewInput(box, PlayerPrefs.GetString(NamePref, "Player"));
            Place(onlineName.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(120, -125), new Vector2(520, 54));

            Image hostPanel = NewPanel("Host Panel", box, new Color(0.4f, 0.8f, 0.5f, 0.1f));
            Place(hostPanel.rectTransform, new Vector2(0.5f, 1f), new Vector2(0, -255), new Vector2(880, 150));
            Text hostInfo = NewText("Host Info", hostPanel.transform,
                "<b>Host a game</b>\nThis computer runs the game. You get a join code to give your friends.", 21, Color.white, TextAnchor.MiddleLeft);
            Place(hostInfo.rectTransform, new Vector2(0f, 0.5f), new Vector2(290, 0), new Vector2(540, 120));
            Button host = NewButton("Host", hostPanel.transform, "Host Game", 26, AccentButton, () =>
            {
                SaveOnlineName();
                OnHostRequested?.Invoke(onlineName.text);
            });
            Place(host.GetComponent<RectTransform>(), new Vector2(1f, 0.5f), new Vector2(-150, 0), new Vector2(250, 70));

            Image joinPanel = NewPanel("Join Panel", box, new Color(0.5f, 0.6f, 1f, 0.1f));
            Place(joinPanel.rectTransform, new Vector2(0.5f, 1f), new Vector2(0, -425), new Vector2(880, 150));
            Text joinInfo = NewText("Join Info", joinPanel.transform, "<b>Join a game</b>  -  type the host's code", 21, Color.white, TextAnchor.MiddleLeft);
            Place(joinInfo.rectTransform, new Vector2(0f, 1f), new Vector2(290, -32), new Vector2(540, 40));
            joinCodeField = NewInput(joinPanel.transform, PlayerPrefs.GetString(CodePref, ""));
            joinCodeField.characterLimit = 40;
            joinCodeField.placeholder.GetComponent<Text>().text = "e.g. 60N00-H87K1";
            Place(joinCodeField.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(290, 48), new Vector2(540, 56));
            Button join = NewButton("Join", joinPanel.transform, "Join", 26, ButtonColor, () =>
            {
                SaveOnlineName();
                PlayerPrefs.SetString(CodePref, joinCodeField.text.Trim());
                PlayerPrefs.Save();
                OnJoinRequested?.Invoke(onlineName.text, joinCodeField.text);
            });
            Place(join.GetComponent<RectTransform>(), new Vector2(1f, 0.5f), new Vector2(-150, 0), new Vector2(250, 70));

            onlineStatus = NewText("Status", box, "", 22, new Color(1f, 0.75f, 0.5f));
            Place(onlineStatus.rectTransform, new Vector2(0.5f, 1f), new Vector2(0, -540), new Vector2(880, 50));

            Text help = NewText("Help", box,
                "Same Wi-Fi / home network: works straight away (allow the game through Windows Firewall when asked).\n" +
                "Over the internet: the host forwards TCP port 7777 on their router, or everyone joins a free VPN such as " +
                "Tailscale, ZeroTier or Radmin VPN and the host shares that address (type it as 100.x.y.z:7777).",
                17, new Color(0.8f, 0.8f, 0.9f));
            Place(help.rectTransform, new Vector2(0.5f, 0f), new Vector2(0, 160), new Vector2(900, 90));

            Button back = NewButton("Back", box, "Back", 26, QuietButton, () =>
            {
                onlineScreen.SetActive(false);
                titleScreen.SetActive(true);
            });
            Place(back.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0, 60), new Vector2(260, 64));

            // ---------------------------------------------------------- host lobby
            hostLobbyScreen = MakeScreen("Host Lobby", 0.7f);
            RectTransform hostBox = Box(hostLobbyScreen.transform, new Vector2(1000, 860), "Hosting a Game");

            Text codeLabel = NewText("Code Label", hostBox, "Join code", 22, new Color(0.85f, 0.85f, 0.95f));
            Place(codeLabel.rectTransform, new Vector2(0.5f, 1f), new Vector2(0, -105), new Vector2(600, 30));
            hostCodeText = NewText("Code", hostBox, "", 72, Gold, TextAnchor.MiddleCenter, true);
            hostCodeText.fontStyle = FontStyle.Bold;
            Place(hostCodeText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0, -170), new Vector2(900, 90));
            hostAddressText = NewText("Address", hostBox, "", 20, new Color(0.85f, 0.85f, 0.95f));
            Place(hostAddressText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0, -232), new Vector2(900, 30));

            Image playersPanel = NewPanel("Players", hostBox, new Color(1, 1, 1, 0.06f));
            Place(playersPanel.rectTransform, new Vector2(0.5f, 1f), new Vector2(0, -385), new Vector2(880, 250));
            hostPlayersText = NewText("Players Text", playersPanel.transform, "", 24, Color.white, TextAnchor.UpperLeft);
            Stretch(hostPlayersText.rectTransform, 18);

            Text botsLabel = NewText("Bots Label", hostBox, "AI bots", 24, Color.white, TextAnchor.MiddleLeft);
            Place(botsLabel.rectTransform, new Vector2(0.5f, 0f), new Vector2(-250, 290), new Vector2(360, 50));
            Button fewer = NewButton("Fewer", hostBox, "-", 30, ButtonColor, () => OnHostOptions?.Invoke(-1, false));
            Place(fewer.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0, 290), new Vector2(64, 54));
            hostBotsText = NewText("Bots", hostBox, "0", 30, Gold);
            Place(hostBotsText.rectTransform, new Vector2(0.5f, 0f), new Vector2(110, 290), new Vector2(120, 54));
            Button more = NewButton("More", hostBox, "+", 30, ButtonColor, () => OnHostOptions?.Invoke(1, false));
            Place(more.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(220, 290), new Vector2(64, 54));

            Text leadersLabel = NewText("Leaders Label", hostBox, "Party Leaders", 24, Color.white, TextAnchor.MiddleLeft);
            Place(leadersLabel.rectTransform, new Vector2(0.5f, 0f), new Vector2(-250, 220), new Vector2(360, 50));
            Button leaders = NewButton("Leaders", hostBox, "", 22, ButtonColor, () => OnHostOptions?.Invoke(0, true));
            Place(leaders.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(170, 220), new Vector2(480, 54));
            hostLeadersText = leaders.GetComponentInChildren<Text>();

            hostStatus = NewText("Status", hostBox, "", 20, new Color(1f, 0.75f, 0.5f));
            Place(hostStatus.rectTransform, new Vector2(0.5f, 0f), new Vector2(0, 150), new Vector2(900, 40));

            Button closeLobby = NewButton("Close", hostBox, "Close Lobby", 26, QuietButton, () => OnLeaveOnline?.Invoke());
            Place(closeLobby.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(-200, 60), new Vector2(260, 64));
            hostStartButton = NewButton("Start", hostBox, "Start Game", 26, AccentButton, () => OnHostStart?.Invoke());
            Place(hostStartButton.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(170, 60), new Vector2(360, 64));

            // ---------------------------------------------------------- joined player's lobby
            clientLobbyScreen = MakeScreen("Client Lobby", 0.7f);
            RectTransform clientBox = Box(clientLobbyScreen.transform, new Vector2(900, 640), "Joined");
            clientTitle = NewText("Title", clientBox, "", 26, Color.white);
            Place(clientTitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0, -110), new Vector2(820, 40));
            Image clientPlayers = NewPanel("Players", clientBox, new Color(1, 1, 1, 0.06f));
            Place(clientPlayers.rectTransform, new Vector2(0.5f, 1f), new Vector2(0, -280), new Vector2(780, 250));
            clientPlayersText = NewText("Players Text", clientPlayers.transform, "", 24, Color.white, TextAnchor.UpperLeft);
            Stretch(clientPlayersText.rectTransform, 18);
            clientStatus = NewText("Status", clientBox, "", 22, new Color(1f, 0.85f, 0.55f));
            Place(clientStatus.rectTransform, new Vector2(0.5f, 0f), new Vector2(0, 150), new Vector2(820, 60));
            Button leave = NewButton("Leave", clientBox, "Leave", 26, QuietButton, () => OnLeaveOnline?.Invoke());
            Place(leave.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0, 60), new Vector2(260, 64));
        }

        private void SaveOnlineName()
        {
            PlayerPrefs.SetString(NamePref, string.IsNullOrWhiteSpace(onlineName.text) ? "Player" : onlineName.text.Trim());
            PlayerPrefs.Save();
        }

        private void HideOnlineScreens()
        {
            if (onlineScreen == null)
            {
                return;
            }

            onlineScreen.SetActive(false);
            hostLobbyScreen.SetActive(false);
            clientLobbyScreen.SetActive(false);
        }

        public void ShowOnlineMenu(string status)
        {
            HideAllScreens();
            hud.SetActive(false);
            onlineStatus.text = status;
            onlineScreen.SetActive(true);
        }

        public void SetOnlineStatus(string status)
        {
            onlineStatus.text = status;
        }

        public void ShowHostLobby(string code, string address, IList<string> names, int bots, bool randomLeaders, bool canStart, string status)
        {
            if (!hostLobbyScreen.activeSelf)
            {
                HideAllScreens();
                hud.SetActive(false);
                hostLobbyScreen.SetActive(true);
            }

            hostCodeText.text = code;
            hostAddressText.text = $"Same network: {address}   ·   Tell your friends: Play Online > Join";
            hostPlayersText.text = PlayerList(names, bots, 0);
            hostBotsText.text = bots.ToString();
            hostLeadersText.text = randomLeaders ? "Randomly assigned" : "Players pick in turn (draft)";
            hostStartButton.interactable = canStart;
            hostStatus.text = status;
        }

        public void ShowClientLobby(string hostName, IList<string> names, int bots, bool randomLeaders, string status)
        {
            if (!clientLobbyScreen.activeSelf)
            {
                HideAllScreens();
                hud.SetActive(false);
                clientLobbyScreen.SetActive(true);
            }

            clientTitle.text = string.IsNullOrEmpty(hostName) ? "Connecting..." : $"You are in <color=#ffd166>{hostName}</color>'s game";
            clientPlayersText.text = names.Count == 0 ? "" : PlayerList(names, bots, -1) + $"\n<size=20><color=#bbbbbb>Party Leaders: {(randomLeaders ? "random" : "draft")}</color></size>";
            clientStatus.text = status;
        }

        private static string PlayerList(IList<string> names, int bots, int hostIndex)
        {
            List<string> lines = new List<string>();
            for (int i = 0; i < names.Count; i++)
            {
                lines.Add($"{i + 1}.  {names[i]}{(i == 0 ? "  <color=#ffd166>(host)</color>" : "")}");
            }

            for (int i = 0; i < bots; i++)
            {
                lines.Add($"{names.Count + i + 1}.  <color=#9fb4ff>AI bot</color>");
            }

            lines.Add($"<size=20><color=#bbbbbb>{names.Count + bots}/6 players</color></size>");
            return string.Join("\n", lines);
        }
    }
}
