using System;
using System.Linq;

namespace HereToSlay
{
    /// <summary>
    /// Heuristic scores the engine attaches to every option it offers.
    /// An AI seat simply picks the highest-scoring option (with a little noise).
    /// </summary>
    public static class AIBrain
    {
        public static int Choose(ChoiceRequest request, Random rng)
        {
            if (request.options.Count == 0)
            {
                return 0;
            }

            int best = 0;
            float bestScore = float.MinValue;
            for (int i = 0; i < request.options.Count; i++)
            {
                float score = request.options[i].aiScore + (float)rng.NextDouble() * 0.25f;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = i;
                }
            }

            return best;
        }

        private static bool AddsNewClass(PlayerState p, HeroClass heroClass)
        {
            return heroClass != HeroClass.None && !p.HasClassInParty(heroClass);
        }

        /// <summary>How much a player wants to keep a card in hand.</summary>
        public static float KeepValue(GameEngine game, PlayerState p, CardInstance card)
        {
            switch (card.def.type)
            {
                case CardType.Hero:
                    return 5f + (AddsNewClass(p, card.def.heroClass) ? 3f : 0f) + (12 - card.def.rollRequirement) * 0.1f;
                case CardType.Magic:
                    return 4.2f;
                case CardType.Challenge:
                    return 3.6f;
                case CardType.Modifier:
                    return 3f + Math.Abs(card.def.modifierValue) * 0.15f;
                case CardType.Item:
                    if (card.def.maskClass != HeroClass.None)
                    {
                        return AddsNewClass(p, card.def.maskClass) ? 4.5f : 2f;
                    }

                    return 3f;
                case CardType.CursedItem:
                    return 2.8f;
                default:
                    return 1f;
            }
        }

        public static float HeroValue(GameEngine game, PlayerState owner, CardInstance hero)
        {
            HeroClass heroClass = hero.EffectiveClass;
            int sameClass = owner.PartyMemberClasses().Count(c => c == heroClass);
            float value = 3f + (sameClass <= 1 ? 3f : 0f);
            if (hero.equippedItem != null)
            {
                value += hero.equippedItem.def.type == CardType.CursedItem ? -1.5f : 1f;
            }

            value += (12 - hero.def.rollRequirement) * 0.15f;
            return value;
        }

        /// <summary>How much p would like to hurt this hero (destroy / sacrifice target selection).</summary>
        public static float ScoreEnemyHero(GameEngine game, PlayerState p, CardInstance hero)
        {
            PlayerState owner = game.OwnerOf(hero);
            if (owner == null || owner == p)
            {
                return -10f;
            }

            if (owner.HasSlain("terratuga") || owner.protectedFromDestruction)
            {
                return -5f;
            }

            return HeroValue(game, owner, hero) + owner.Threat() * 0.8f;
        }

        public static float ScoreStealTarget(GameEngine game, PlayerState p, CardInstance hero)
        {
            PlayerState owner = game.OwnerOf(hero);
            if (owner == null || owner == p)
            {
                return -10f;
            }

            if (owner.protectedFromStealing)
            {
                return -5f;
            }

            float value = ScoreEnemyHero(game, p, hero);
            if (AddsNewClass(p, hero.EffectiveClass))
            {
                value += 4f;
            }

            return value;
        }

        public static float ScoreItemTarget(GameEngine game, PlayerState p, CardInstance item, CardInstance hero)
        {
            PlayerState owner = game.OwnerOf(hero);
            if (item.def.type == CardType.CursedItem)
            {
                return owner == p ? -10f : ScoreEnemyHero(game, p, hero);
            }

            if (owner != p)
            {
                return -10f;
            }

            if (item.def.maskClass != HeroClass.None)
            {
                // Best on a hero whose class is duplicated, if the mask adds a new class.
                int sameClass = p.PartyMemberClasses().Count(c => c == hero.EffectiveClass);
                return sameClass > 1 ? 5f : 1f;
            }

            return 5f - hero.def.rollRequirement * 0.2f;
        }

        public static float ScoreItemRemoval(GameEngine game, PlayerState p, CardInstance hero)
        {
            PlayerState owner = game.OwnerOf(hero);
            bool cursed = hero.equippedItem != null && hero.equippedItem.def.type == CardType.CursedItem;
            if (owner == p)
            {
                return cursed ? 5f : -3f;
            }

            return cursed ? -3f : 2f + (owner?.Threat() ?? 0f) * 0.3f;
        }

