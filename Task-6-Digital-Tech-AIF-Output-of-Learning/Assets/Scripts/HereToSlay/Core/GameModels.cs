using System;
using System.Collections.Generic;
using System.Linq;

namespace HereToSlay
{
    /// <summary>One physical card in the game. Several instances can share a CardDefinition.</summary>
    public sealed class CardInstance
    {
        private static int nextUid = 1;

        public readonly int uid;
        public readonly CardDefinition def;

        /// <summary>Heroes only: the Item card equipped to this Hero.</summary>
        public CardInstance equippedItem;

        /// <summary>Items only: index of the player who played the item (its "owner").</summary>
        public int itemOwner = -1;

        public CardInstance(CardDefinition definition)
        {
            uid = nextUid++;
            def = definition;
        }

        public string Name => def.displayName;

        /// <summary>A Hero's class after Masks are applied.</summary>
        public HeroClass EffectiveClass
        {
            get
            {
                if (equippedItem != null && equippedItem.def.maskClass != HeroClass.None)
                {
                    return equippedItem.def.maskClass;
                }

                return def.heroClass;
            }
        }

        public bool HasItem(string itemId)
        {
            return equippedItem != null && equippedItem.def.id == itemId;
        }

        public override string ToString()
        {
            return def.displayName;
        }
    }

    public sealed class PlayerState
    {
        public int index;
        public string name;
        public bool isHuman;
        public CardInstance leader;
        public readonly List<CardInstance> hand = new List<CardInstance>();
        public readonly List<CardInstance> party = new List<CardInstance>();
        public readonly List<CardInstance> slainMonsters = new List<CardInstance>();

        public int actionPoints;
        public readonly HashSet<int> heroesUsedThisTurn = new HashSet<int>();
        public int rollBonusThisTurn;
        public bool cannotBeChallengedThisTurn;
        public bool shadowClawUsedThisTurn;
        public bool protectedFromStealing;
        public bool protectedFromDestruction;

        public bool HasLeader(string id)
        {
            return leader != null && leader.def.id == id;
        }

        public bool HasSlain(string monsterId)
        {
            return slainMonsters.Any(m => m.def.id == monsterId);
        }

        /// <summary>One entry per party member, the Party Leader included.</summary>
        public List<HeroClass> PartyMemberClasses()
        {
            List<HeroClass> classes = new List<HeroClass>();
            if (leader != null)
            {
                classes.Add(leader.def.heroClass);
            }

            classes.AddRange(party.Select(h => h.EffectiveClass));
            return classes;
        }

        public int DistinctClassCount()
        {
            return PartyMemberClasses().Where(c => c != HeroClass.None).Distinct().Count();
        }

        public bool HasClassInParty(HeroClass heroClass)
        {
            return PartyMemberClasses().Contains(heroClass);
        }

        public int CountInHand(CardType type)
        {
            return hand.Count(c => c.def.type == type);
        }

        public int MaxActionPoints()
        {
            return HereToSlayCardDatabase.StartingActionPoints + (HasSlain("megaSlime") ? 1 : 0);
        }

        public CardPlayContext ToContext()
        {
            return new CardPlayContext
            {
                actionPoints = actionPoints,
                monstersSlain = slainMonsters.Count,
                partyClasses = PartyMemberClasses(),
                partyCardIds = party.Select(h => h.def.id).ToList()
            };
        }

        /// <summary>Rough "how close to winning" score used by the AI to pick targets.</summary>
        public float Threat()
        {
            return slainMonsters.Count * 2.2f + DistinctClassCount() * 1.0f + party.Count * 0.35f;
        }

        public override string ToString()
        {
            return name;
        }
    }

    public enum RollKind
    {
        HeroEffect,
        AttackMonster,
        Challenge
    }

