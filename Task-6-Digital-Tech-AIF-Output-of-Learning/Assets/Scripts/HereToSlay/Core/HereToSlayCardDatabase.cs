using System;
using System.Collections.Generic;
using System.Linq;
#if UNITY_2017_1_OR_NEWER
using UnityEngine;
#endif

namespace HereToSlay
{
    public enum CardType
    {
        Hero,
        PartyLeader,
        Monster,
        Item,
        CursedItem,
        Magic,
        Modifier,
        Challenge,
        Rule,
        CardBack
    }

    public enum HeroClass
    {
        None,
        Fighter,
        Bard,
        Guardian,
        Ranger,
        Thief,
        Wizard
    }

    public enum CardUse
    {
        Play,
        Attack,
        UseHeroEffect,
        React
    }

    public enum MonsterPenalty
    {
        None,
        SacrificeHero,
        DiscardTwo
    }

    [Serializable]
    public sealed class CardDefinition
    {
        public string id;
        public string displayName;
        public string assetName;
        public string spriteResourcePath;
        public CardType type;
        public HeroClass heroClass;
        public int deckCopies = 1;
        public int actionCostToPlay;
        public int actionCostToAttack;
        public int actionCostToUseHeroEffect;
        public int modifierValue;
        /// <summary>Second value for split modifiers such as +2/-2 (0 when the card only has one value).</summary>
        public int modifierAltValue;
        public bool canBeChallenged;
        public bool playableFromHand = true;
        public bool reactionCard;

        /// <summary>Heroes: the 2d6 total needed to use the effect.</summary>
        public int rollRequirement;

        /// <summary>Monsters: roll this or higher to slay.</summary>
        public int slayRoll;
        /// <summary>Monsters: roll this or lower and suffer the penalty.</summary>
        public int failRoll;
        public MonsterPenalty failPenalty;
        /// <summary>Monsters: cards you draw after slaying it (e.g. Mega Slime draws 2).</summary>
        public int slayDrawCards;
        /// <summary>
        /// Monsters like Dracos are reversed: rolling LOW (slayRoll or less) slays it,
        /// rolling HIGH (failRoll or more) triggers the penalty.
        /// </summary>
        public bool reversedRoll;

        /// <summary>Monster requirements. HeroClass.None means "any Hero".</summary>
        public List<HeroClass> monsterRequirements = new List<HeroClass>();

        /// <summary>Items: the class a Mask turns the equipped Hero into.</summary>
        public HeroClass maskClass = HeroClass.None;

        public string effectText = "";
        /// <summary>Monsters: the passive bonus you gain after slaying it.</summary>
        public string slainEffectText = "";

        public bool IsHero => type == CardType.Hero;
        public bool IsMonster => type == CardType.Monster;
        public bool IsPartyLeader => type == CardType.PartyLeader;
        public bool IsItem => type == CardType.Item || type == CardType.CursedItem;
        public bool RequiresPartyClasses => monsterRequirements != null && monsterRequirements.Count > 0;

#if UNITY_2017_1_OR_NEWER
        private Sprite cachedSprite;

        public Sprite LoadSprite()
        {
            if (cachedSprite != null)
            {
                return cachedSprite;
            }

            // Always build the sprite from the texture so every card is exactly 1 world unit wide,
            // whatever resolution the art was saved at.
            Texture2D texture = Resources.Load<Texture2D>(spriteResourcePath);
            if (texture != null)
            {
                texture.filterMode = FilterMode.Trilinear;
                texture.wrapMode = TextureWrapMode.Clamp;
                cachedSprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), texture.width);
            }

            return cachedSprite;
        }