        public static float ScorePlay(GameEngine game, PlayerState p, CardInstance card)
        {
            bool opponentsHaveHeroes = game.players.Any(o => o != p && o.party.Count > 0);
            switch (card.def.type)
            {
                case CardType.Hero:
                    return 6f + (AddsNewClass(p, card.def.heroClass) ? 3f : 0f) + GameEngine.ProbabilityAtLeast(card.def.rollRequirement, game.PassiveBonus(p, RollKind.HeroEffect, null, null)) * 1.5f;

                case CardType.Item:
                    if (card.def.maskClass != HeroClass.None)
                    {
                        bool useful = AddsNewClass(p, card.def.maskClass) && p.party.Any(h => h.equippedItem == null);
                        return useful ? 5.5f : 0.5f;
                    }

                    return p.party.Any(h => h.equippedItem == null) ? 3.5f : 0f;

                case CardType.CursedItem:
                    return game.players.Any(o => o != p && o.party.Any(h => h.equippedItem == null)) ? 3.2f : -1f;

                case CardType.Magic:
                    switch (card.def.id)
                    {
                        case "callToTheFallen":
                            return game.discardPile.Any(c => c.def.type == CardType.Hero) ? 6f : -2f;
                        case "criticalBoost":
                            return 4.5f;
                        case "destructiveSpell":
                            return opponentsHaveHeroes && p.hand.Count >= 2 ? 5.5f : -2f;
                        case "enchantedSpell":
                            return p.actionPoints >= 3 && game.activeMonsters.Any(m => HereToSlayCardDatabase.MeetsMonsterRequirements(m.def, p.PartyMemberClasses())) ? 4f : 0.3f;
                        case "entanglingTrap":
                            return opponentsHaveHeroes && p.hand.Count >= 3 ? 5f : -2f;
                        case "forcedExchange":
                            return opponentsHaveHeroes && p.party.Count > 0 ? 3.5f : -2f;
                        case "forcefulWinds":
                        {
                            int theirs = game.players.Where(o => o != p).Sum(o => o.party.Count(h => h.equippedItem != null && h.equippedItem.def.type == CardType.Item));
                            int cursedOnMine = p.party.Count(h => h.equippedItem != null && h.equippedItem.def.type == CardType.CursedItem);
                            int mine = p.party.Count(h => h.equippedItem != null && h.equippedItem.def.type == CardType.Item);
                            return 1f + theirs + cursedOnMine * 1.5f - mine;
                        }
                        case "windsOfChange":
                            return 3f;
                        default:
                            return 2f;
                    }

                default:
                    return -5f;
            }
        }

        public static float ScoreUseHero(GameEngine game, PlayerState p, CardInstance hero)
        {
            float probability = GameEngine.ProbabilityAtLeast(hero.def.rollRequirement, game.PassiveBonus(p, RollKind.HeroEffect, hero, null));
            float effectValue = 4.5f;
            bool opponentsHaveHeroes = game.players.Any(o => o != p && o.party.Count > 0);
            bool opponentsHaveCards = game.players.Any(o => o != p && o.hand.Count > 0);

            switch (hero.def.id)
            {
                case "nappingNibbles":
                    effectValue = 0f;
                    break;
                case "badAxe":
                case "seriousGrey":
                case "fluffy":
                case "shurikitty":
                case "hopper":
                case "spooky":
                case "kitNapper":
                case "whiskers":
                case "wiggles":
                case "tipsyTootie":
                case "meowzio":
                    effectValue = opponentsHaveHeroes ? 7f : 0.5f;
                    break;
                case "bearClaw":
                case "furyKnuckle":
                case "luckyBucky":
                case "slyPickings":
                case "buttons":
                case "plunderingPuma":
                case "slipperyPaws":
                case "silentShadow":
                case "heavyBear":
                    effectValue = opponentsHaveCards ? 4.5f : 0.3f;
                    break;
                case "guidingLight":
                    effectValue = game.discardPile.Any(c => c.def.type == CardType.Hero) ? 5f : 0f;
                    break;
                case "radiantHorn":
                    effectValue = game.discardPile.Any(c => c.def.type == CardType.Modifier) ? 3.5f : 0f;
                    break;
                case "lookieRookie":
                    effectValue = game.discardPile.Any(c => c.def.IsItem) ? 3.5f : 0f;
                    break;
                case "bunBun":
                    effectValue = game.discardPile.Any(c => c.def.type == CardType.Magic) ? 4f : 0f;
                    break;
                case "holyCurselifter":
                    effectValue = p.party.Any(h => h.equippedItem != null && h.equippedItem.def.type == CardType.CursedItem) ? 5f : 0f;
                    break;
                case "wilyRed":
                    effectValue = Math.Max(0, 7 - p.hand.Count) * 1.2f;
                    break;
                case "vibrantGlow":
                case "wiseShield":
                    effectValue = p.actionPoints >= 3 ? 3f : 0.5f;
                    break;
                case "ironResolve":
                case "calmingVoice":
                case "mightyBlade":
                case "sharpFox":
                    effectValue = 1.5f;
                    break;
                case "toughTeddy":
                    effectValue = game.players.Count(o => o != p && o.HasClassInParty(HeroClass.Fighter)) * 1.5f;
                    break;
                case "dodgyDealer":
                    effectValue = game.players.Where(o => o != p).Select(o => o.hand.Count).DefaultIfEmpty(0).Max() - p.hand.Count;
                    break;
                case "smoothMimimeow":
                    effectValue = game.players.Count(o => o != p && o.HasClassInParty(HeroClass.Thief) && o.hand.Count > 0) * 2f;
                    break;
            }

            return probability * effectValue;
        }

