using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;

namespace HereToSlay.Net
{
    /// <summary>What happens when nobody answers in time (shared by local timers and the host's safety net).</summary>
    public static class TimeoutRules
    {
        public static int Choose(ChoiceRequest request, Random rng)
        {
            switch (request.kind)
            {
                case ChoiceKind.MainAction:
                {
                    int draw = request.options.FindIndex(o => o.action == "draw");
                    return draw >= 0 ? draw : Math.Max(0, request.options.FindIndex(o => o.action == "end"));
                }

                case ChoiceKind.Challenge:
                case ChoiceKind.Modifier:
                {
                    int pass = request.options.FindIndex(o => o.card == null);
                    return pass >= 0 ? pass : request.options.Count - 1;
                }

                default:
                    return AIBrain.Choose(request, rng);
            }
        }
    }

    /// <summary>
    /// Host side of an online game. The host runs the real <see cref="GameEngine"/>; remote players get a filtered
    /// copy of the table (only their own hand face-up) and answer the questions the engine asks them.
    /// If a remote player leaves, an AI takes over their seat.
    /// </summary>
    public sealed class HostSession
    {
        public const int MaxPlayers = 6;
        private const float SnapshotInterval = 0.1f;
        private const float SafetyGraceSeconds = 8f;

        private readonly NetHost net = new NetHost();
        private readonly List<NetConnection> lobby = new List<NetConnection>();
        private readonly Dictionary<int, NetConnection> seatConnections = new Dictionary<int, NetConnection>();
        private readonly Dictionary<NetConnection, byte[]> lastState = new Dictionary<NetConnection, byte[]>();
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly Random rng = new Random();

        private ChoiceRequest forwarded;
        private int forwardedId;
        private NetConnection forwardedTo;
        private double forwardedAt;
        private float forwardedLimit;
        private int nextRequestId;
        private double lastSnapshotAt = -1;
        private ChoiceRequest lastActive;

        public string HostName { get; private set; } = "Host";
        public string Code { get; private set; } = "";
        public string Address { get; private set; } = "";
        /// <summary>Internet code from Unity Relay (empty until it is ready, or if Relay is unavailable).</summary>
        public string OnlineCode { get; set; } = "";
        public int Port => net.Port;
        public int AiCount { get; private set; }
        public bool RandomLeaders { get; private set; } = true;
        public GameEngine Engine { get; private set; }
        public bool InGame => Engine != null;

        /// <summary>Seconds a remote player gets for a question (0 = no limit). Sent to the client so its clock matches.</summary>
        public Func<ChoiceKind, float> TimeLimit = kind => 0f;

        public event Action LobbyChanged;
        public event Action<string> Notice;

        public void Open(string hostName, int port = JoinCode.DefaultPort)
        {
            HostName = string.IsNullOrWhiteSpace(hostName) ? "Host" : hostName.Trim();
            net.Start(port);
            IPAddress address = JoinCode.LocalAddress();
            Address = address + ":" + net.Port;
            Code = JoinCode.Encode(address, net.Port);
        }

        /// <summary>Lets players reach this game another way too (e.g. Unity Relay over the internet).</summary>
        public void AddTransport(HostTransport transport)
        {
            net.AddTransport(transport);
        }

        public List<string> PlayerNames()
        {
            List<string> names = new List<string> { HostName };
            names.AddRange(lobby.Select(c => c.name));
            return names;
        }

        public int HumanCount => 1 + lobby.Count;
        public int TotalPlayers => HumanCount + AiCount;
        public bool CanStart => TotalPlayers >= 2 && TotalPlayers <= MaxPlayers;

        public void SetOptions(int aiCount, bool randomLeaders)
        {
            AiCount = Math.Max(0, Math.Min(aiCount, MaxPlayers - HumanCount));
            RandomLeaders = randomLeaders;
            BroadcastLobby();
        }

        /// <summary>Host first, then the joined players in join order, then bots.</summary>
        public List<SeatConfig> BuildSeats()
        {
            List<SeatConfig> seats = new List<SeatConfig> { new SeatConfig(HostName, true) };
            foreach (NetConnection connection in lobby)
            {
                seats.Add(new SeatConfig(connection.name, true));
            }

            string[] botNames = { "Bot Ada", "Bot Bram", "Bot Cora", "Bot Dex", "Bot Eve" };
            for (int i = 0; i < AiCount; i++)
            {
                seats.Add(new SeatConfig(botNames[i % botNames.Length], false));
            }

            return seats;
        }

