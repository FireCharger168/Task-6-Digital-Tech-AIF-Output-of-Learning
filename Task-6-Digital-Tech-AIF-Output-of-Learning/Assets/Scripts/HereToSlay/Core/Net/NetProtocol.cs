using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace HereToSlay.Net
{
    public enum MsgType : byte
    {
        // client -> host
        Hello = 1,
        Answer = 2,
        // host -> client
        Welcome = 20,
        Lobby = 21,
        Start = 22,
        State = 23,
        Request = 24,
        ClearRequest = 25,
        Log = 26,
        Roll = 27,
        Reveal = 28,
        Kick = 29
    }

    /// <summary>Binary message helpers shared by host and client.</summary>
    public static class NetProtocol
    {
        public const int Version = 1;
        public const int MaxMessageBytes = 4 * 1024 * 1024;
        public const string HiddenCardId = "cardBack000";

        public static BinaryWriter Begin(MsgType type, out MemoryStream stream)
        {
            stream = new MemoryStream();
            BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8);
            writer.Write((byte)type);
            return writer;
        }

        public static byte[] Finish(BinaryWriter writer, MemoryStream stream)
        {
            writer.Flush();
            return stream.ToArray();
        }

        public static BinaryReader Open(byte[] payload, out MsgType type)
        {
            BinaryReader reader = new BinaryReader(new MemoryStream(payload), Encoding.UTF8);
            type = (MsgType)reader.ReadByte();
            return reader;
        }

        // ------------------------------------------------------------ simple messages

        public static byte[] Hello(string name)
        {
            BinaryWriter w = Begin(MsgType.Hello, out MemoryStream s);
            w.Write(Version);
            w.Write(name ?? "");
            return Finish(w, s);
        }

        public static byte[] Answer(int requestId, int index)
        {
            BinaryWriter w = Begin(MsgType.Answer, out MemoryStream s);
            w.Write(requestId);
            w.Write(index);
            return Finish(w, s);
        }

        public static byte[] Welcome(string hostName)
        {
            BinaryWriter w = Begin(MsgType.Welcome, out MemoryStream s);
            w.Write(hostName ?? "");
            return Finish(w, s);
        }

        public static byte[] Kick(string reason)
        {
            BinaryWriter w = Begin(MsgType.Kick, out MemoryStream s);
            w.Write(reason ?? "");
            return Finish(w, s);
        }

        public static byte[] Lobby(IList<string> names, int aiCount, bool randomLeaders)
        {
            BinaryWriter w = Begin(MsgType.Lobby, out MemoryStream s);
            w.Write(names.Count);
            foreach (string name in names)
            {
                w.Write(name);
            }

            w.Write(aiCount);
            w.Write(randomLeaders);
            return Finish(w, s);
        }

        public static byte[] Start(IList<SeatConfig> seats, int yourIndex, bool randomLeaders)
        {
            BinaryWriter w = Begin(MsgType.Start, out MemoryStream s);
            w.Write(seats.Count);
            foreach (SeatConfig seat in seats)
            {
                w.Write(seat.name);
                w.Write(seat.isHuman);
            }

            w.Write(yourIndex);
            w.Write(randomLeaders);
            return Finish(w, s);
        }

        public static byte[] Log(string line)
        {
            BinaryWriter w = Begin(MsgType.Log, out MemoryStream s);
            w.Write(line ?? "");
            return Finish(w, s);
        }

        public static byte[] ClearRequest(int requestId)
        {
            BinaryWriter w = Begin(MsgType.ClearRequest, out MemoryStream s);
            w.Write(requestId);
            return Finish(w, s);
        }

        // ------------------------------------------------------------ cards

        /// <summary>uid 0 = no card. An empty id means "face down" (the receiver must not learn what it is).</summary>
        public static void WriteCard(BinaryWriter w, CardInstance card, bool visible = true)
        {
            if (card == null)
            {
                w.Write(0);
                return;
            }

            w.Write(card.uid);
            w.Write(visible ? card.def.id : "");
        }

        public static void ReadCard(BinaryReader r, out int uid, out string id)
        {
            uid = r.ReadInt32();
            id = uid == 0 ? null : r.ReadString();
        }

        public static void WriteCardList(BinaryWriter w, IList<CardInstance> cards, Func<CardInstance, bool> visible = null)
        {
            w.Write(cards.Count);
            foreach (CardInstance card in cards)
            {
                WriteCard(w, card, visible == null || visible(card));
            }
        }

        // ------------------------------------------------------------ state snapshot

        /// <summary>The whole public table as one player (<paramref name="seat"/>) is allowed to see it.</summary>
        public static byte[] State(GameEngine engine, int seat, int activeChooser, ChoiceKind activeKind)
        {
            BinaryWriter w = Begin(MsgType.State, out MemoryStream s);
            w.Write(engine.turnNumber);
            w.Write(engine.currentPlayerIndex);
            w.Write(engine.winner != null ? engine.winner.index : -1);
            w.Write(engine.deck.Count);
            w.Write(engine.monsterDeck.Count);
            w.Write(activeChooser);
            w.Write((byte)activeKind);

            w.Write(engine.players.Count);
            foreach (PlayerState p in engine.players)
            {
                w.Write(p.name);
                w.Write(p.isHuman);
                w.Write(p.actionPoints);
                WriteCard(w, p.leader);
                bool mine = p.index == seat;
                WriteCardList(w, p.hand, c => mine);
                w.Write(p.party.Count);
                foreach (CardInstance hero in p.party)
                {
                    WriteCard(w, hero);
                    WriteCard(w, hero.equippedItem);
                    w.Write(hero.equippedItem != null ? hero.equippedItem.itemOwner : -1);
                }

                WriteCardList(w, p.slainMonsters);
                w.Write(p.heroesUsedThisTurn.Count);
                foreach (int uid in p.heroesUsedThisTurn)
                {
                    w.Write(uid);
                }
            }

            WriteCardList(w, engine.discardPile);
            WriteCardList(w, engine.activeMonsters);
            WriteCard(w, engine.stagedCard);
            w.Write(engine.stagedBy != null ? engine.stagedBy.index : -1);
            return Finish(w, s);
        }

        // ------------------------------------------------------------ choice requests

        public static byte[] Request(GameEngine engine, ChoiceRequest request, int requestId, float timeLimit)
        {
            int seat = request.chooser.index;
            HashSet<CardInstance> revealed = new HashSet<CardInstance>(request.revealed);
            bool Visible(CardInstance card)
            {
                if (card == null || revealed.Contains(card))
                {
                    return true;
                }

                if (engine.deck.Contains(card))
                {
                    return false;
                }

                foreach (PlayerState p in engine.players)
                {
                    if (p.index != seat && p.hand.Contains(card))
                    {
                        return false;
                    }
                }

                return true;
            }

            BinaryWriter w = Begin(MsgType.Request, out MemoryStream s);
            w.Write(requestId);
            w.Write(seat);
            w.Write((byte)request.kind);
            w.Write(request.prompt ?? "");
            w.Write(request.pickOnBoard);
            w.Write(timeLimit);
            w.Write(request.options.Count);
            foreach (ChoiceOption option in request.options)
            {
                bool visible = Visible(option.card);
                w.Write(visible ? option.label ?? "" : "A face-down card");
                WriteCard(w, option.card, visible);
                w.Write(option.player != null ? option.player.index : -1);
                w.Write(option.value);
                w.Write(option.action ?? "");
                w.Write(option.aiScore);
            }

            WriteCardList(w, request.revealed);
            return Finish(w, s);
        }

        // ------------------------------------------------------------ rolls and reveals

        public static byte[] Roll(RollContext roll)
        {
            BinaryWriter w = Begin(MsgType.Roll, out MemoryStream s);
            WriteRoll(w, roll, 2);
            return Finish(w, s);
        }

        private static void WriteRoll(BinaryWriter w, RollContext roll, int depth)
        {
            w.Write(roll != null && depth > 0);
            if (roll == null || depth <= 0)
            {
                return;
            }

            w.Write(roll.roller != null ? roll.roller.index : -1);
            w.Write((byte)roll.kind);
            w.Write(roll.description ?? "");
            WriteCard(w, roll.hero);
            WriteCard(w, roll.monster);
            w.Write(roll.die1);
            w.Write(roll.die2);
            w.Write(roll.passiveBonus);
            w.Write(roll.modifierTotal);
            w.Write(roll.target);
            w.Write(roll.slayOn);
            w.Write(roll.failOn);
            w.Write(roll.reversed);
            w.Write(roll.isChallenger);
            WriteStrings(w, roll.bonusNotes);
            WriteStrings(w, roll.modifierNotes);
            WriteRoll(w, roll.opposing, depth - 1);
        }

        public static RollContext ReadRoll(BinaryReader r, Func<int, string, CardInstance> resolve, IList<PlayerState> players)
        {
            if (!r.ReadBoolean())
            {
                return null;
            }

            RollContext roll = new RollContext();
            int roller = r.ReadInt32();
            roll.roller = roller >= 0 && roller < players.Count ? players[roller] : null;
            roll.kind = (RollKind)r.ReadByte();
            roll.description = r.ReadString();
            ReadCard(r, out int heroUid, out string heroId);
            roll.hero = resolve(heroUid, heroId);
            ReadCard(r, out int monsterUid, out string monsterId);
            roll.monster = resolve(monsterUid, monsterId);
            roll.die1 = r.ReadInt32();
            roll.die2 = r.ReadInt32();
            roll.passiveBonus = r.ReadInt32();
            roll.modifierTotal = r.ReadInt32();
            roll.target = r.ReadInt32();
            roll.slayOn = r.ReadInt32();
            roll.failOn = r.ReadInt32();
            roll.reversed = r.ReadBoolean();
            roll.isChallenger = r.ReadBoolean();
            roll.bonusNotes.AddRange(ReadStrings(r));
            roll.modifierNotes.AddRange(ReadStrings(r));
            roll.opposing = ReadRoll(r, resolve, players);
            return roll;
        }

        public static byte[] Reveal(IList<CardInstance> cards, string caption)
        {
            BinaryWriter w = Begin(MsgType.Reveal, out MemoryStream s);
            w.Write(caption ?? "");
            WriteCardList(w, cards);
            return Finish(w, s);
        }

        private static void WriteStrings(BinaryWriter w, IList<string> list)
        {
            w.Write(list.Count);
            foreach (string item in list)
            {
                w.Write(item ?? "");
            }
        }

        private static List<string> ReadStrings(BinaryReader r)
        {
            int count = r.ReadInt32();
            List<string> list = new List<string>(count);
            for (int i = 0; i < count; i++)
            {
                list.Add(r.ReadString());
            }

            return list;
        }
    }
}