        public static float ScoreAttack(GameEngine game, PlayerState p, CardInstance monster)
        {
            CardDefinition def = monster.def;
            int bonus = game.PassiveBonus(p, RollKind.AttackMonster, null, null);
            float slay;
            float fail;
            if (def.reversedRoll)
            {
                int modifierHelp = p.hand.Where(c => c.def.type == CardType.Modifier).Select(c => Math.Max(-Math.Min(c.def.modifierValue, c.def.modifierAltValue), 0)).DefaultIfEmpty(0).Max();
                slay = GameEngine.ProbabilityAtMost(def.slayRoll, bonus - modifierHelp / 2);
                fail = GameEngine.ProbabilityAtLeast(def.failRoll, bonus);
            }
            else
            {
                int modifierHelp = p.hand.Where(c => c.def.type == CardType.Modifier).Select(c => Math.Max(c.def.modifierValue, 0)).DefaultIfEmpty(0).Max();
                slay = GameEngine.ProbabilityAtLeast(def.slayRoll, bonus + modifierHelp / 2);
                fail = GameEngine.ProbabilityAtMost(def.failRoll, bonus);
            }

            float penalty = def.failPenalty == MonsterPenalty.SacrificeHero ? 5f : 3f;
            float value = 12f + p.slainMonsters.Count * 4f + def.slayDrawCards;
            return slay * value - fail * penalty;
        }

        public static float ScoreDraw(GameEngine game, PlayerState p)
        {
            return p.hand.Count >= 7 ? 0.2f : 2.4f;
        }

        public static float ScoreRedraw(GameEngine game, PlayerState p)
        {
            float handValue = p.hand.Sum(c => KeepValue(game, p, c));
            return p.hand.Count <= 2 && handValue < 8f ? 3f : 0.1f;
        }

        public static float ScoreChallenge(GameEngine game, PlayerState challenger, PlayerState player, CardInstance card)
        {
            float value = 0.3f + player.Threat() * 0.08f;
            switch (card.def.type)
            {
                case CardType.Hero:
                    value += player.HasClassInParty(card.def.heroClass) ? 0.1f : 0.35f;
                    break;
                case CardType.Magic:
                    value += 0.35f;
                    break;
                case CardType.CursedItem:
                    value += 0.3f;
                    break;
                case CardType.Item:
                    value += 0.1f;
                    break;
            }

            if (player.DistinctClassCount() >= 5 || player.slainMonsters.Count >= 2)
            {
                value += 0.6f;
            }

            return value;
        }

        public static float ScoreModifier(GameEngine game, PlayerState q, RollContext ctx, int value)
        {
            bool wantsSuccess = q == ctx.roller;
            bool now = ctx.IsSuccessfulFor(ctx.Total);
            bool after = ctx.IsSuccessfulFor(ctx.Total + value);

            if (wantsSuccess)
            {
                if (!now && after)
                {
                    return 2f - Math.Abs(value) * 0.05f;
                }

                if (ctx.kind == RollKind.AttackMonster && !ctx.reversed && ctx.Total <= ctx.failOn && ctx.Total + value > ctx.failOn && value > 0)
                {
                    return 0.8f;
                }

                return -1f;
            }

            // Not the roller: only bother to sabotage threatening players, and only when it flips the result.
            if (now && !after)
            {
                float threat = ctx.roller.Threat();
                float mine = q.Threat();
                return 0.6f + (threat - mine) * 0.15f + (ctx.kind == RollKind.AttackMonster ? 0.8f : 0f);
            }

            return -1f;
        }
    }
}