        /// <summary>Call with an engine built from <see cref="BuildSeats"/> before running it.</summary>
        public void BeginGame(GameEngine engine)
        {
            Engine = engine;
            seatConnections.Clear();
            lastState.Clear();
            forwarded = null;
            lastActive = null;
            List<SeatConfig> seats = engine.players.Select(p => new SeatConfig(p.name, p.isHuman)).ToList();
            for (int i = 0; i < lobby.Count; i++)
            {
                int seat = i + 1;
                NetConnection connection = lobby[i];
                connection.seatIndex = seat;
                seatConnections[seat] = connection;
                engine.players[seat].isRemote = true;
                connection.Send(NetProtocol.Start(seats, seat, engine.randomLeaders));
            }

            engine.OnLog += line => Broadcast(NetProtocol.Log(line));
            engine.OnRoll += roll =>
            {
                SendSnapshots(true);
                Broadcast(NetProtocol.Roll(roll));
            };
            engine.OnReveal += (who, cards, caption) =>
            {
                if (who != null && seatConnections.TryGetValue(who.index, out NetConnection connection))
                {
                    SendSnapshot(connection, true);
                    connection.Send(NetProtocol.Reveal(cards, caption));
                }
            };
        }

        /// <summary>Game over or abandoned: keep everyone connected and go back to the lobby.</summary>
        public void EndGame()
        {
            if (Engine != null)
            {
                SendSnapshots(true);
            }

            Engine = null;
            forwarded = null;
            seatConnections.Clear();
            // Players who left during the game are no longer in the lobby.
            lobby.RemoveAll(c => !c.Connected);
            AiCount = Math.Min(AiCount, MaxPlayers - HumanCount);
            BroadcastLobby();
        }

        public void Stop()
        {
            Broadcast(NetProtocol.Kick("The host closed the game."), true);
            net.Stop();
            lobby.Clear();
            seatConnections.Clear();
            Engine = null;
        }

        private double Now => clock.Elapsed.TotalSeconds;

        /// <summary>Call every frame with the request the engine is currently waiting on (or null).</summary>
        public void Poll(ChoiceRequest current)
        {
            net.Poll(Handle);
            if (Engine == null)
            {
                return;
            }

            if (current != null && current.Resolved)
            {
                current = null;
            }

            // The forwarded question was answered (or replaced): tell its player to close it.
            if (forwarded != null && (forwarded.Resolved || forwarded != current))
            {
                forwardedTo?.Send(NetProtocol.ClearRequest(forwardedId));
                forwarded = null;
                forwardedTo = null;
            }

            bool activeChanged = current != lastActive;
            lastActive = current;
            if (activeChanged)
            {
                SendSnapshots(true);
            }

            if (current != null && current.chooser.isRemote && seatConnections.TryGetValue(current.chooser.index, out NetConnection target))
            {
                if (forwarded == null)
                {
                    forwarded = current;
                    forwardedTo = target;
                    forwardedId = ++nextRequestId;
                    forwardedAt = Now;
                    forwardedLimit = TimeLimit(current.kind);
                    target.Send(NetProtocol.Request(Engine, current, forwardedId, forwardedLimit));
                }
                else if (forwardedLimit > 0f && Now - forwardedAt > forwardedLimit + SafetyGraceSeconds)
                {
                    // The player's own clock should have answered long ago (lag or a stuck client): decide for them.
                    current.Select(TimeoutRules.Choose(current, rng));
                }
            }

            if (Now - lastSnapshotAt >= SnapshotInterval)
            {
                SendSnapshots(false);
            }
        }

        private void SendSnapshots(bool force)
        {
            if (Engine == null)
            {
                return;
            }

            lastSnapshotAt = Now;
            foreach (NetConnection connection in seatConnections.Values.ToList())
            {
                SendSnapshot(connection, force);
            }
        }

        private void SendSnapshot(NetConnection connection, bool force)
        {
            if (Engine == null || !connection.Connected)
            {
                return;
            }

            ChoiceRequest active = lastActive;
            byte[] state = NetProtocol.State(Engine, connection.seatIndex, active != null ? active.chooser.index : -1,
                active != null ? active.kind : ChoiceKind.Info);
            if (!force && lastState.TryGetValue(connection, out byte[] previous) && previous.AsSpan().SequenceEqual(state))
            {
                return;
            }

            lastState[connection] = state;
            connection.Send(state);
        }

