using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace HereToSlay
{
    public sealed class SeatConfig
    {
        public string name;
        public bool isHuman;

        public SeatConfig(string name, bool isHuman)
        {
            this.name = name;
            this.isHuman = isHuman;
        }
    }

    /// <summary>
    /// The complete Here to Slay rules engine. Pure C# (no Unity types) so it can be simulated headlessly.
    /// Every step that needs a decision yields a <see cref="ChoiceRequest"/>; see <see cref="EngineRunner"/>.
    /// </summary>
    public sealed partial class GameEngine
    {
        public const int MaxTurns = 500;

        public readonly List<PlayerState> players = new List<PlayerState>();
        public readonly List<CardInstance> deck = new List<CardInstance>();
        public readonly List<CardInstance> discardPile = new List<CardInstance>();
        public readonly List<CardInstance> monsterDeck = new List<CardInstance>();
        public readonly List<CardInstance> activeMonsters = new List<CardInstance>();
        public readonly List<CardInstance> leaderPool = new List<CardInstance>();

        public readonly Random rng;

        public int currentPlayerIndex;
        public int turnNumber;
        public PlayerState winner;
        public bool GameOver => winner != null;
        public PlayerState Current => players.Count == 0 ? null : players[currentPlayerIndex];

        /// <summary>The card currently being played (shown in the middle of the table while it can be challenged).</summary>
        public CardInstance stagedCard;
        public PlayerState stagedBy;
        public RollContext activeRoll;
        public ChoiceRequest activeRequest;

        /// <summary>When false, the engine does not yield Pause objects (fast headless simulation).</summary>
        public bool usePauses = true;

        public event Action<string> OnLog;
        public event Action<RollContext> OnRoll;
        /// <summary>A player privately sees some cards (viewer, cards, caption).</summary>
        public event Action<PlayerState, List<CardInstance>, string> OnReveal;
        public event Action<PlayerState> OnGameOver;

        public readonly List<string> log = new List<string>();

        public GameEngine(IList<SeatConfig> seats, int? seed = null)
        {
            rng = seed.HasValue ? new Random(seed.Value) : new Random();
            for (int i = 0; i < seats.Count; i++)
            {
                players.Add(new PlayerState { index = i, name = seats[i].name, isHuman = seats[i].isHuman });
            }
        }

        // ------------------------------------------------------------------ helpers

        public void Log(string message)
        {
            log.Add(message);
            if (log.Count > 400)
            {
                log.RemoveAt(0);
            }

            OnLog?.Invoke(message);
        }

        private object Wait(float seconds)
        {
            return usePauses ? new Pause(seconds) : null;
        }

        public int D6()
        {
            return rng.Next(1, 7);
        }

        private void Shuffle<T>(List<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                T tmp = list[i];
                list[i] = list[j];
                list[j] = tmp;
            }
        }

        public IEnumerable<PlayerState> OthersInTurnOrder(PlayerState from)
        {
            for (int k = 1; k < players.Count; k++)
            {
                yield return players[(from.index + k) % players.Count];
            }
        }

        public PlayerState OwnerOf(CardInstance hero)
        {
            return players.FirstOrDefault(p => p.party.Contains(hero));
        }

        public IEnumerable<CardInstance> AllPartyHeroes()
        {
            return players.SelectMany(p => p.party);
        }

        /// <summary>Probability that 2d6 + bonus reaches at least target.</summary>
        public static float ProbabilityAtLeast(int target, int bonus)
        {
            int hits = 0;
            for (int a = 1; a <= 6; a++)
            {
                for (int b = 1; b <= 6; b++)
                {
                    if (a + b + bonus >= target)
                    {
                        hits++;
                    }
                }
            }

            return hits / 36f;
        }

        public static float ProbabilityAtMost(int target, int bonus)
        {
            return 1f - ProbabilityAtLeast(target + 1, bonus);
        }

        public int PassiveBonus(PlayerState p, RollKind kind, CardInstance hero, List<string> notes)
        {
            int bonus = 0;
            void Add(int amount, string why)
            {
                bonus += amount;
                notes?.Add($"{(amount > 0 ? "+" : "")}{amount} {why}");
            }

            if (p.HasSlain("anuranCauldron"))
            {
                Add(1, "Anuran Cauldron");
            }

            if (p.rollBonusThisTurn != 0 && p == Current)
            {
                Add(p.rollBonusThisTurn, "this turn");
            }

            switch (kind)
            {
                case RollKind.HeroEffect:
                    if (p.HasLeader("theCharismaticSong")) Add(1, "Charismatic Song");
                    if (p.HasSlain("darkDragonKing")) Add(1, "Dark Dragon King");
                    if (hero != null && hero.HasItem("reallyBigRing")) Add(2, "Really Big Ring");
                    if (hero != null && hero.HasItem("curseOfTheSnakeSEyes")) Add(-2, "Snake's Eyes");
                    break;
                case RollKind.AttackMonster:
                    if (p.HasLeader("theDivineArrow")) Add(1, "Divine Arrow");
                    break;
                case RollKind.Challenge:
                    if (p.HasLeader("theFistOfReason")) Add(2, "Fist of Reason");
                    if (p.HasSlain("titanWyvern")) Add(1, "Titan Wyvern");
                    break;
            }

            return bonus;
        }

        public bool CheckForWinner()
        {
            if (GameOver)
            {
                return true;
            }

            // The current player wins ties.
            foreach (PlayerState p in new[] { Current }.Concat(OthersInTurnOrder(Current)))
            {
                if (HereToSlayCardDatabase.HasWon(p.ToContext()))
                {
                    winner = p;
                    string how = p.slainMonsters.Count >= HereToSlayCardDatabase.MonstersRequiredToWin
                        ? "slaying 3 Monsters"
                        : "gathering a Party of all 6 classes";
                    Log($"*** {p.name} WINS by {how}! ***");
                    OnGameOver?.Invoke(p);
                    return true;
                }
            }

            return false;
        }

        // ------------------------------------------------------------------ game flow

        public IEnumerator Run()
        {
            yield return Setup();

            while (!GameOver)
            {
                yield return TakeTurn(Current);
                if (GameOver)
                {
                    break;
                }

                if (turnNumber >= MaxTurns)
                {
                    winner = players.OrderByDescending(p => p.Threat()).First();
                    Log($"Turn limit reached. {winner.name} wins on points.");
                    OnGameOver?.Invoke(winner);
                    break;
                }

                currentPlayerIndex = (currentPlayerIndex + 1) % players.Count;
            }
        }

        private IEnumerator Setup()
        {
            foreach (CardDefinition def in HereToSlayCardDatabase.BuildMainDeck())
            {
                deck.Add(new CardInstance(def));
            }

            Shuffle(deck);

            foreach (CardDefinition def in HereToSlayCardDatabase.GetMonsterDeck())
            {
                monsterDeck.Add(new CardInstance(def));
            }

            Shuffle(monsterDeck);
            RefillMonsters();

            foreach (CardDefinition def in HereToSlayCardDatabase.GetPartyLeaders())
            {
                leaderPool.Add(new CardInstance(def));
            }

            Shuffle(leaderPool);
            Log("Welcome to Here to Slay! Choose your Party Leaders.");

            currentPlayerIndex = rng.Next(players.Count);

            foreach (PlayerState p in new[] { Current }.Concat(OthersInTurnOrder(Current)).ToList())
            {
                ChoiceRequest pick = new ChoiceRequest(p, ChoiceKind.PickCard, $"{p.name}: choose your Party Leader");
                foreach (CardInstance leader in leaderPool)
                {
                    pick.Add(leader.Name, (float)rng.NextDouble(), leader);
                }

                yield return Ask(pick);
                p.leader = pick.Chosen.card;
                leaderPool.Remove(p.leader);
                Log($"{p.name} leads with {p.leader.Name} ({p.leader.def.heroClass}).");
            }

            for (int round = 0; round < HereToSlayCardDatabase.StartingHandSize; round++)
            {
                foreach (PlayerState p in players)
                {
                    CardInstance card = DrawTop();
                    if (card != null)
                    {
                        p.hand.Add(card);
                    }
                }
            }

            Log($"{Current.name} goes first.");
            yield return Wait(0.5f);
        }

        /// <summary>Wraps a request so the host (UI or AI) can see which one is active.</summary>
        public IEnumerator Ask(ChoiceRequest request)
        {
            if (request.options.Count == 0)
            {
                request.Add("OK", 0f);
            }

            activeRequest = request;
            yield return request;
            activeRequest = null;
        }

        private IEnumerator TakeTurn(PlayerState p)
        {
            turnNumber++;
            p.protectedFromDestruction = false;
            p.protectedFromStealing = false;
            p.heroesUsedThisTurn.Clear();
            p.rollBonusThisTurn = 0;
            p.cannotBeChallengedThisTurn = false;
            p.shadowClawUsedThisTurn = false;
            p.actionPoints = p.MaxActionPoints();

            Log($"--- {p.name}'s turn ---");
            yield return Wait(0.4f);
            yield return DrawCards(p, 1);

            while (!GameOver && p.actionPoints > 0)
            {
                ChoiceRequest request = BuildMainActionRequest(p);
                yield return Ask(request);
                ChoiceOption choice = request.Chosen;
                if (choice == null || choice.action == "end")
                {
                    break;
                }

                yield return ExecuteMainAction(p, choice);
                CheckForWinner();
            }

            if (!GameOver)
            {
                yield return EnforceHandLimit(p);
            }

            p.rollBonusThisTurn = 0;
            p.cannotBeChallengedThisTurn = false;
        }

        private ChoiceRequest BuildMainActionRequest(PlayerState p)
        {
            ChoiceRequest request = new ChoiceRequest(p, ChoiceKind.MainAction,
                $"{p.name}: {p.actionPoints} action point{(p.actionPoints == 1 ? "" : "s")} left. Choose an action.")
            {
                pickOnBoard = true
            };

            if (p.actionPoints >= 1)
            {
                foreach (CardInstance card in p.hand)
                {
                    if (CanPlayFromHand(p, card))
                    {
                        request.Add($"Play {card.Name} (1 AP)", AIBrain.ScorePlay(this, p, card), card, null, "play");
                    }
                }

                foreach (CardInstance hero in p.party)
                {
                    if (!p.heroesUsedThisTurn.Contains(hero.uid) && !hero.HasItem("sealingKey"))
                    {
                        request.Add($"Roll for {hero.Name}'s effect (1 AP)", AIBrain.ScoreUseHero(this, p, hero), hero, null, "hero");
                    }
                }
            }

            if (p.actionPoints >= 2)
            {
                foreach (CardInstance monster in activeMonsters)
                {
                    if (HereToSlayCardDatabase.MeetsMonsterRequirements(monster.def, p.PartyMemberClasses()))
                    {
                        request.Add($"Attack {monster.Name} (2 AP)", AIBrain.ScoreAttack(this, p, monster), monster, null, "attack");
                    }
                }
            }

            if (p.actionPoints >= 1)
            {
                request.Add("Draw a card (1 AP)", AIBrain.ScoreDraw(this, p), null, null, "draw");
            }

            if (p.actionPoints >= 1 && p.HasLeader("theShadowClaw") && !p.shadowClawUsedThisTurn &&
                players.Any(o => o != p && o.hand.Count > 0))
            {
                request.Add("Shadow Claw: pull a card (1 AP)", AIBrain.ScoreDraw(this, p) + 0.3f, null, null, "claw");
            }

            if (p.actionPoints >= 3)
            {
                request.Add("Discard hand & draw 5 (3 AP)", AIBrain.ScoreRedraw(this, p), null, null, "redraw");
            }

            request.Add("End turn", 0.05f, null, null, "end");
            return request;
        }

        public bool CanPlayFromHand(PlayerState p, CardInstance card)
        {
            switch (card.def.type)
            {
                case CardType.Hero:
                    return true;
                case CardType.Item:
                case CardType.CursedItem:
                    return ItemTargets(p, card).Any();
                case CardType.Magic:
                    return true;
                default:
                    return false;
            }
        }

        public IEnumerable<CardInstance> ItemTargets(PlayerState p, CardInstance item)
        {
            if (item.def.type == CardType.CursedItem)
            {
                return AllPartyHeroes().Where(h => h.equippedItem == null);
            }

            return p.party.Where(h => h.equippedItem == null);
        }

        private IEnumerator ExecuteMainAction(PlayerState p, ChoiceOption choice)
        {
            switch (choice.action)
            {
                case "play":
                    p.actionPoints -= 1;
                    p.hand.Remove(choice.card);
                    yield return PlayCard(p, choice.card);
                    break;
                case "hero":
                    p.actionPoints -= 1;
                    yield return UseHero(p, choice.card);
                    break;
                case "attack":
                    p.actionPoints -= 2;
                    yield return AttackMonster(p, choice.card);
                    break;
                case "draw":
                    p.actionPoints -= 1;
                    yield return DrawCards(p, 1);
                    break;
                case "claw":
                    p.actionPoints -= 1;
                    p.shadowClawUsedThisTurn = true;
                    Ref<PlayerState> target = new Ref<PlayerState>();
                    yield return ChoosePlayer(p, "Shadow Claw: pull a card from whom?", players.Where(o => o != p && o.hand.Count > 0), o => o.hand.Count + o.Threat() * 0.3f, target);
                    if (target.value != null)
                    {
                        yield return Pull(p, target.value, 1, null);
                    }
                    break;
                case "redraw":
                    p.actionPoints -= 3;
                    Log($"{p.name} discards their hand ({p.hand.Count} cards) and draws 5.");
                    discardPile.AddRange(p.hand);
                    p.hand.Clear();
                    yield return DrawCards(p, 5);
                    break;
            }
        }

        private IEnumerator EnforceHandLimit(PlayerState p)
        {
            int excess = p.hand.Count - HereToSlayCardDatabase.MaxHandSize;
            if (excess > 0)
            {
                Log($"{p.name} has too many cards and must discard {excess}.");
                yield return DiscardCards(p, excess, "Hand limit is 7: discard a card");
            }
        }

        // ------------------------------------------------------------------ deck & hand

        public CardInstance DrawTop()
        {
            if (deck.Count == 0 && discardPile.Count > 0)
            {
                deck.AddRange(discardPile);
                discardPile.Clear();
                Shuffle(deck);
                Log("The discard pile is shuffled to form a new deck.");
            }

            if (deck.Count == 0)
            {
                return null;
            }

            CardInstance card = deck[deck.Count - 1];
            deck.RemoveAt(deck.Count - 1);
            return card;
        }

        public void RefillMonsters()
        {
            while (activeMonsters.Count < 3 && monsterDeck.Count > 0)
            {
                CardInstance monster = monsterDeck[monsterDeck.Count - 1];
                monsterDeck.RemoveAt(monsterDeck.Count - 1);
                activeMonsters.Add(monster);
            }
        }

        public IEnumerator DrawCards(PlayerState p, int count, List<CardInstance> drawn = null, bool triggers = true)
        {
            int got = 0;
            for (int i = 0; i < count; i++)
            {
                CardInstance card = DrawTop();
                if (card == null)
                {
                    break;
                }

                p.hand.Add(card);
                drawn?.Add(card);
                got++;

                if (!triggers)
                {
                    continue;
                }

                if (card.def.type == CardType.Modifier && p.HasSlain("rexMajor"))
                {
                    Log($"{p.name} reveals {card.Name} (Rex Major) and draws another card.");
                    yield return DrawCards(p, 1, drawn);
                }
                else if (card.def.IsItem && p.HasSlain("malamammoth") && CanPlayFromHand(p, card))
                {
                    yield return OfferPlayImmediately(p, card, "Malamammoth");
                }
                else if (card.def.type == CardType.Magic && p.HasSlain("orthus"))
                {
                    yield return OfferPlayImmediately(p, card, "Orthus");
                }
            }

            if (got > 0)
            {
                Log($"{p.name} draws {got} card{(got == 1 ? "" : "s")}.");
            }
            else
            {
                Log("The deck is empty.");
            }

            yield return Wait(0.2f);
        }

        /// <summary>Asks whether to play a card from hand immediately (for free). Plays it on "yes".</summary>
        public IEnumerator OfferPlayImmediately(PlayerState p, CardInstance card, string source)
        {
            if (!p.hand.Contains(card) || !CanPlayFromHand(p, card))
            {
                yield break;
            }

            ChoiceRequest ask = new ChoiceRequest(p, ChoiceKind.YesNo, $"{source}: play {card.Name} immediately?");
            ask.revealed.Add(card);
            ask.Add("Play it now", AIBrain.ScorePlay(this, p, card), card);
            ask.Add("Keep it", 1.5f);
            yield return Ask(ask);
            if (ask.selectedIndex == 0)
            {
                p.hand.Remove(card);
                yield return PlayCard(p, card);
            }
        }

        public IEnumerator DiscardCards(PlayerState p, int count, string prompt, List<CardInstance> discarded = null)
        {
            for (int i = 0; i < count && p.hand.Count > 0; i++)
            {
                ChoiceRequest request = new ChoiceRequest(p, ChoiceKind.PickCard, $"{p.name}: {prompt} ({count - i} left)");
                foreach (CardInstance card in p.hand)
                {
                    request.Add(card.Name, -AIBrain.KeepValue(this, p, card), card);
                }

                yield return Ask(request);
                CardInstance chosen = request.Chosen.card;
                p.hand.Remove(chosen);
                discardPile.Add(chosen);
                discarded?.Add(chosen);
                Log($"{p.name} discards {chosen.Name}.");
            }
        }

        /// <summary>PULL = take random cards from another player's hand.</summary>
        public IEnumerator Pull(PlayerState p, PlayerState from, int count, List<CardInstance> pulled)
        {
            for (int i = 0; i < count && from.hand.Count > 0; i++)
            {
                CardInstance card = from.hand[rng.Next(from.hand.Count)];
                from.hand.Remove(card);
                p.hand.Add(card);
                pulled?.Add(card);
                Log($"{p.name} pulls a card from {from.name}'s hand.");
                OnReveal?.Invoke(p, new List<CardInstance> { card }, $"You pulled {card.Name}");
            }

            yield return Wait(0.3f);
        }

        public IEnumerator ChoosePlayer(PlayerState chooser, string prompt, IEnumerable<PlayerState> candidates, Func<PlayerState, float> aiScore, Ref<PlayerState> result)
        {
            List<PlayerState> list = candidates.ToList();
            result.value = null;
            if (list.Count == 0)
            {
                Log("No valid player to choose.");
                yield break;
            }

            ChoiceRequest request = new ChoiceRequest(chooser, ChoiceKind.PickPlayer, prompt);
            foreach (PlayerState candidate in list)
            {
                request.Add($"{candidate.name} ({candidate.hand.Count} cards, {candidate.party.Count} heroes)", aiScore(candidate), null, candidate);
            }

            yield return Ask(request);
            result.value = request.Chosen.player;
        }

        public IEnumerator ChooseCard(PlayerState chooser, string prompt, IEnumerable<CardInstance> candidates, Func<CardInstance, float> aiScore, Ref<CardInstance> result, string noneLabel = null, bool reveal = false)
        {
            List<CardInstance> list = candidates.ToList();
            result.value = null;
            if (list.Count == 0)
            {
                yield break;
            }

            ChoiceRequest request = new ChoiceRequest(chooser, ChoiceKind.PickCard, prompt);
            foreach (CardInstance card in list)
            {
                PlayerState owner = OwnerOf(card);
                string label = owner != null && owner != chooser ? $"{card.Name} ({owner.name})" : card.Name;
                request.Add(label, aiScore(card), card);
                if (reveal)
                {
                    request.revealed.Add(card);
                }
            }

            if (noneLabel != null)
            {
                request.Add(noneLabel, 0f);
            }

            yield return Ask(request);
            result.value = request.Chosen.card;
        }

        // ------------------------------------------------------------------ playing cards

        public IEnumerator PlayCard(PlayerState p, CardInstance card)
        {
            switch (card.def.type)
            {
                case CardType.Hero:
                    yield return PlayHero(p, card);
                    break;
                case CardType.Item:
                case CardType.CursedItem:
                    yield return PlayItem(p, card);
                    break;
                case CardType.Magic:
                    yield return PlayMagic(p, card);
                    break;
                default:
                    p.hand.Add(card);
                    break;
            }

            CheckForWinner();
        }

        private IEnumerator Stage(PlayerState p, CardInstance card, string text)
        {
            stagedCard = card;
            stagedBy = p;
            Log(text);
            yield return Wait(0.7f);
        }

        private void Unstage()
        {
            stagedCard = null;
            stagedBy = null;
        }

        private IEnumerator PlayHero(PlayerState p, CardInstance card)
        {
            yield return Stage(p, card, $"{p.name} plays the Hero {card.Name}.");
            Ref<bool> blocked = new Ref<bool>();
            yield return ChallengeWindow(p, card, blocked);
            Unstage();
            if (blocked.value)
            {
                discardPile.Add(card);
                yield break;
            }

            p.party.Add(card);
            if (CheckForWinner())
            {
                yield break;
            }

            if (card.HasItem("sealingKey"))
            {
                yield break;
            }

            ChoiceRequest ask = new ChoiceRequest(p, ChoiceKind.YesNo, $"Roll to use {card.Name}'s effect now? ({card.def.rollRequirement}+)");
            ask.revealed.Add(card);
            ask.Add("Roll!", AIBrain.ScoreUseHero(this, p, card) + 1f, card);
            ask.Add("Not now", 0.5f);
            yield return Ask(ask);
            if (ask.selectedIndex == 0)
            {
                yield return UseHero(p, card);
            }
        }

        private IEnumerator PlayItem(PlayerState p, CardInstance card)
        {
            Ref<CardInstance> target = new Ref<CardInstance>();
            yield return ChooseCard(p, $"Equip {card.Name} to which Hero?", ItemTargets(p, card), h => AIBrain.ScoreItemTarget(this, p, card, h), target);
            if (target.value == null)
            {
                p.hand.Add(card);
                yield break;
            }

            PlayerState owner = OwnerOf(target.value);
            yield return Stage(p, card, $"{p.name} plays {card.Name} on {owner?.name}'s {target.value.Name}.");
            Ref<bool> blocked = new Ref<bool>();
            yield return ChallengeWindow(p, card, blocked);
            Unstage();

            if (blocked.value || OwnerOf(target.value) == null || target.value.equippedItem != null)
            {
                discardPile.Add(card);
                yield break;
            }

            target.value.equippedItem = card;
            card.itemOwner = p.index;
        }

        private IEnumerator PlayMagic(PlayerState p, CardInstance card)
        {
            yield return Stage(p, card, $"{p.name} casts {card.Name}.");
            Ref<bool> blocked = new Ref<bool>();
            yield return ChallengeWindow(p, card, blocked);
            if (blocked.value)
            {
                Unstage();
                discardPile.Add(card);
                yield break;
            }

            yield return ResolveMagic(p, card);
            Unstage();
            discardPile.Add(card);

            if (p.HasLeader("theCloakedSage"))
            {
                Log("The Cloaked Sage: draw a card.");
                yield return DrawCards(p, 1);
            }
        }

        // ------------------------------------------------------------------ challenges & rolls

        private IEnumerator ChallengeWindow(PlayerState p, CardInstance card, Ref<bool> blocked)
        {
            blocked.value = false;
            if (!card.def.canBeChallenged)
            {
                yield break;
            }

            if (p.cannotBeChallengedThisTurn && p == Current)
            {
                yield break;
            }

            if (card.def.IsItem && p.HasSlain("warwornOwlbear"))
            {
                yield break;
            }

            foreach (PlayerState other in OthersInTurnOrder(p).ToList())
            {
                CardInstance challengeCard = other.hand.FirstOrDefault(c => c.def.type == CardType.Challenge);
                if (challengeCard == null)
                {
                    continue;
                }

                ChoiceRequest ask = new ChoiceRequest(other, ChoiceKind.Challenge, $"{other.name}: {p.name} is playing {card.Name}. Challenge it?");
                ask.revealed.Add(card);
                ask.Add("Challenge!", AIBrain.ScoreChallenge(this, other, p, card), challengeCard);
                ask.Add("Let it pass", 0.7f);
                yield return Ask(ask);
                if (ask.selectedIndex != 0)
                {
                    continue;
                }

                other.hand.Remove(challengeCard);
                discardPile.Add(challengeCard);
                Log($"{other.name} CHALLENGES {p.name}'s {card.Name}!");
                yield return Wait(0.5f);

                if (p.HasSlain("bloodwing") && other.hand.Count > 0)
                {
                    Log($"Bloodwing: {other.name} must discard a card.");
                    yield return DiscardCards(other, 1, "Bloodwing: discard a card");
                }

                RollContext challengerRoll = new RollContext { roller = other, kind = RollKind.Challenge, isChallenger = true, description = $"{other.name} rolls to challenge" };
                RollContext defenderRoll = new RollContext { roller = p, kind = RollKind.Challenge, isChallenger = false, description = $"{p.name} rolls to defend {card.Name}" };
                challengerRoll.opposing = defenderRoll;
                defenderRoll.opposing = challengerRoll;

                yield return Roll(challengerRoll, false);
                yield return Roll(defenderRoll, true);

                if (challengerRoll.Total >= defenderRoll.Total)
                {
                    Log($"Challenge succeeds ({challengerRoll.Total} vs {defenderRoll.Total}). {card.Name} is discarded.");
                    blocked.value = true;
                }
                else
                {
                    Log($"Challenge fails ({challengerRoll.Total} vs {defenderRoll.Total}). {card.Name} resolves.");
                }

                yield return Wait(0.6f);
                yield break;
            }
        }

        /// <summary>Rolls 2d6, applies passive bonuses, then opens a modifier window for everybody.</summary>
        public IEnumerator Roll(RollContext ctx, bool announceOpposing = false)
        {
            ctx.die1 = D6();
            ctx.die2 = D6();
            ctx.passiveBonus = PassiveBonus(ctx.roller, ctx.kind, ctx.hero, ctx.bonusNotes);
            activeRoll = ctx;
            OnRoll?.Invoke(ctx);
            yield return Wait(1.0f);

            yield return ModifierWindow(ctx);

            Log($"{ctx.description}: {ctx.Breakdown()}");
            OnRoll?.Invoke(ctx);
            yield return Wait(0.8f);
        }

        private IEnumerator ModifierWindow(RollContext ctx)
        {
            int n = players.Count;
            int passesInARow = 0;
            int i = ctx.roller.index;
            int safety = 0;

            while (passesInARow < n && safety++ < 200)
            {
                PlayerState q = players[i];
                List<CardInstance> mods = q.hand.Where(c => c.def.type == CardType.Modifier).ToList();
                bool played = false;

                if (mods.Count > 0)
                {
                    ChoiceRequest ask = new ChoiceRequest(q, ChoiceKind.Modifier,
                        $"{q.name}: {ctx.description} — currently {ctx.Total}. Play a Modifier?");
                    foreach (CardInstance mod in mods)
                    {
                        foreach (int value in ModifierValues(q, mod))
                        {
                            ask.Add($"{(value > 0 ? "+" : "")}{value}  ({mod.Name})", AIBrain.ScoreModifier(this, q, ctx, value), mod, null, null, value);
                        }
                    }

                    ask.Add("Pass", 0.1f);
                    yield return Ask(ask);
                    ChoiceOption chosen = ask.Chosen;
                    if (chosen.card != null)
                    {
                        played = true;
                        q.hand.Remove(chosen.card);
                        discardPile.Add(chosen.card);
                        ctx.modifierTotal += chosen.value;
                        ctx.modifierNotes.Add($"{(chosen.value > 0 ? "+" : "-")} {Math.Abs(chosen.value)} ({q.name})");
                        Log($"{q.name} plays {chosen.card.Name}: {(chosen.value > 0 ? "+" : "")}{chosen.value} → {ctx.Total}.");

                        if (q != ctx.roller && ctx.roller.HasSlain("abyssQueen"))
                        {
                            ctx.modifierTotal += 1;
                            ctx.modifierNotes.Add("+ 1 (Abyss Queen)");
                            Log("Abyss Queen: +1.");
                        }

                        OnRoll?.Invoke(ctx);
                        yield return Wait(0.6f);

                        foreach (PlayerState serpent in players.Where(o => o.HasSlain("crownedSerpent")).ToList())
                        {
                            Log($"Crowned Serpent: {serpent.name} draws a card.");
                            yield return DrawCards(serpent, 1);
                        }
                    }
                }

                passesInARow = played ? 0 : passesInARow + 1;
                i = (i + 1) % n;
            }
        }

        private IEnumerable<int> ModifierValues(PlayerState q, CardInstance mod)
        {
            List<int> values = new List<int> { mod.def.modifierValue };
            if (mod.def.modifierAltValue != 0)
            {
                values.Add(mod.def.modifierAltValue);
            }

            if (q.HasLeader("theProtectingHorn"))
            {
                values = values.Select(v => v + Math.Sign(v)).ToList();
            }

            return values;
        }

        public IEnumerator UseHero(PlayerState p, CardInstance hero)
        {
            if (hero.HasItem("sealingKey"))
            {
                Log($"{hero.Name} is sealed and cannot use its effect.");
                yield break;
            }

            p.heroesUsedThisTurn.Add(hero.uid);
            RollContext ctx = new RollContext
            {
                roller = p,
                kind = RollKind.HeroEffect,
                hero = hero,
                target = hero.def.rollRequirement,
                description = $"{p.name} rolls for {hero.Name} (needs {hero.def.rollRequirement}+)"
            };
            yield return Roll(ctx);

            if (ctx.Total >= ctx.target)
            {
                Log($"Success! {hero.Name}: {hero.def.effectText}");
                if (p.HasSlain("articAries"))
                {
                    Log("Arctic Aries: draw a card.");
                    yield return DrawCards(p, 1);
                }

                yield return ResolveHeroEffect(p, hero);

                if (hero.HasItem("suspiciouslyShinyCoin") && p.hand.Count > 0)
                {
                    Log("Suspiciously Shiny Coin: discard a card.");
                    yield return DiscardCards(p, 1, "Shiny Coin: discard a card");
                }
            }
            else
            {
                Log($"{hero.Name}'s effect fails.");
                if (hero.HasItem("particularlyRustyCoin"))
                {
                    Log("Particularly Rusty Coin: draw a card.");
                    yield return DrawCards(p, 1);
                }
            }

            CheckForWinner();
        }

        private IEnumerator AttackMonster(PlayerState p, CardInstance monster)
        {
            Log($"{p.name} attacks {monster.Name}!");
            RollContext ctx = new RollContext
            {
                roller = p,
                kind = RollKind.AttackMonster,
                monster = monster,
                slayOn = monster.def.slayRoll,
                failOn = monster.def.failRoll,
                description = $"{p.name} attacks {monster.Name} (slay {monster.def.slayRoll}+, fail {monster.def.failRoll}-)"
            };
            yield return Roll(ctx);

            if (ctx.Total >= monster.def.slayRoll)
            {
                activeMonsters.Remove(monster);
                p.slainMonsters.Add(monster);
                Log($"{p.name} SLAYS {monster.Name}! Bonus: {monster.def.slainEffectText}");
                RefillMonsters();
                if (monster.def.id == "megaSlime")
                {
                    p.actionPoints += 1;
                }

                yield return Wait(0.8f);
            }
            else if (ctx.Total <= monster.def.failRoll)
            {
                Log($"{monster.Name} fights back!");
                if (monster.def.failPenalty == MonsterPenalty.DiscardTwo)
                {
                    yield return DiscardCards(p, 2, $"{monster.Name}: discard 2 cards");
                }
                else
                {
                    yield return Sacrifice(p, 1);
                }
            }
            else
            {
                Log($"{monster.Name} survives. Nothing happens.");
            }

            CheckForWinner();
        }

        // ------------------------------------------------------------------ heroes moving around

        private void RemoveHero(PlayerState owner, CardInstance hero, PlayerState itemGoesToHandOf = null)
        {
            owner.party.Remove(hero);
            if (hero.equippedItem != null)
            {
                if (itemGoesToHandOf != null)
                {
                    itemGoesToHandOf.hand.Add(hero.equippedItem);
                }
                else
                {
                    discardPile.Add(hero.equippedItem);
                }

                hero.equippedItem.itemOwner = -1;
                hero.equippedItem = null;
            }
        }

        public IEnumerator DestroyHero(PlayerState attacker, CardInstance hero, bool attackerTakesItem = false)
        {
            PlayerState owner = OwnerOf(hero);
            if (owner == null)
            {
                yield break;
            }

            if (owner.HasSlain("terratuga"))
            {
                Log($"Terratuga protects {owner.name}'s {hero.Name} from being destroyed.");
                yield break;
            }

            if (owner.protectedFromDestruction)
            {
                Log($"Mighty Blade protects {owner.name}'s {hero.Name}.");
                yield break;
            }

            if (hero.HasItem("decoyDoll"))
            {
                discardPile.Add(hero.equippedItem);
                hero.equippedItem = null;
                Log($"The Decoy Doll is destroyed instead of {hero.Name}.");
                yield break;
            }

            if (attacker != owner && attacker.HasSlain("corruptedSabretooth") && !owner.protectedFromStealing)
            {
                ChoiceRequest ask = new ChoiceRequest(attacker, ChoiceKind.YesNo, $"Corrupted Sabretooth: steal {hero.Name} instead of destroying it?");
                ask.revealed.Add(hero);
                ask.Add("Steal it", 2f);
                ask.Add("Destroy it", 1f);
                yield return Ask(ask);
                if (ask.selectedIndex == 0)
                {
                    yield return StealHero(attacker, hero, null);
                    yield break;
                }
            }

            RemoveHero(owner, hero, attackerTakesItem ? attacker : null);
            discardPile.Add(hero);
            Log($"{attacker.name} DESTROYS {owner.name}'s {hero.Name}.");
            yield return Wait(0.5f);

            if (owner.HasSlain("dracos"))
            {
                Log($"Dracos: {owner.name} draws a card.");
                yield return DrawCards(owner, 1);
            }
        }

        public IEnumerator StealHero(PlayerState thief, CardInstance hero, Ref<bool> success)
        {
            PlayerState owner = OwnerOf(hero);
            if (success != null)
            {
                success.value = false;
            }

            if (owner == null || owner == thief)
            {
                yield break;
            }

            if (owner.protectedFromStealing)
            {
                Log($"Calming Voice: {owner.name}'s Heroes cannot be stolen.");
                yield break;
            }

            owner.party.Remove(hero);
            thief.party.Add(hero);
            Log($"{thief.name} STEALS {hero.Name} from {owner.name}.");
            if (success != null)
            {
                success.value = true;
            }

            yield return Wait(0.5f);
        }

        public void MoveHero(CardInstance hero, PlayerState to)
        {
            PlayerState from = OwnerOf(hero);
            from?.party.Remove(hero);
            to.party.Add(hero);
        }

        public IEnumerator Sacrifice(PlayerState p, int count)
        {
            for (int i = 0; i < count; i++)
            {
                if (p.party.Count == 0)
                {
                    Log($"{p.name} has no Hero to sacrifice.");
                    yield break;
                }

                Ref<CardInstance> choice = new Ref<CardInstance>();
                yield return ChooseCard(p, $"{p.name}: SACRIFICE a Hero card", p.party, h => -AIBrain.HeroValue(this, p, h), choice);
                CardInstance hero = choice.value;
                if (hero.HasItem("decoyDoll"))
                {
                    discardPile.Add(hero.equippedItem);
                    hero.equippedItem = null;
                    Log($"{p.name}'s Decoy Doll is sacrificed instead of {hero.Name}.");
                    continue;
                }

                RemoveHero(p, hero);
                discardPile.Add(hero);
                Log($"{p.name} sacrifices {hero.Name}.");
                yield return Wait(0.4f);
            }
        }

        /// <summary>Choose any Hero in another player's Party and destroy it.</summary>
        public IEnumerator DestroyAHero(PlayerState p, bool attackerTakesItem = false)
        {
            Ref<CardInstance> target = new Ref<CardInstance>();
            yield return ChooseCard(p, "DESTROY a Hero card", players.Where(o => o != p).SelectMany(o => o.party),
                h => AIBrain.ScoreEnemyHero(this, p, h), target);
            if (target.value == null)
            {
                Log("There is no Hero to destroy.");
                yield break;
            }

            yield return DestroyHero(p, target.value, attackerTakesItem);
        }

        /// <summary>Choose any Hero in another player's Party and steal it.</summary>
        public IEnumerator StealAHero(PlayerState p, Ref<CardInstance> stolen, PlayerState onlyFrom = null)
        {
            Ref<CardInstance> target = new Ref<CardInstance>();
            IEnumerable<CardInstance> candidates = onlyFrom != null
                ? onlyFrom.party
                : players.Where(o => o != p).SelectMany(o => o.party);
            yield return ChooseCard(p, "STEAL a Hero card", candidates, h => AIBrain.ScoreStealTarget(this, p, h), target);
            if (stolen != null)
            {
                stolen.value = null;
            }

            if (target.value == null)
            {
                Log("There is no Hero to steal.");
                yield break;
            }

            Ref<bool> ok = new Ref<bool>();
            yield return StealHero(p, target.value, ok);
            if (ok.value && stolen != null)
            {
                stolen.value = target.value;
            }
        }
    }
}
