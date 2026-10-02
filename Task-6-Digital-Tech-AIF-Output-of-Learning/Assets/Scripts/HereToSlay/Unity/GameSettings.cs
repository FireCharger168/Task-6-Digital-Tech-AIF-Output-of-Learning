using UnityEngine;

namespace HereToSlay.View
{
    /// <summary>Player preferences, saved with PlayerPrefs so they survive restarts.</summary>
    public static class GameSettings
    {
        public static readonly float[] Speeds = { 0.5f, 1f, 1.5f, 2f, 3f, 4f };
        public static readonly string[] AiSpeedNames = { "Fast", "Normal", "Slow" };
        private static readonly float[] AiThinkTimes = { 0.15f, 0.35f, 0.7f };

        /// <summary>Seconds per action on your turn. When it runs out, your remaining energy is spent drawing cards (0 = no limit).</summary>
        public static readonly int[] TurnTimes = { 0, 10, 15, 20, 30, 45, 60 };
        /// <summary>Seconds for other decisions (targets, discards, yes/no). When it runs out a sensible choice is made for you.</summary>
        public static readonly int[] ChoiceTimes = { 0, 5, 8, 10, 15, 20, 30 };
        /// <summary>Seconds to react with a Challenge or Modifier card. When it runs out you pass.</summary>
        public static readonly int[] ReactionTimes = { 0, 2, 3, 5, 8, 10, 15 };

        private const int DefaultSpeed = 3;     // 2x
        private const int DefaultAi = 1;        // Normal
        private const int DefaultTurn = 3;      // 20 s
        private const int DefaultChoice = 3;    // 10 s
        private const int DefaultReaction = 2;  // 3 s

        public static int SpeedIndex = DefaultSpeed;
        public static int AiSpeedIndex = DefaultAi;
        public static int TurnTimerIndex = DefaultTurn;
        public static int ChoiceTimerIndex = DefaultChoice;
        public static int ReactionTimerIndex = DefaultReaction;
        public static bool Fullscreen = true;
        public static bool HoverZoom = true;
        public static bool EventFeed = true;

        public static float GameSpeed => Speeds[Mathf.Clamp(SpeedIndex, 0, Speeds.Length - 1)];
        public static float AiThinkTime => AiThinkTimes[Mathf.Clamp(AiSpeedIndex, 0, AiThinkTimes.Length - 1)];
        public static int TurnSeconds => TurnTimes[Mathf.Clamp(TurnTimerIndex, 0, TurnTimes.Length - 1)];
        public static int ChoiceSeconds => ChoiceTimes[Mathf.Clamp(ChoiceTimerIndex, 0, ChoiceTimes.Length - 1)];
        public static int ReactionSeconds => ReactionTimes[Mathf.Clamp(ReactionTimerIndex, 0, ReactionTimes.Length - 1)];

        public static string SecondsLabel(int seconds)
        {
            return seconds <= 0 ? "No limit" : $"{seconds}s";
        }

        // "hts2." keys: new defaults (faster game) replace the values saved by older versions.
        public static void Load()
        {
            SpeedIndex = PlayerPrefs.GetInt("hts2.speed", DefaultSpeed);
            AiSpeedIndex = PlayerPrefs.GetInt("hts2.aiSpeed", DefaultAi);
            TurnTimerIndex = PlayerPrefs.GetInt("hts2.turnTimer", DefaultTurn);
            ChoiceTimerIndex = PlayerPrefs.GetInt("hts2.choiceTimer", DefaultChoice);
            ReactionTimerIndex = PlayerPrefs.GetInt("hts2.reactionTimer", DefaultReaction);
            Fullscreen = PlayerPrefs.GetInt("hts2.fullscreen", 1) == 1;
            HoverZoom = PlayerPrefs.GetInt("hts2.hoverZoom", 1) == 1;
            EventFeed = PlayerPrefs.GetInt("hts2.eventFeed", 1) == 1;
        }

        public static void Save()
        {
            PlayerPrefs.SetInt("hts2.speed", SpeedIndex);
            PlayerPrefs.SetInt("hts2.aiSpeed", AiSpeedIndex);
            PlayerPrefs.SetInt("hts2.turnTimer", TurnTimerIndex);
            PlayerPrefs.SetInt("hts2.choiceTimer", ChoiceTimerIndex);
            PlayerPrefs.SetInt("hts2.reactionTimer", ReactionTimerIndex);
            PlayerPrefs.SetInt("hts2.fullscreen", Fullscreen ? 1 : 0);
            PlayerPrefs.SetInt("hts2.hoverZoom", HoverZoom ? 1 : 0);
            PlayerPrefs.SetInt("hts2.eventFeed", EventFeed ? 1 : 0);
            PlayerPrefs.Save();
        }

        public static void Apply()
        {
#if !UNITY_EDITOR
            if (Screen.fullScreen != Fullscreen)
            {
                Screen.fullScreen = Fullscreen;
            }
#endif
        }

        public static void ResetToDefaults()
        {
            SpeedIndex = DefaultSpeed;
            AiSpeedIndex = DefaultAi;
            TurnTimerIndex = DefaultTurn;
            ChoiceTimerIndex = DefaultChoice;
            ReactionTimerIndex = DefaultReaction;
            Fullscreen = true;
            HoverZoom = true;
            EventFeed = true;
        }
    }
}