        private void Broadcast(byte[] payload, bool includeLobby = false)
        {
            IEnumerable<NetConnection> targets = includeLobby || Engine == null ? lobby.ToList() : seatConnections.Values.ToList();
            foreach (NetConnection connection in targets)
            {
                connection.Send(payload);
            }
        }

        private void BroadcastLobby()
        {
            byte[] message = NetProtocol.Lobby(PlayerNames(), AiCount, RandomLeaders);
            foreach (NetConnection connection in lobby)
            {
                connection.Send(message);
            }

            LobbyChanged?.Invoke();
        }

        private void Handle(NetInbound inbound)
        {
            NetConnection connection = inbound.connection;
            if (inbound.payload == null)
            {
                Disconnected(connection);
                return;
            }

            try
            {
                using (BinaryReader reader = NetProtocol.Open(inbound.payload, out MsgType type))
                {
                    switch (type)
                    {
                        case MsgType.Hello:
                            HandleHello(connection, reader.ReadInt32(), reader.ReadString());
                            break;
                        case MsgType.Answer:
                            HandleAnswer(connection, reader.ReadInt32(), reader.ReadInt32());
                            break;
                    }
                }
            }
            catch (Exception)
            {
                connection.Close();
            }
        }

        private void HandleHello(NetConnection connection, int version, string name)
        {
            if (lobby.Contains(connection))
            {
                return;
            }

            string refusal = version != NetProtocol.Version ? "This game is a different version of Here to Slay."
                : Engine != null ? "A game is already in progress. Try again when it ends."
                : HumanCount >= MaxPlayers ? "The game is full."
                : null;
            if (refusal != null)
            {
                connection.Send(NetProtocol.Kick(refusal));
                net.Remove(connection);
                return;
            }

            name = string.IsNullOrWhiteSpace(name) ? "Player" : name.Trim();
            if (name.Length > 16)
            {
                name = name.Substring(0, 16);
            }

            string unique = name;
            for (int n = 2; PlayerNames().Contains(unique); n++)
            {
                unique = name + " " + n;
            }

            connection.name = unique;
            lobby.Add(connection);
            AiCount = Math.Min(AiCount, MaxPlayers - HumanCount);
            connection.Send(NetProtocol.Welcome(HostName));
            Notice?.Invoke($"{unique} joined.");
            BroadcastLobby();
        }

        private void HandleAnswer(NetConnection connection, int requestId, int index)
        {
            if (forwarded == null || forwardedTo != connection || requestId != forwardedId || forwarded.Resolved)
            {
                return;
            }

            if (index >= 0 && index < forwarded.options.Count)
            {
                forwarded.Select(index);
            }
        }

        private void Disconnected(NetConnection connection)
        {
            net.Remove(connection);
            lastState.Remove(connection);
            if (Engine != null && connection.seatIndex >= 0 && seatConnections.TryGetValue(connection.seatIndex, out NetConnection seated) && seated == connection)
            {
                seatConnections.Remove(connection.seatIndex);
                PlayerState player = Engine.players[connection.seatIndex];
                player.isRemote = false;
                player.isHuman = false;
                player.name += " (bot)";
                if (forwardedTo == connection)
                {
                    forwarded = null;
                    forwardedTo = null;
                }

                Engine.Log($"{connection.name} left the game. A bot takes over their seat.");
                Notice?.Invoke($"{connection.name} left - a bot takes over.");
                return;
            }

            if (lobby.Remove(connection))
            {
                Notice?.Invoke($"{connection.name} left.");
                BroadcastLobby();
            }
        }
    }

    public enum ClientStatus
    {
        Idle,
        Connecting,
        Connected,
        Failed,
        Closed
    }

    /// <summary>
    /// Client side of an online game: keeps a mirror <see cref="GameEngine"/> (never run, only filled from the host's
    /// snapshots) so the normal board and UI can draw it, and turns the host's questions into local ChoiceRequests.
    /// </summary>
    public sealed class ClientSession
    {
        private ClientLink link;
        private bool helloSent;
        private bool closed;
        private readonly Dictionary<int, CardInstance> cards = new Dictionary<int, CardInstance>();
        private readonly List<CardInstance> hiddenPool = new List<CardInstance>();
        private static CardDefinition hiddenDefinition;
        private int pendingId;
        private string kickReason = "";

