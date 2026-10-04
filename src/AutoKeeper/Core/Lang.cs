namespace AutoKeeper.Core
{
    /// <summary>
    /// Picks the Brazilian Portuguese or the English text by the game's language — the same rule as the settings
    /// window and the status panel (Portuguese when the game is in Portuguese, English otherwise).
    /// Messages are built with it at the moment they are written; <see cref="TryPair"/> lets the panel keep both texts
    /// and show them again in the current language after the player switches it (0.3.38).
    /// </summary>
    internal static class Lang
    {
        // Both texts of the latest T() call: the panel and the bot state store them so a language switch re-translates
        // what is already on screen. Only exact matches are trusted (main thread only, like the rest of the mod).
        private static string lastPt;
        private static string lastEn;

        /// <summary>The game is set to Portuguese.</summary>
        public static bool Pt => GameApi.IsGameLanguagePortuguese();

        /// <summary>Returns <paramref name="pt"/> when the game is in Portuguese, otherwise <paramref name="en"/>.</summary>
        public static string T(string pt, string en)
        {
            lastPt = pt;
            lastEn = en;
            return Pt ? pt : en;
        }

        /// <summary>
        /// Both texts behind <paramref name="text"/> when it is the result of the latest <see cref="T"/> call (in either
        /// language). False for any other text (built from several calls, or not translated): keep it as it is.
        /// </summary>
        public static bool TryPair(string text, out string pt, out string en)
        {
            if (text != null && lastEn != null && (text == lastPt || text == lastEn))
            {
                pt = lastPt;
                en = lastEn;
                return true;
            }
            pt = null;
            en = null;
            return false;
        }

        /// <summary>The text for the current language: <paramref name="pt"/>/<paramref name="en"/> when known, else <paramref name="text"/>.</summary>
        public static string Pick(string text, string pt, string en) => en == null ? text : (Pt ? pt : en);
    }
}
