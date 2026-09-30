namespace AutoKeeper.Core
{
    /// <summary>
    /// Picks the Brazilian Portuguese or the English text by the game's language — the same rule as the settings
    /// window and the status panel (Portuguese when the game is in Portuguese, English otherwise).
    /// Log and panel messages are built with it at the moment they are written.
    /// </summary>
    internal static class Lang
    {
        /// <summary>The game is set to Portuguese.</summary>
        public static bool Pt => GameApi.IsGameLanguagePortuguese();

        /// <summary>Returns <paramref name="pt"/> when the game is in Portuguese, otherwise <paramref name="en"/>.</summary>
        public static string T(string pt, string en) => Pt ? pt : en;
    }
}