        public ClientStatus Status
        {
            get
            {
                if (closed)
                {
                    return ClientStatus.Closed;
                }

                if (link == null)
                {
                    return ClientStatus.Idle;
                }

                switch (link.State)
                {
                    case LinkState.Connected:
                        return helloSent ? ClientStatus.Connected : ClientStatus.Connecting;
                    case LinkState.Failed:
                        return ClientStatus.Failed;
                    case LinkState.Closed:
                        return ClientStatus.Closed;
                    default:
                        return ClientStatus.Connecting;
                }
            }
        }

        public string Error => !string.IsNullOrEmpty(kickReason) ? kickReason : link?.Error ?? "";
        public string PlayerName { get; private set; } = "";
        public string HostName { get; private set; } = "";
        public List<string> LobbyNames { get; } = new List<string>();
        public int LobbyAiCount { get; private set; }
        public bool LobbyRandomLeaders { get; private set; }

        public GameEngine Mirror { get; private set; }
        public int MySeat { get; private set; } = -1;
        public PlayerState Me => Mirror != null && MySeat >= 0 && MySeat < Mirror.players.Count ? Mirror.players[MySeat] : null;
        public ChoiceRequest Pending { get; private set; }
        public int ActiveChooser { get; private set; } = -1;
        public ChoiceKind ActiveKind { get; private set; }
        /// <summary>Counts snapshots received (lets the UI notice a fresh table).</summary>
        public int StateVersion { get; private set; }

        public event Action LobbyChanged;
        public event Action<GameEngine> GameStarted;
        public event Action<string> LogReceived;
        public event Action<RollContext> RollReceived;
        public event Action<List<CardInstance>, string> RevealReceived;
        public event Action<string> Disconnected;

        private static CardDefinition Hidden => hiddenDefinition ?? (hiddenDefinition = HereToSlayCardDatabase.GetCard(NetProtocol.HiddenCardId));

        /// <summary>Direct connection (same network / VPN / forwarded port). Watch <see cref="Status"/>.</summary>
        public void ConnectInBackground(IPEndPoint endPoint, string playerName)
        {
            TcpClientLink tcp = new TcpClientLink();
            tcp.Connect(endPoint);
            Begin(tcp, playerName);
        }

        /// <summary>Uses any link (e.g. Relay); the hello is sent as soon as the link is up.</summary>
        public void Begin(ClientLink clientLink, string playerName)
        {
            PlayerName = string.IsNullOrWhiteSpace(playerName) ? "Player" : playerName.Trim();
            link = clientLink;
            helloSent = false;
            closed = false;
        }

        public void Stop()
        {
            closed = true;
            link?.Stop();
        }

        public void Poll()
        {
            if (link == null || closed)
            {
                return;
            }

            if (!helloSent && link.State == LinkState.Connected)
            {
                helloSent = true;
                link.Send(NetProtocol.Hello(PlayerName));
            }

            link.Poll(Handle);
        }

        /// <summary>Sends the player's pick for <see cref="Pending"/> once it has been selected locally.</summary>
        public bool SendAnswerIfReady()
        {
            if (Pending == null || !Pending.Resolved)
            {
                return false;
            }

            link?.Send(NetProtocol.Answer(pendingId, Pending.selectedIndex));
            Pending = null;
            return true;
        }