    /// <summary>Everything about a 2d6 roll in progress: modifiers can be added until everyone passes.</summary>
    public sealed class RollContext
    {
        public PlayerState roller;
        public RollKind kind;
        public string description;
        public CardInstance hero;
        public CardInstance monster;
        public int die1;
        public int die2;
        public int passiveBonus;
        public readonly List<string> bonusNotes = new List<string>();
        public int modifierTotal;
        public readonly List<string> modifierNotes = new List<string>();

        // For hero effects: success if Total >= target.
        public int target;
        // For monster attacks.
        public int slayOn;
        public int failOn;
        public bool reversed;
        // For challenges: the roll we are trying to beat / not be beaten by.
        public RollContext opposing;
        public bool isChallenger;

        public int Natural => die1 + die2;
        public int Total => Natural + passiveBonus + modifierTotal;

        public string Breakdown()
        {
            string text = $"{die1} + {die2}";
            if (passiveBonus != 0)
            {
                text += $" {(passiveBonus > 0 ? "+" : "-")} {Math.Abs(passiveBonus)} ({string.Join(", ", bonusNotes)})";
            }

            foreach (string note in modifierNotes)
            {
                text += " " + note;
            }

            return text + $" = {Total}";
        }

        /// <summary>From the roller's point of view: how good is this total? Used by the AI when choosing modifiers.</summary>
        public bool IsSuccessfulFor(int total)
        {
            switch (kind)
            {
                case RollKind.HeroEffect:
                    return total >= target;
                case RollKind.AttackMonster:
                    return reversed ? total <= slayOn : total >= slayOn;
                case RollKind.Challenge:
                    if (opposing == null)
                    {
                        return total >= 7;
                    }

                    return isChallenger ? total >= opposing.Total : total > opposing.Total;
                default:
                    return false;
            }
        }
    }

    public enum ChoiceKind
    {
        MainAction,
        PickCard,
        PickPlayer,
        YesNo,
        Modifier,
        Challenge,
        Info
    }

    public sealed class ChoiceOption
    {
        public string label;
        public CardInstance card;
        public PlayerState player;
        public int value;
        public string action;
        public float aiScore;

        public override string ToString()
        {
            return label;
        }
    }

    /// <summary>
    /// The engine yields one of these whenever a player has to decide something.
    /// A human answers it through the UI; an AI answers it through <see cref="AIBrain"/>.
    /// </summary>
    public sealed class ChoiceRequest
    {
        public PlayerState chooser;
        public ChoiceKind kind;
        public string prompt;
        public readonly List<ChoiceOption> options = new List<ChoiceOption>();
        /// <summary>Cards that should be shown face-up to the chooser (e.g. Sharp Fox looking at a hand).</summary>
        public readonly List<CardInstance> revealed = new List<CardInstance>();
        /// <summary>Main action requests: cards are picked by clicking them on the table.</summary>
        public bool pickOnBoard;

        public int selectedIndex = -1;
        public bool Resolved => selectedIndex >= 0;
        public ChoiceOption Chosen => Resolved && selectedIndex < options.Count ? options[selectedIndex] : null;

        public ChoiceRequest(PlayerState who, ChoiceKind kind, string prompt)
        {
            chooser = who;
            this.kind = kind;
            this.prompt = prompt;
        }

        public ChoiceOption Add(string label, float aiScore, CardInstance card = null, PlayerState player = null, string action = null, int value = 0)
        {
            ChoiceOption option = new ChoiceOption
            {
                label = label,
                aiScore = aiScore,
                card = card,
                player = player,
                action = action,
                value = value
            };
            options.Add(option);
            return option;
        }

        public void Select(int index)
        {
            if (index >= 0 && index < options.Count)
            {
                selectedIndex = index;
            }
        }

        public void Select(ChoiceOption option)
        {
            Select(options.IndexOf(option));
        }
    }

    /// <summary>Yielded by the engine to ask the host for a short visual pause. Ignored by headless simulations.</summary>
    public sealed class Pause
    {
        public readonly float seconds;

        public Pause(float seconds)
        {
            this.seconds = seconds;
        }
    }

    public sealed class Ref<T>
    {
        public T value;
    }
}
