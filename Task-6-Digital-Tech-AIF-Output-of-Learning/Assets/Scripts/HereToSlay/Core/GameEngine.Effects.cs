using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace HereToSlay
{
    /// <summary>Card effects for every Hero and Magic card.</summary>
    public sealed partial class GameEngine
    {
        private IEnumerable<PlayerState> OpponentsWithCards(PlayerState p)
        {
            return players.Where(o => o != p && o.hand.Count > 0);
        }

        private IEnumerable<PlayerState> OpponentsWithHeroes(PlayerState p)
        {
            return players.Where(o => o != p && o.party.Count > 0);
        }

        private IEnumerator ChooseOpponentToPull(PlayerState p, string prompt, Ref<PlayerState> result)
        {
            yield return ChoosePlayer(p, prompt, OpponentsWithCards(p), o => o.hand.Count + o.Threat() * 0.4f, result);
        }

        private IEnumerator SearchDiscard(PlayerState p, CardType type, string what)
        {
            Ref<CardInstance> pick = new Ref<CardInstance>();
            yield return ChooseCard(p, $"Search the discard pile for {what}", discardPile.Where(c => c.def.type == type).Distinct().ToList(),
                c => AIBrain.KeepValue(this, p, c), pick, null, true);
            if (pick.value == null)
            {
                Log($"There is no {what} in the discard pile.");
                yield break;
            }

            discardPile.Remove(pick.value);
            p.hand.Add(pick.value);
            Log($"{p.name} takes {pick.value.Name} from the discard pile.");
        }

        private IEnumerator OfferPlayFromHandOfType(PlayerState p, CardType type, string source, bool mandatory = false)
        {
            List<CardInstance> candidates = p.hand.Where(c => (c.def.type == type || (type == CardType.Item && c.def.IsItem)) && CanPlayFromHand(p, c)).ToList();
            if (candidates.Count == 0)
            {
                yield break;
            }

            Ref<CardInstance> pick = new Ref<CardInstance>();
            yield return ChooseCard(p, $"{source}: choose a card to play immediately", candidates, c => AIBrain.ScorePlay(this, p, c) + 1f, pick, mandatory ? null : "Don't play");
            if (pick.value != null)
            {
                p.hand.Remove(pick.value);
                yield return PlayCard(p, pick.value);
            }
        }

        private IEnumerator ShowCards(PlayerState viewer, List<CardInstance> cards, string caption)
        {
            OnReveal?.Invoke(viewer, cards, caption);
            ChoiceRequest info = new ChoiceRequest(viewer, ChoiceKind.Info, caption);
            info.revealed.AddRange(cards);
            info.Add("OK", 1f);
            yield return Ask(info);
        }

        private IEnumerator ResolveHeroEffect(PlayerState p, CardInstance hero)
        {
            Ref<PlayerState> who = new Ref<PlayerState>();
            List<CardInstance> got = new List<CardInstance>();

            switch (hero.def.id)
            {
                // ---------------- Fighters
                case "badAxe":
                    yield return DestroyAHero(p);
                    break;

                case "bearClaw":
                    yield return ChooseOpponentToPull(p, "Bear Claw: pull from whom?", who);
                    if (who.value != null)
                    {
                        yield return Pull(p, who.value, 1, got);
                        if (got.Count > 0 && got[0].def.type == CardType.Hero)
                        {
                            Log("It's a Hero! Pull a second card.");
                            yield return Pull(p, who.value, 1, got);
                        }
                    }
                    break;

                case "bearyWise":
                {
                    List<CardInstance> discarded = new List<CardInstance>();
                    foreach (PlayerState other in OthersInTurnOrder(p).ToList())
                    {
                        yield return DiscardCards(other, 1, "Beary Wise: discard a card", discarded);
                    }

                    Ref<CardInstance> pick = new Ref<CardInstance>();
                    yield return ChooseCard(p, "Beary Wise: take one of the discarded cards", discarded, c => AIBrain.KeepValue(this, p, c), pick, null, true);
                    if (pick.value != null)
                    {
                        discardPile.Remove(pick.value);
                        p.hand.Add(pick.value);
                        Log($"{p.name} takes {pick.value.Name}.");
                    }
                    break;
                }

                case "furyKnuckle":
                    yield return ChooseOpponentToPull(p, "Fury Knuckle: pull from whom?", who);
                    if (who.value != null)
                    {
                        yield return Pull(p, who.value, 1, got);
                        if (got.Count > 0 && got[0].def.type == CardType.Challenge)
                        {
                            Log("It's a Challenge card! Pull a second card.");
                            yield return Pull(p, who.value, 1, got);
                        }
                    }
                    break;

                case "heavyBear":
                    yield return ChoosePlayer(p, "Heavy Bear: who must discard 2 cards?", OpponentsWithCards(p), o => o.hand.Count * 0.5f + o.Threat(), who);
                    if (who.value != null)
                    {
                        yield return DiscardCards(who.value, 2, "Heavy Bear: discard a card");
                    }
                    break;

                case "panChucks":
                    yield return DrawCards(p, 2, got);
                    CardInstance chal = got.FirstOrDefault(c => c.def.type == CardType.Challenge && p.hand.Contains(c));
                    if (chal != null && OpponentsWithHeroes(p).Any())
                    {
                        ChoiceRequest ask = new ChoiceRequest(p, ChoiceKind.YesNo, "Pan Chucks: reveal the Challenge card to DESTROY a Hero?");
                        ask.revealed.Add(chal);
                        ask.Add("Reveal & destroy", 2f);
                        ask.Add("No", 0.5f);
                        yield return Ask(ask);
                        if (ask.selectedIndex == 0)
                        {
                            Log($"{p.name} reveals a Challenge card.");
                            yield return DestroyAHero(p);
                        }
                    }
                    break;

                case "qiBear":
                {
                    int maxTargets = players.Where(o => o != p).Sum(o => o.party.Count);
                    int count = 0;
                    while (count < 3 && p.hand.Count > 0)
                    {
                        Ref<CardInstance> pick = new Ref<CardInstance>();
                        int capturedCount = count;
                        yield return ChooseCard(p, $"Qi Bear: discard a card to destroy a Hero ({count}/3)", p.hand.ToList(),
                            c => capturedCount < maxTargets ? 3f - AIBrain.KeepValue(this, p, c) * 0.5f : -5f, pick, "Stop discarding");
                        if (pick.value == null)
                        {
                            break;
                        }

                        p.hand.Remove(pick.value);
                        discardPile.Add(pick.value);
                        Log($"{p.name} discards {pick.value.Name}.");
                        count++;
                    }

                    for (int i = 0; i < count; i++)
                    {
                        yield return DestroyAHero(p);
                    }
                    break;
                }

                case "toughTeddy":
                    foreach (PlayerState other in OthersInTurnOrder(p).Where(o => o.HasClassInParty(HeroClass.Fighter)).ToList())
                    {
                        yield return DiscardCards(other, 1, "Tough Teddy: discard a card");
                    }
                    break;

                // ---------------- Bards
                case "dodgyDealer":
                    yield return ChoosePlayer(p, "Dodgy Dealer: trade hands with whom?", players.Where(o => o != p),
                        o => o.hand.Count - p.hand.Count + 0.1f, who);
                    if (who.value != null)
                    {
                        List<CardInstance> mine = p.hand.ToList();
                        p.hand.Clear();
                        p.hand.AddRange(who.value.hand);
                        who.value.hand.Clear();
                        who.value.hand.AddRange(mine);
                        Log($"{p.name} trades hands with {who.value.name}.");
                    }
                    break;

                case "fuzzyCheeks":
                    yield return DrawCards(p, 1);
                    yield return OfferPlayFromHandOfType(p, CardType.Hero, "Fuzzy Cheeks", true);
                    break;

                case "greedyCheeks":
                    foreach (PlayerState other in OthersInTurnOrder(p).Where(o => o.hand.Count > 0).ToList())
                    {
                        Ref<CardInstance> give = new Ref<CardInstance>();
                        yield return ChooseCard(other, $"Greedy Cheeks: give {p.name} a card", other.hand.ToList(), c => -AIBrain.KeepValue(this, other, c), give);
                        if (give.value != null)
                        {
                            other.hand.Remove(give.value);
                            p.hand.Add(give.value);
                            Log($"{other.name} gives {p.name} a card.");
                        }
                    }
                    break;

                case "luckyBucky":
                    yield return ChooseOpponentToPull(p, "Lucky Bucky: pull from whom?", who);
                    if (who.value != null)
                    {
                        yield return Pull(p, who.value, 1, got);
                        if (got.Count > 0 && got[0].def.type == CardType.Hero)
                        {
                            yield return OfferPlayImmediately(p, got[0], "Lucky Bucky");
                        }
                    }
                    break;

                case "mellowDee":
                    yield return DrawCards(p, 1, got);
                    if (got.Count > 0 && got[0].def.type == CardType.Hero)
                    {
                        yield return OfferPlayImmediately(p, got[0], "Mellow Dee");
                    }
                    break;

                case "nappingNibbles":
                    Log("Napping Nibbles is fast asleep. Nothing happens.");
                    break;

                case "peanut":
                    yield return DrawCards(p, 2);
                    break;

                case "tipsyTootie":
                {
                    yield return ChoosePlayer(p, "Tipsy Tootie: steal from whom?", OpponentsWithHeroes(p), o => o.Threat(), who);
                    if (who.value != null)
                    {
                        Ref<CardInstance> stolen = new Ref<CardInstance>();
                        yield return StealAHero(p, stolen, who.value);
                        if (stolen.value != null && p.party.Contains(hero))
                        {
                            MoveHero(hero, who.value);
                            Log($"Tipsy Tootie stumbles over to {who.value.name}'s Party.");
                        }
                    }
                    break;
                }

                // ---------------- Guardians
                case "calmingVoice":
                    p.protectedFromStealing = true;
                    Log($"{p.name}'s Heroes cannot be stolen until their next turn.");
                    break;

                case "guidingLight":
                    yield return SearchDiscard(p, CardType.Hero, "a Hero card");
                    break;

                case "holyCurselifter":
                {
                    List<CardInstance> cursed = p.party.Where(h => h.equippedItem != null && h.equippedItem.def.type == CardType.CursedItem).ToList();
                    Ref<CardInstance> pick = new Ref<CardInstance>();
                    yield return ChooseCard(p, "Holy Curselifter: lift the curse from which Hero?", cursed, h => 1f, pick);
                    if (pick.value == null)
                    {
                        Log("No Cursed Items to lift.");
                    }
                    else
                    {
                        CardInstance item = pick.value.equippedItem;
                        pick.value.equippedItem = null;
                        item.itemOwner = -1;
                        p.hand.Add(item);
                        Log($"{p.name} returns {item.Name} to their hand.");
                    }
                    break;
                }

                case "ironResolve":
                    p.cannotBeChallengedThisTurn = true;
                    Log($"Cards {p.name} plays cannot be challenged this turn.");
                    break;

                case "mightyBlade":
                    p.protectedFromDestruction = true;
                    Log($"{p.name}'s Heroes cannot be destroyed until their next turn.");
                    break;

                case "radiantHorn":
                    yield return SearchDiscard(p, CardType.Modifier, "a Modifier card");
                    break;

                case "vibrantGlow":
                    p.rollBonusThisTurn += 5;
                    Log($"{p.name} gets +5 to all rolls this turn.");
                    break;

                case "wiseShield":
                    p.rollBonusThisTurn += 3;
                    Log($"{p.name} gets +3 to all rolls this turn.");
                    break;

                // ---------------- Rangers
                case "bullseye":
                {
                    List<CardInstance> top = new List<CardInstance>();
                    for (int i = 0; i < 3; i++)
                    {
                        CardInstance c = DrawTop();
                        if (c != null)
                        {
                            top.Add(c);
                        }
                    }

                    Ref<CardInstance> pick = new Ref<CardInstance>();
                    yield return ChooseCard(p, "Bullseye: add one card to your hand", top, c => AIBrain.KeepValue(this, p, c), pick, null, true);
                    if (pick.value != null)
                    {
                        top.Remove(pick.value);
                        p.hand.Add(pick.value);
                    }

                    // Return the rest in any order: the chosen card goes back on top.
                    if (top.Count == 2)
                    {
                        Ref<CardInstance> onTop = new Ref<CardInstance>();
                        yield return ChooseCard(p, "Bullseye: which card goes back on TOP of the deck?", top, c => -AIBrain.KeepValue(this, p, c), onTop, null, true);
                        CardInstance first = onTop.value ?? top[0];
                        top.Remove(first);
                        deck.Add(top[0]);
                        deck.Add(first);
                    }
                    else
                    {
                        deck.AddRange(top);
                    }

                    Log($"{p.name} looks at the top 3 cards and keeps one.");
                    break;
                }

                case "hook":
                    yield return OfferPlayFromHandOfType(p, CardType.Item, "Hook");
                    yield return DrawCards(p, 1);
                    break;

                case "lookieRookie":
                    yield return SearchDiscard(p, CardType.Item, "an Item card");
                    break;

                case "quickDraw":
                {
                    yield return DrawCards(p, 2, got);
                    List<CardInstance> items = got.Where(c => c.def.IsItem && p.hand.Contains(c) && CanPlayFromHand(p, c)).ToList();
                    if (items.Count > 0)
                    {
                        Ref<CardInstance> pick = new Ref<CardInstance>();
                        yield return ChooseCard(p, "Quick Draw: play one of the drawn Items now?", items, c => AIBrain.ScorePlay(this, p, c) + 1f, pick, "Keep them", true);
                        if (pick.value != null)
                        {
                            p.hand.Remove(pick.value);
                            yield return PlayCard(p, pick.value);
                        }
                    }
                    break;
                }

                case "seriousGrey":
                    yield return DestroyAHero(p);
                    yield return DrawCards(p, 1);
                    break;

                case "sharpFox":
                    yield return ChoosePlayer(p, "Sharp Fox: look at whose hand?", OpponentsWithCards(p), o => o.Threat() + o.hand.Count * 0.2f, who);
                    if (who.value != null)
                    {
                        Log($"{p.name} looks at {who.value.name}'s hand.");
                        yield return ShowCards(p, who.value.hand.ToList(), $"{who.value.name}'s hand");
                    }
                    break;

                case "wildshot":
                    yield return DrawCards(p, 3);
                    yield return DiscardCards(p, 1, "Wildshot: discard a card");
                    break;

                case "wilyRed":
                {
                    int need = HereToSlayCardDatabase.MaxHandSize - p.hand.Count;
                    if (need > 0)
                    {
                        yield return DrawCards(p, need);
                    }
                    else
                    {
                        Log($"{p.name} already has 7 or more cards.");
                    }
                    break;
                }

                // ---------------- Thieves
                case "kitNapper":
                    yield return StealAHero(p, null);
                    break;

                case "meowzio":
                    yield return ChoosePlayer(p, "Meowzio: choose a player", players.Where(o => o != p && (o.party.Count > 0 || o.hand.Count > 0)), o => o.Threat() + o.hand.Count * 0.2f, who);
                    if (who.value != null)
                    {
                        if (who.value.party.Count > 0)
                        {
                            yield return StealAHero(p, null, who.value);
                        }

                        yield return Pull(p, who.value, 1, null);
                    }
                    break;

                case "plunderingPuma":
                    yield return ChooseOpponentToPull(p, "Plundering Puma: pull 2 cards from whom?", who);
                    if (who.value != null)
                    {
                        yield return Pull(p, who.value, 2, null);
                        yield return DrawCards(who.value, 1);
                    }
                    break;

                case "shurikitty":
                    yield return DestroyAHero(p, true);
                    break;

                case "silentShadow":
                    yield return ChooseOpponentToPull(p, "Silent Shadow: look at whose hand?", who);
                    if (who.value != null)
                    {
                        Ref<CardInstance> pick = new Ref<CardInstance>();
                        yield return ChooseCard(p, $"Silent Shadow: take a card from {who.value.name}'s hand", who.value.hand.ToList(), c => AIBrain.KeepValue(this, p, c), pick, null, true);
                        if (pick.value != null)
                        {
                            who.value.hand.Remove(pick.value);
                            p.hand.Add(pick.value);
                            Log($"{p.name} takes a card from {who.value.name}'s hand.");
                        }
                    }
                    break;

                case "slipperyPaws":
                    yield return ChooseOpponentToPull(p, "Slippery Paws: pull 2 cards from whom?", who);
                    if (who.value != null)
                    {
                        yield return Pull(p, who.value, 2, got);
                        Ref<CardInstance> pick = new Ref<CardInstance>();
                        yield return ChooseCard(p, "Slippery Paws: discard one of the pulled cards", got.Where(c => p.hand.Contains(c)).ToList(), c => -AIBrain.KeepValue(this, p, c), pick, null, true);
                        if (pick.value != null)
                        {
                            p.hand.Remove(pick.value);
                            discardPile.Add(pick.value);
                            Log($"{p.name} discards {pick.value.Name}.");
                        }
                    }
                    break;

                case "slyPickings":
                    yield return ChooseOpponentToPull(p, "Sly Pickings: pull from whom?", who);
                    if (who.value != null)
                    {
                        yield return Pull(p, who.value, 1, got);
                        if (got.Count > 0 && got[0].def.IsItem)
                        {
                            yield return OfferPlayImmediately(p, got[0], "Sly Pickings");
                        }
                    }
                    break;

                case "smoothMimimeow":
                    foreach (PlayerState other in OthersInTurnOrder(p).Where(o => o.HasClassInParty(HeroClass.Thief) && o.hand.Count > 0).ToList())
                    {
                        yield return Pull(p, other, 1, null);
                    }
                    break;

                // ---------------- Wizards
                case "bunBun":
                    yield return SearchDiscard(p, CardType.Magic, "a Magic card");
                    break;

                case "buttons":
                    yield return ChooseOpponentToPull(p, "Buttons: pull from whom?", who);
                    if (who.value != null)
                    {
                        yield return Pull(p, who.value, 1, got);
                        if (got.Count > 0 && got[0].def.type == CardType.Magic)
                        {
                            yield return OfferPlayImmediately(p, got[0], "Buttons");
                        }
                    }
                    break;

                case "fluffy":
                    yield return DestroyAHero(p);
                    yield return DestroyAHero(p);
                    break;

                case "hopper":
                    yield return ChoosePlayer(p, "Hopper: who must sacrifice a Hero?", OpponentsWithHeroes(p), o => o.Threat(), who);
                    if (who.value != null)
                    {
                        yield return Sacrifice(who.value, 1);
                    }
                    break;

                case "snowball":
                    yield return DrawCards(p, 1, got);
                    if (got.Count > 0 && got[0].def.type == CardType.Magic && p.hand.Contains(got[0]))
                    {
                        ChoiceRequest ask = new ChoiceRequest(p, ChoiceKind.YesNo, $"Snowball: play {got[0].Name} now and draw another card?");
                        ask.revealed.Add(got[0]);
                        ask.Add("Play it", AIBrain.ScorePlay(this, p, got[0]) + 1f);
                        ask.Add("Keep it", 1f);
                        yield return Ask(ask);
                        if (ask.selectedIndex == 0)
                        {
                            p.hand.Remove(got[0]);
                            yield return PlayCard(p, got[0]);
                            yield return DrawCards(p, 1);
                        }
                    }
                    break;

                case "spooky":
                    foreach (PlayerState other in OthersInTurnOrder(p).ToList())
                    {
                        yield return Sacrifice(other, 1);
                    }
                    break;

                case "whiskers":
                    yield return StealAHero(p, null);
                    yield return DestroyAHero(p);
                    break;

                case "wiggles":
                {
                    Ref<CardInstance> stolen = new Ref<CardInstance>();
                    yield return StealAHero(p, stolen);
                    if (stolen.value != null && !stolen.value.HasItem("sealingKey"))
                    {
                        Log($"Wiggles: {p.name} rolls to use {stolen.value.Name}'s effect.");
                        yield return UseHero(p, stolen.value);
                    }
                    break;
                }

                default:
                    Log($"({hero.Name} has no effect implemented.)");
                    break;
            }
        }

        private IEnumerator ResolveMagic(PlayerState p, CardInstance card)
        {
            Ref<PlayerState> who = new Ref<PlayerState>();

            switch (card.def.id)
            {
                case "callToTheFallen":
                    yield return SearchDiscard(p, CardType.Hero, "a Hero card");
                    break;

                case "criticalBoost":
                    yield return DrawCards(p, 3);
                    yield return DiscardCards(p, 1, "Critical Boost: discard a card");
                    break;

                case "destructiveSpell":
                    yield return DiscardCards(p, 1, "Destructive Spell: discard a card");
                    yield return DestroyAHero(p);
                    break;

                case "enchantedSpell":
                    p.rollBonusThisTurn += 2;
                    Log($"{p.name} gets +2 to all rolls this turn.");
                    break;

                case "entanglingTrap":
                    yield return DiscardCards(p, 2, "Entangling Trap: discard a card");
                    yield return StealAHero(p, null);
                    break;

                case "forcedExchange":
                    yield return ChoosePlayer(p, "Forced Exchange: exchange with whom?", OpponentsWithHeroes(p), o => o.Threat(), who);
                    if (who.value != null)
                    {
                        Ref<CardInstance> stolen = new Ref<CardInstance>();
                        yield return StealAHero(p, stolen, who.value);
                        if (stolen.value != null)
                        {
                            Ref<CardInstance> give = new Ref<CardInstance>();
                            yield return ChooseCard(p, $"Forced Exchange: move one of your Heroes to {who.value.name}", p.party.Where(h => h != stolen.value).ToList(),
                                h => -AIBrain.HeroValue(this, p, h), give);
                            if (give.value != null)
                            {
                                MoveHero(give.value, who.value);
                                Log($"{give.value.Name} moves to {who.value.name}'s Party.");
                            }
                        }
                    }
                    break;

                case "forcefulWinds":
                    foreach (CardInstance hero in AllPartyHeroes().Where(h => h.equippedItem != null).ToList())
                    {
                        ReturnItemToOwner(hero, OwnerOf(hero));
                    }

                    Log("Forceful Winds blow every Item back to its owner's hand.");
                    break;

                case "windsOfChange":
                {
                    Ref<CardInstance> pick = new Ref<CardInstance>();
                    yield return ChooseCard(p, "Winds of Change: return which equipped Item?", AllPartyHeroes().Where(h => h.equippedItem != null).ToList(),
                        h => AIBrain.ScoreItemRemoval(this, p, h), pick);
                    if (pick.value != null)
                    {
                        Log($"{pick.value.equippedItem.Name} is returned to its owner's hand.");
                        ReturnItemToOwner(pick.value, OwnerOf(pick.value));
                    }

                    yield return DrawCards(p, 1);
                    break;
                }

                default:
                    Log($"({card.Name} has no effect implemented.)");
                    break;
            }
        }

        private void ReturnItemToOwner(CardInstance hero, PlayerState heroOwner)
        {
            CardInstance item = hero.equippedItem;
            if (item == null)
            {
                return;
            }

            hero.equippedItem = null;
            PlayerState itemOwner = item.itemOwner >= 0 && item.itemOwner < players.Count ? players[item.itemOwner] : heroOwner;
            item.itemOwner = -1;
            (itemOwner ?? heroOwner).hand.Add(item);
        }
    }
}