        private void Handle(NetInbound inbound)
        {
            if (inbound.payload == null)
            {
                if (!closed)
                {
                    closed = true;
                    Disconnected?.Invoke(string.IsNullOrEmpty(Error) ? "Lost connection to the host." : Error);
                }

                return;
            }

            using (BinaryReader reader = NetProtocol.Open(inbound.payload, out MsgType type))
            {
                switch (type)
                {
                    case MsgType.Welcome:
                        HostName = reader.ReadString();
                        break;
                    case MsgType.Kick:
                        kickReason = reader.ReadString();
                        break;
                    case MsgType.Lobby:
                        ReadLobby(reader);
                        break;
                    case MsgType.Start:
                        ReadStart(reader);
                        break;
                    case MsgType.State:
                        ReadState(reader);
                        break;
                    case MsgType.Request:
                        ReadRequest(reader);
                        break;
                    case MsgType.ClearRequest:
                        if (reader.ReadInt32() == pendingId)
                        {
                            Pending = null;
                        }

                        break;
                    case MsgType.Log:
                    {
                        string line = reader.ReadString();
                        Mirror?.log.Add(line);
                        LogReceived?.Invoke(line);
                        break;
                    }

                    case MsgType.Roll:
                        if (Mirror != null)
                        {
                            RollContext roll = NetProtocol.ReadRoll(reader, (uid, id) => Resolve(uid, id), Mirror.players);
                            if (roll != null)
                            {
                                Mirror.activeRoll = roll;
                                RollReceived?.Invoke(roll);
                            }
                        }

                        break;
                    case MsgType.Reveal:
                    {
                        string caption = reader.ReadString();
                        List<CardInstance> revealed = ReadCards(reader, new Dictionary<int, CardInstance>());
                        RevealReceived?.Invoke(revealed, caption);
                        break;
                    }
                }
            }
        }

        private void ReadLobby(BinaryReader reader)
        {
            LobbyNames.Clear();
            int count = reader.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                LobbyNames.Add(reader.ReadString());
            }

            LobbyAiCount = reader.ReadInt32();
            LobbyRandomLeaders = reader.ReadBoolean();
            LobbyChanged?.Invoke();
        }

        private void ReadStart(BinaryReader reader)
        {
            int count = reader.ReadInt32();
            List<SeatConfig> seats = new List<SeatConfig>();
            for (int i = 0; i < count; i++)
            {
                seats.Add(new SeatConfig(reader.ReadString(), reader.ReadBoolean()));
            }

            MySeat = reader.ReadInt32();
            bool randomLeaders = reader.ReadBoolean();
            cards.Clear();
            Pending = null;
            ActiveChooser = -1;
            Mirror = new GameEngine(seats, 0, randomLeaders, false) { usePauses = false };
            foreach (PlayerState p in Mirror.players)
            {
                p.isRemote = p.index != MySeat && p.isHuman;
            }

            GameStarted?.Invoke(Mirror);
        }

        private CardInstance Resolve(int uid, string id)
        {
            if (uid == 0)
            {
                return null;
            }

            CardDefinition definition = string.IsNullOrEmpty(id) ? Hidden : HereToSlayCardDatabase.GetCard(id) ?? Hidden;
            if (cards.TryGetValue(uid, out CardInstance known) && known.def == definition)
            {
                return known;
            }

            CardInstance created = new CardInstance(definition, uid);
            cards[uid] = created;
            return created;
        }

        private CardInstance ReadOne(BinaryReader reader)
        {
            NetProtocol.ReadCard(reader, out int uid, out string id);
            return Resolve(uid, id);
        }

        private List<CardInstance> ReadCards(BinaryReader reader, Dictionary<int, CardInstance> temp)
        {
            int count = reader.ReadInt32();
            List<CardInstance> list = new List<CardInstance>(count);
            for (int i = 0; i < count; i++)
            {
                NetProtocol.ReadCard(reader, out int uid, out string id);
                list.Add(temp != null ? ResolveTemporary(uid, id, temp) : Resolve(uid, id));
            }

            return list;
        }

        /// <summary>For cards revealed only inside a question: never overwrites the face-down table copy.</summary>
        private CardInstance ResolveTemporary(int uid, string id, Dictionary<int, CardInstance> temp)
        {
            if (uid == 0)
            {
                return null;
            }

            if (temp.TryGetValue(uid, out CardInstance existing))
            {
                return existing;
            }

            CardDefinition definition = string.IsNullOrEmpty(id) ? Hidden : HereToSlayCardDatabase.GetCard(id) ?? Hidden;
            CardInstance card = cards.TryGetValue(uid, out CardInstance known) && known.def == definition
                ? known
                : new CardInstance(definition, uid);
            temp[uid] = card;
            return card;
        }

        private void FillHidden(List<CardInstance> target, int count)
        {
            while (hiddenPool.Count < count)
            {
                hiddenPool.Add(new CardInstance(Hidden, -1 - hiddenPool.Count));
            }

            target.Clear();
            for (int i = 0; i < count; i++)
            {
                target.Add(hiddenPool[i]);
            }
        }