#endif
    }

    [Serializable]
    public sealed class CardPlayContext
    {
        public int actionPoints;
        public int monstersSlain;
        public List<HeroClass> partyClasses = new List<HeroClass>();
        public List<string> partyCardIds = new List<string>();
    }

    public static class HereToSlayCardDatabase
    {
        public const string ResourceFolder = "here to slay card assets";
        public const int StartingActionPoints = 3;
        public const int MonstersRequiredToWin = 3;
        public const int HeroClassesRequiredToWin = 6;
        public const int StartingHandSize = 5;
        public const int MaxHandSize = 7;

        private static readonly HeroClass Any = HeroClass.None;

        public static readonly List<CardDefinition> Cards = new List<CardDefinition>
        {
            // ---------------- Party Leaders ----------------
            PartyLeader("theFistOfReason", "The Fist of Reason", HeroClass.Fighter,
                "Each time you roll to CHALLENGE, +2 to your roll."),
            PartyLeader("theCharismaticSong", "The Charismatic Song", HeroClass.Bard,
                "Each time you roll to use a Hero card's effect, +1 to your roll."),
            PartyLeader("theProtectingHorn", "The Protecting Horn", HeroClass.Guardian,
                "Each time you play a Modifier card on a roll, +1 or -1 to that roll."),
            PartyLeader("theDivineArrow", "The Divine Arrow", HeroClass.Ranger,
                "Each time you roll to ATTACK a Monster card, +1 to your roll."),
            PartyLeader("theShadowClaw", "The Shadow Claw", HeroClass.Thief,
                "Once per turn on your turn, you may spend an action point to PULL a card from another player's hand."),
            PartyLeader("theCloakedSage", "The Cloaked Sage", HeroClass.Wizard,
                "Each time you play a Magic card, DRAW a card."),

            // ---------------- Monsters ----------------
            Monster("abyssQueen", "Abyss Queen", 8, 5, MonsterPenalty.SacrificeHero,
                "Each time another player plays a Modifier card on one of your rolls, +1 to your roll.", Any, Any),
            Monster("articAries", "Arctic Aries", 10, 6, MonsterPenalty.SacrificeHero,
                "Each time you successfully roll to use a Hero card's effect, you may DRAW a card.", Any),
            Monster("anuranCauldron", "Anuran Cauldron", 7, 6, MonsterPenalty.SacrificeHero,
                "Each time you roll, +1 to your roll.", Any, Any, Any),
            Monster("bloodwing", "Bloodwing", 9, 6, MonsterPenalty.SacrificeHero,
                "Each time another player CHALLENGES you, that player must DISCARD a card.", Any, Any),
            SlayDraw(Monster("corruptedSabretooth", "Corrupted Sabretooth", 9, 6, MonsterPenalty.SacrificeHero,
                "Each time you would DESTROY a Hero card, you may STEAL that Hero card instead.", Any, Any, Any), 1),
            Monster("crownedSerpent", "Crowned Serpent", 10, 7, MonsterPenalty.SacrificeHero,
                "Each time any player (including you) plays a Modifier card, you may DRAW a card.", Any, Any),
            Reversed(Monster("dracos", "Dracos", 5, 8, MonsterPenalty.SacrificeHero,
                "Each time a Hero card in your Party is destroyed, you may DRAW a card.", Any)),
            Monster("darkDragonKing", "Dark Dragon King", 8, 4, MonsterPenalty.DiscardTwo,
                "Each time you roll to use a Hero card's effect, +1 to your roll.", HeroClass.Bard, Any),
            Monster("malamammoth", "Malamammoth", 8, 4, MonsterPenalty.DiscardTwo,
                "Each time you DRAW an Item card, you may play it immediately.", HeroClass.Ranger, Any),
            SlayDraw(Monster("megaSlime", "Mega Slime", 8, 7, MonsterPenalty.SacrificeHero,
                "You may spend an extra action point on each of your turns.", Any, Any, Any, Any), 2),
            Monster("orthus", "Orthus", 8, 4, MonsterPenalty.DiscardTwo,
                "Each time you DRAW a Magic card, you may play it immediately.", HeroClass.Wizard, Any),
            Monster("rexMajor", "Rex Major", 8, 4, MonsterPenalty.DiscardTwo,
                "Each time you DRAW a Modifier card, you may reveal it and DRAW a second card.", HeroClass.Guardian, Any),
            Monster("terratuga", "Terratuga", 11, 7, MonsterPenalty.SacrificeHero,
                "Your Hero cards cannot be destroyed.", Any),
            Monster("titanWyvern", "Titan Wyvern", 8, 4, MonsterPenalty.DiscardTwo,
                "Each time you roll for a Challenge card, +1 to your roll.", HeroClass.Fighter, Any),
            Monster("warwornOwlbear", "Warworn Owlbear", 8, 4, MonsterPenalty.DiscardTwo,
                "Item cards you play cannot be challenged.", HeroClass.Thief, Any),

            // ---------------- Fighters ----------------
            Hero("badAxe", "Bad Axe", HeroClass.Fighter, 8, "DESTROY a Hero card."),
            Hero("bearClaw", "Bear Claw", HeroClass.Fighter, 7, "PULL a card from another player's hand. If it is a Hero card, PULL a second card from that player's hand."),
            Hero("bearyWise", "Beary Wise", HeroClass.Fighter, 7, "Each other player must DISCARD a card. Choose one of the discarded cards and add it to your hand."),
            Hero("furyKnuckle", "Fury Knuckle", HeroClass.Fighter, 5, "PULL a card from another player's hand. If it is a Challenge card, PULL a second card from that player's hand."),
            Hero("heavyBear", "Heavy Bear", HeroClass.Fighter, 5, "Choose a player. That player must DISCARD 2 cards."),
            Hero("panChucks", "Pan Chucks", HeroClass.Fighter, 8, "DRAW 2 cards. If at least one of those cards is a Challenge card, you may reveal it, then DESTROY a Hero card."),
            Hero("qiBear", "Qi Bear", HeroClass.Fighter, 10, "DISCARD up to 3 cards. For each card discarded, DESTROY a Hero card."),
            Hero("toughTeddy", "Tough Teddy", HeroClass.Fighter, 4, "Each other player with a Fighter in their Party must DISCARD a card."),

            // ---------------- Bards ----------------
            Hero("dodgyDealer", "Dodgy Dealer", HeroClass.Bard, 9, "Trade hands with another player."),
            Hero("fuzzyCheeks", "Fuzzy Cheeks", HeroClass.Bard, 8, "DRAW a card and play a Hero card from your hand immediately."),
            Hero("greedyCheeks", "Greedy Cheeks", HeroClass.Bard, 8, "Each other player must give you a card from their hand."),
            Hero("luckyBucky", "Lucky Bucky", HeroClass.Bard, 7, "PULL a card from another player's hand. If that card is a Hero card, you may play it immediately."),
            Hero("mellowDee", "Mellow Dee", HeroClass.Bard, 7, "DRAW a card. If that card is a Hero card, you may play it immediately."),
            Hero("nappingNibbles", "Napping Nibbles", HeroClass.Bard, 2, "Do nothing."),
            Hero("peanut", "Peanut", HeroClass.Bard, 7, "DRAW 2 cards."),
            Hero("tipsyTootie", "Tipsy Tootie", HeroClass.Bard, 6, "Choose a player. STEAL a Hero card from that player's Party and move Tipsy Tootie to that player's Party."),

            // ---------------- Guardians ----------------
            Hero("calmingVoice", "Calming Voice", HeroClass.Guardian, 9, "Hero cards in your Party cannot be stolen until your next turn."),
            Hero("guidingLight", "Guiding Light", HeroClass.Guardian, 7, "Search the discard pile for a Hero card and add it to your hand."),
            Hero("holyCurselifter", "Holy Curselifter", HeroClass.Guardian, 5, "Return a Cursed Item card equipped to a Hero card in your Party to your hand."),
            Hero("ironResolve", "Iron Resolve", HeroClass.Guardian, 8, "Cards you play cannot be challenged for the rest of your turn."),
            Hero("mightyBlade", "Mighty Blade", HeroClass.Guardian, 8, "Hero cards in your Party cannot be destroyed until your next turn."),
            Hero("radiantHorn", "Radiant Horn", HeroClass.Guardian, 6, "Search the discard pile for a Modifier card and add it to your hand."),
            Hero("vibrantGlow", "Vibrant Glow", HeroClass.Guardian, 9, "+5 to all of your rolls until the end of your turn."),
            Hero("wiseShield", "Wise Shield", HeroClass.Guardian, 6, "+3 to all of your rolls until the end of your turn."),

            // ---------------- Rangers ----------------
            Hero("bullseye", "Bullseye", HeroClass.Ranger, 7, "Look at the top 3 cards of the deck. Add one to your hand, then return the other two to the top of the deck in any order."),
            Hero("hook", "Hook", HeroClass.Ranger, 6, "Play an Item card from your hand immediately and DRAW a card."),
            Hero("lookieRookie", "Lookie Rookie", HeroClass.Ranger, 5, "Search the discard pile for an Item card and add it to your hand."),
            Hero("quickDraw", "Quick Draw", HeroClass.Ranger, 8, "DRAW 2 cards. If at least one of those cards is an Item card, you may play one of them immediately."),
            Hero("seriousGrey", "Serious Grey", HeroClass.Ranger, 9, "DESTROY a Hero card and DRAW a card."),
            Hero("sharpFox", "Sharp Fox", HeroClass.Ranger, 5, "Look at another player's hand."),
            Hero("wildshot", "Wildshot", HeroClass.Ranger, 8, "DRAW 3 cards and DISCARD a card."),
            Hero("wilyRed", "Wily Red", HeroClass.Ranger, 10, "DRAW cards until you have 7 cards in your hand."),

            // ---------------- Thieves ----------------
            Hero("kitNapper", "Kit Napper", HeroClass.Thief, 9, "STEAL a Hero card."),
            Hero("meowzio", "Meowzio", HeroClass.Thief, 10, "Choose a player. STEAL a Hero card from that player's Party and PULL a card from that player's hand."),
            Hero("plunderingPuma", "Plundering Puma", HeroClass.Thief, 6, "PULL 2 cards from another player's hand. That player may DRAW a card."),
            Hero("shurikitty", "Shurikitty", HeroClass.Thief, 9, "DESTROY a Hero card. If that Hero card had an Item card equipped to it, add that Item card to your hand instead of moving it to the discard pile."),
            Hero("silentShadow", "Silent Shadow", HeroClass.Thief, 8, "Look at another player's hand. Choose a card and add it to your hand."),
            Hero("slipperyPaws", "Slippery Paws", HeroClass.Thief, 6, "PULL 2 cards from another player's hand, then DISCARD one of those cards."),
            Hero("slyPickings", "Sly Pickings", HeroClass.Thief, 6, "PULL a card from another player's hand. If that card is an Item card, you may play it immediately."),
            Hero("smoothMimimeow", "Smooth Mimimeow", HeroClass.Thief, 7, "PULL a card from the hand of each other player with a Thief in their Party."),

            // ---------------- Wizards ----------------
            Hero("bunBun", "Bun Bun", HeroClass.Wizard, 5, "Search the discard pile for a Magic card and add it to your hand."),
            Hero("buttons", "Buttons", HeroClass.Wizard, 6, "PULL a card from another player's hand. If that card is a Magic card, you may play it immediately."),
            Hero("fluffy", "Fluffy", HeroClass.Wizard, 10, "DESTROY 2 Hero cards."),
            Hero("hopper", "Hopper", HeroClass.Wizard, 7, "Choose a player. That player must SACRIFICE a Hero card."),
            Hero("snowball", "Snowball", HeroClass.Wizard, 6, "DRAW a card. If it is a Magic card, you may play it immediately and DRAW a second card."),
            Hero("spooky", "Spooky", HeroClass.Wizard, 10, "Each other player must SACRIFICE a Hero card."),
            Hero("whiskers", "Whiskers", HeroClass.Wizard, 11, "STEAL a Hero card and DESTROY a Hero card."),
            Hero("wiggles", "Wiggles", HeroClass.Wizard, 10, "STEAL a Hero card and roll to use its effect immediately."),

            // ---------------- Items ----------------
            Mask("bardMask", "Bard Mask", HeroClass.Bard),
            Item("decoyDoll", "Decoy Doll", CardType.Item, 1, "If the equipped Hero card would be sacrificed or destroyed, move Decoy Doll to the discard pile instead."),
            Mask("fighterMask", "Fighter Mask", HeroClass.Fighter),
            Mask("guardianMask", "Guardian Mask", HeroClass.Guardian),
            Item("particularlyRustyCoin", "Particularly Rusty Coin", CardType.Item, 2, "If you unsuccessfully roll to use the equipped Hero card's effect, DRAW a card."),
            Mask("rangerMask", "Ranger Mask", HeroClass.Ranger),
            Item("reallyBigRing", "Really Big Ring", CardType.Item, 2, "Each time you roll to use the equipped Hero card's effect, +2 to your roll."),
            Mask("thiefMask", "Thief Mask", HeroClass.Thief),
            Mask("wizardMask", "Wizard Mask", HeroClass.Wizard),

            Item("curseOfTheSnakeSEyes", "Curse of the Snake's Eyes", CardType.CursedItem, 1, "Each time you roll to use the equipped Hero card's effect, -2 to your roll."),
            Item("sealingKey", "Sealing Key", CardType.CursedItem, 1, "You cannot use the equipped Hero card's effect."),
            Item("suspiciouslyShinyCoin", "Suspiciously Shiny Coin", CardType.CursedItem, 2, "If you successfully roll to use the equipped Hero card's effect, DISCARD a card."),

            // ---------------- Modifiers ----------------
            Modifier("modifierPlus1Minus3", "Modifier +1/-3", 1, -3, 4),
            Modifier("modifierPlus2Minus2", "Modifier +2/-2", 2, -2, 9),
            Modifier("modifierPlus3Minus1", "Modifier +3/-1", 3, -1, 4),
            Modifier("modifierPlus4", "Modifier +4", 4, 0, 4),
            Modifier("modifierMinus4", "Modifier -4", -4, 0, 4),

            // ---------------- Magic ----------------
            Magic("callToTheFallen", "Call to the Fallen", 1, "Search the discard pile for a Hero card and add it to your hand."),
            Magic("criticalBoost", "Critical Boost", 3, "DRAW 3 cards and DISCARD a card."),
            Magic("destructiveSpell", "Destructive Spell", 1, "DISCARD a card, then DESTROY a Hero card."),
            Magic("enchantedSpell", "Enchanted Spell", 2, "+2 to all of your rolls until the end of your turn."),
            Magic("entanglingTrap", "Entangling Trap", 2, "DISCARD 2 cards, then STEAL a Hero card."),
            Magic("forcedExchange", "Forced Exchange", 1, "Choose a player. STEAL a Hero card from that player's Party, then move a Hero card from your Party to that player's Party."),
            Magic("forcefulWinds", "Forceful Winds", 1, "Return every equipped Item card to its player's hand."),
            Magic("windsOfChange", "Winds of Change", 1, "Return an Item card equipped to any player's Hero card to that player's hand, then DRAW a card."),

            // ---------------- Challenge ----------------
            Challenge("challenge", "Challenge", 14),

            NonPlayable("ruleCard", "Rule Card", CardType.Rule),
            NonPlayable("ruleCard2", "Rule Card 2", CardType.Rule),
            NonPlayable("cardBack000", "Card Back 000", CardType.CardBack),
            NonPlayable("cardBack001", "Card Back 001", CardType.CardBack),
            NonPlayable("cardBack002", "Card Back 002", CardType.CardBack),
        };

        private static readonly Dictionary<string, CardDefinition> CardsById =
            Cards.ToDictionary(card => card.id, StringComparer.OrdinalIgnoreCase);

        public static CardDefinition GetCard(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return null;
            }

            CardsById.TryGetValue(id, out CardDefinition card);
            return card;
        }

        public static IReadOnlyList<CardDefinition> GetCardsByType(CardType type)
        {
            return Cards.Where(card => card.type == type).ToList();
        }

        public static IReadOnlyList<CardDefinition> GetHeroesByClass(HeroClass heroClass)
        {
            return Cards.Where(card => card.type == CardType.Hero && card.heroClass == heroClass).ToList();
        }

        public static List<CardDefinition> BuildMainDeck()
        {
            List<CardDefinition> deck = new List<CardDefinition>();
            foreach (CardDefinition card in Cards)
            {
                if (!BelongsInMainDeck(card))
                {
                    continue;
                }

                for (int copy = 0; copy < Math.Max(1, card.deckCopies); copy++)
                {
                    deck.Add(card);
                }
            }

            return deck;
        }

        public static IReadOnlyList<CardDefinition> GetPartyLeaders()
        {
            return GetCardsByType(CardType.PartyLeader);
        }

        public static IReadOnlyList<CardDefinition> GetMonsterDeck()
        {
            return GetCardsByType(CardType.Monster);
        }

        public static int GetActionCost(CardDefinition card, CardUse use)
        {
            if (card == null)
            {
                return int.MaxValue;
            }

            switch (use)
            {
                case CardUse.Attack:
                    return card.actionCostToAttack;
                case CardUse.UseHeroEffect:
                    return card.actionCostToUseHeroEffect;
                case CardUse.React:
                    return 0;
                default:
                    return card.actionCostToPlay;
            }
        }

        public static bool CanPlay(CardDefinition card, CardPlayContext context, out string reason)
        {
            reason = "";

            if (card == null)
            {
                reason = "No card selected.";
                return false;
            }

            if (!card.playableFromHand || card.reactionCard)
            {
                reason = $"{card.displayName} is not played as an action.";
                return false;
            }

            if (context == null)
            {
                reason = "No play context supplied.";
                return false;
            }

            int cost = GetActionCost(card, CardUse.Play);
            if (context.actionPoints < cost)
            {
                reason = $"Need {cost} action point(s).";
                return false;
            }

            return true;
        }

        public static bool CanAttackMonster(CardDefinition monster, CardPlayContext context, out string reason)
        {
            reason = "";

            if (monster == null || monster.type != CardType.Monster)
            {
                reason = "Selected card is not a monster.";
                return false;
            }

            if (context == null)
            {
                reason = "No play context supplied.";
                return false;
            }

            if (context.actionPoints < monster.actionCostToAttack)
            {
                reason = $"Need {monster.actionCostToAttack} action point(s) to attack a monster.";
                return false;
            }

            if (!MeetsMonsterRequirements(monster, context.partyClasses))
            {
                reason = "Party does not meet this monster's requirement.";
                return false;
            }

            return true;
        }

        public static bool CanUseHeroEffect(CardDefinition hero, CardPlayContext context, out string reason)
        {
            reason = "";

            if (hero == null || hero.type != CardType.Hero)
            {
                reason = "Selected card is not a hero.";
                return false;
            }

            if (context == null)
            {
                reason = "No play context supplied.";
                return false;
            }

            if (context.actionPoints < hero.actionCostToUseHeroEffect)
            {
                reason = $"Need {hero.actionCostToUseHeroEffect} action point(s) to use this hero effect.";
                return false;
            }

            return true;
        }

        public static bool CanChallenge(CardDefinition targetCard)
        {
            return CanBeChallenged(targetCard);
        }

        public static IReadOnlyList<CardDefinition> GetPlayableCards(IEnumerable<string> handCardIds, CardPlayContext context)
        {
            if (handCardIds == null)
            {
                return new List<CardDefinition>();
            }

            return handCardIds
                .Select(GetCard)
                .Where(card => CanPlay(card, context, out string _))
                .ToList();
        }

        public static bool HasFullParty(IEnumerable<HeroClass> partyClasses)
        {
            if (partyClasses == null)
            {
                return false;
            }

            HashSet<HeroClass> uniqueClasses = new HashSet<HeroClass>(
                partyClasses.Where(heroClass => heroClass != HeroClass.None));

            return uniqueClasses.Count >= HeroClassesRequiredToWin;
        }

        public static bool HasSlainEnoughMonsters(int monstersSlain)
        {
            return monstersSlain >= MonstersRequiredToWin;
        }

        public static bool HasWon(CardPlayContext context)
        {
            if (context == null)
            {
                return false;
            }

            return HasSlainEnoughMonsters(context.monstersSlain) || HasFullParty(context.partyClasses);
        }

        /// <summary>
        /// partyClasses holds one entry per party member (the Party Leader counts as a member of its class).
        /// Class-specific requirements must each be met by a different member; "any" requirements are met by whoever is left.
        /// </summary>
        public static bool MeetsMonsterRequirements(CardDefinition monster, IEnumerable<HeroClass> partyClasses)
        {
            if (monster == null || monster.type != CardType.Monster)
            {
                return false;
            }

            List<HeroClass> available = partyClasses == null ? new List<HeroClass>() : partyClasses.ToList();

            if (monster.monsterRequirements == null || monster.monsterRequirements.Count == 0)
            {
                return true;
            }

            int anyNeeded = 0;
            foreach (HeroClass requiredClass in monster.monsterRequirements)
            {
                if (requiredClass == HeroClass.None)
                {
                    anyNeeded++;
                    continue;
                }

                int index = available.IndexOf(requiredClass);
                if (index < 0)
                {
                    return false;
                }

                available.RemoveAt(index);
            }

            return available.Count >= anyNeeded;
        }

        public static string DescribeRequirements(CardDefinition monster)
        {
            if (monster == null || monster.monsterRequirements == null || monster.monsterRequirements.Count == 0)
            {
                return "None";
            }

            return string.Join(" + ", monster.monsterRequirements.Select(c => c == HeroClass.None ? "Hero" : c.ToString()));
        }

        public static bool BelongsInMainDeck(CardDefinition card)
        {
            if (card == null)
            {
                return false;
            }

            return card.type == CardType.Hero
                || card.type == CardType.Item
                || card.type == CardType.CursedItem
                || card.type == CardType.Magic
                || card.type == CardType.Modifier
                || card.type == CardType.Challenge;
        }

        public static bool CanBeChallenged(CardDefinition card)
        {
            return card != null && card.canBeChallenged;
        }

        public static bool CanUseAsReaction(CardDefinition card)
        {
            return card != null && card.reactionCard;
        }

        public static int ApplyModifier(CardDefinition modifier, int roll)
        {
            if (modifier == null || modifier.type != CardType.Modifier)
            {
                return roll;
            }

            return roll + modifier.modifierValue;
        }

        public static List<CardDefinition> Shuffle(IEnumerable<CardDefinition> cards, System.Random random = null)
        {
            random = random ?? new System.Random();
            List<CardDefinition> shuffled = cards == null ? new List<CardDefinition>() : cards.ToList();

            for (int i = shuffled.Count - 1; i > 0; i--)
            {
                int swapIndex = random.Next(i + 1);
                CardDefinition temp = shuffled[i];
                shuffled[i] = shuffled[swapIndex];
                shuffled[swapIndex] = temp;
            }

            return shuffled;
        }

        public static string FullDescription(CardDefinition card)
        {
            if (card == null)
            {
                return "";
            }

            switch (card.type)
            {
                case CardType.Hero:
                    return $"{card.heroClass} Hero\nRoll {card.rollRequirement}+ to use:\n{card.effectText}";
                case CardType.PartyLeader:
                    return $"{card.heroClass} Party Leader\n{card.effectText}";
                case CardType.Monster:
                    string penalty = card.failPenalty == MonsterPenalty.DiscardTwo ? "DISCARD 2 cards" : "SACRIFICE a Hero card";
                    string slay = "SLAY this Monster card" + (card.slayDrawCards > 0 ? $" & DRAW {card.slayDrawCards} card{(card.slayDrawCards > 1 ? "s" : "")}" : "");
                    string rolls = card.reversedRoll
                        ? $"{card.slayRoll}- : {slay}\n{card.failRoll}+ : {penalty}"
                        : $"{card.slayRoll}+ : {slay}\n{card.failRoll}- : {penalty}";
                    return $"Monster\nRequirement: {DescribeRequirements(card)}\n{rolls}\nOnce slain: {card.slainEffectText}";
                case CardType.Item:
                    return $"Item\n{card.effectText}";
                case CardType.CursedItem:
                    return $"Cursed Item\n{card.effectText}";
                case CardType.Magic:
                    return $"Magic\n{card.effectText}";
                case CardType.Modifier:
                    return $"Modifier\nPlay on any roll (no action cost).\n{card.effectText}";
                case CardType.Challenge:
                    return $"Challenge\n{card.effectText}";
                default:
                    return card.effectText;
            }
        }

        private static CardDefinition Hero(string id, string name, HeroClass heroClass, int roll, string effect)
        {
            CardDefinition card = Basic(id, name, CardType.Hero);
            card.heroClass = heroClass;
            card.actionCostToUseHeroEffect = 1;
            card.rollRequirement = roll;
            card.effectText = effect;
            return card;
        }

        private static CardDefinition PartyLeader(string id, string name, HeroClass heroClass, string effect)
        {
            CardDefinition card = NonPlayable(id, name, CardType.PartyLeader);
            card.heroClass = heroClass;
            card.playableFromHand = false;
            card.actionCostToPlay = 0;
            card.effectText = effect;
            return card;
        }

        private static CardDefinition Monster(string id, string name, int slay, int fail, MonsterPenalty penalty, string slainEffect, params HeroClass[] requirements)
        {
            CardDefinition card = NonPlayable(id, name, CardType.Monster);
            card.actionCostToAttack = 2;
            card.slayRoll = slay;
            card.failRoll = fail;
            card.failPenalty = penalty;
            card.slainEffectText = slainEffect;
            card.effectText = slainEffect;
            card.monsterRequirements = requirements == null ? new List<HeroClass>() : requirements.ToList();
            return card;
        }

        private static CardDefinition Reversed(CardDefinition monster)
        {
            monster.reversedRoll = true;
            return monster;
        }

        private static CardDefinition SlayDraw(CardDefinition monster, int cards)
        {
            monster.slayDrawCards = cards;
            return monster;
        }

        private static CardDefinition Mask(string id, string name, HeroClass maskClass)
        {
            CardDefinition card = Item(id, name, CardType.Item, 1, $"The equipped Hero card is considered a {maskClass} instead of its original class.");
            card.maskClass = maskClass;
            return card;
        }

        private static CardDefinition Item(string id, string name, CardType type, int copies, string effect)
        {
            CardDefinition card = Basic(id, name, type);
            card.deckCopies = copies;
            card.effectText = effect;
            return card;
        }

        private static CardDefinition Magic(string id, string name, int copies, string effect)
        {
            CardDefinition card = Basic(id, name, CardType.Magic);
            card.deckCopies = copies;
            card.effectText = effect;
            return card;
        }

        private static CardDefinition Modifier(string id, string name, int value, int altValue, int copies)
        {
            CardDefinition card = Basic(id, name, CardType.Modifier);
            card.actionCostToPlay = 0;
            card.modifierValue = value;
            card.modifierAltValue = altValue;
            card.reactionCard = true;
            card.canBeChallenged = false;
            card.deckCopies = copies;
            card.effectText = altValue == 0
                ? $"{(value > 0 ? "+" : "")}{value} to any roll."
                : $"+{value} or {altValue} to any roll.";
            return card;
        }

        private static CardDefinition Challenge(string id, string name, int copies)
        {
            CardDefinition card = Basic(id, name, CardType.Challenge);
            card.actionCostToPlay = 0;
            card.reactionCard = true;
            card.canBeChallenged = false;
            card.deckCopies = copies;
            card.effectText = "You may play this card when another player attempts to play a Hero, Item, or Magic card. CHALLENGE that card. (Both players roll; if the challenger rolls equal or higher, the card is discarded.)";
            return card;
        }

        private static CardDefinition Basic(string id, string name, CardType type)
        {
            return new CardDefinition
            {
                id = id,
                displayName = name,
                assetName = id + ".png",
                spriteResourcePath = ResourceFolder + "/" + id,
                type = type,
                heroClass = HeroClass.None,
                actionCostToPlay = 1,
                actionCostToAttack = 0,
                actionCostToUseHeroEffect = 0,
                canBeChallenged = type == CardType.Hero || type == CardType.Item || type == CardType.CursedItem || type == CardType.Magic,
                playableFromHand = true,
                reactionCard = false
            };
        }

        private static CardDefinition NonPlayable(string id, string name, CardType type)
        {
            CardDefinition card = Basic(id, name, type);
            card.actionCostToPlay = 0;
            card.canBeChallenged = false;
            card.playableFromHand = false;
            return card;
        }
    }
}
