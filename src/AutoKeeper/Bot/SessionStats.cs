namespace AutoKeeper.Bot
{
    /// <summary>
    /// What the bot did since the save was loaded, for the one-line summary on the status panel ("Session: 34 bodies ·
    /// slept 19× · on for 5h02"). Cleared on a new load or on returning to the main menu (BotController.ResetForNewWorld).
    /// </summary>
    internal static class SessionStats
    {
        /// <summary>Bodies finished: put in the crematorium or buried.</summary>
        public static int Bodies;

        /// <summary>Times the bot slept in the bed and woke up rested.</summary>
        public static int Sleeps;

        /// <summary>Real time with the bot turned on (pauses for menus, dialogues and sleep included).</summary>
        public static float OnSeconds;

        public static void Reset()
        {
            Bodies = 0;
            Sleeps = 0;
            OnSeconds = 0f;
        }
    }
}