        private void ReadState(BinaryReader reader)
        {
            GameEngine engine = Mirror;
            if (engine == null)
            {
                return;
            }

            engine.turnNumber = reader.ReadInt32();
            engine.currentPlayerIndex = reader.ReadInt32();
            int winner = reader.ReadInt32();
            int deckCount = reader.ReadInt32();
            int monsterDeckCount = reader.ReadInt32();
            ActiveChooser = reader.ReadInt32();
            ActiveKind = (ChoiceKind)reader.ReadByte();

            // Items are re-attached below; forget old attachments first.
            foreach (CardInstance card in cards.Values)
            {
                card.equippedItem = null;
            }

            int players = reader.ReadInt32();
            for (int i = 0; i < players; i++)
            {
                PlayerState p = engine.players[i];
                p.name = reader.ReadString();
                p.isHuman = reader.ReadBoolean();
                p.isRemote = p.isHuman && i != MySeat;
                p.actionPoints = reader.ReadInt32();
                p.leader = ReadOne(reader);
                List<CardInstance> hand = ReadCards(reader, null);
                p.hand.Clear();
                p.hand.AddRange(hand);
                int partyCount = reader.ReadInt32();
                p.party.Clear();
                for (int h = 0; h < partyCount; h++)
                {
                    CardInstance hero = ReadOne(reader);
                    CardInstance item = ReadOne(reader);
                    int itemOwner = reader.ReadInt32();
                    if (item != null)
                    {
                        item.itemOwner = itemOwner;
                    }

                    hero.equippedItem = item;
                    p.party.Add(hero);
                }

                List<CardInstance> slain = ReadCards(reader, null);
                p.slainMonsters.Clear();
                p.slainMonsters.AddRange(slain);
                p.heroesUsedThisTurn.Clear();
                int used = reader.ReadInt32();
                for (int u = 0; u < used; u++)
                {
                    p.heroesUsedThisTurn.Add(reader.ReadInt32());
                }
            }

            List<CardInstance> discard = ReadCards(reader, null);
            engine.discardPile.Clear();
            engine.discardPile.AddRange(discard);
            List<CardInstance> monsters = ReadCards(reader, null);
            engine.activeMonsters.Clear();
            engine.activeMonsters.AddRange(monsters);
            engine.stagedCard = ReadOne(reader);
            int stagedBy = reader.ReadInt32();
            engine.stagedBy = stagedBy >= 0 && stagedBy < engine.players.Count ? engine.players[stagedBy] : null;

            FillHidden(engine.deck, deckCount);
            // The monster deck is face down too; it gets its own placeholders so they never alias the main deck's.
            engine.monsterDeck.Clear();
            for (int i = 0; i < monsterDeckCount; i++)
            {
                engine.monsterDeck.Add(new CardInstance(Hidden, -100000 - i));
            }

            engine.winner = winner >= 0 && winner < engine.players.Count ? engine.players[winner] : null;
            StateVersion++;
        }

        private void ReadRequest(BinaryReader reader)
        {
            GameEngine engine = Mirror;
            if (engine == null)
            {
                return;
            }

            int id = reader.ReadInt32();
            int seat = reader.ReadInt32();
            ChoiceKind kind = (ChoiceKind)reader.ReadByte();
            string prompt = reader.ReadString();
            bool pickOnBoard = reader.ReadBoolean();
            float limit = reader.ReadSingle();
            ChoiceRequest request = new ChoiceRequest(engine.players[seat], kind, prompt)
            {
                pickOnBoard = pickOnBoard,
                timeLimit = limit
            };

            Dictionary<int, CardInstance> temp = new Dictionary<int, CardInstance>();
            int count = reader.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                string label = reader.ReadString();
                NetProtocol.ReadCard(reader, out int uid, out string cardId);
                CardInstance card = ResolveTemporary(uid, cardId, temp);
                int playerIndex = reader.ReadInt32();
                int value = reader.ReadInt32();
                string action = reader.ReadString();
                float score = reader.ReadSingle();
                request.Add(label, score, card, playerIndex >= 0 && playerIndex < engine.players.Count ? engine.players[playerIndex] : null,
                    action.Length == 0 ? null : action, value);
            }

            request.revealed.AddRange(ReadCards(reader, temp));
            pendingId = id;
            Pending = request;
        }
    }
}
