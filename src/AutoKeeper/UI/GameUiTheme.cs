using System;
using System.Linq;
using AutoKeeper.Core;
using TMPro;
using UnityEngine;

namespace AutoKeeper.UI
{
    /// <summary>
    /// The game's palette and font for the UI drawn by the mod (status panel and simple fallback window).
    /// Colors taken from GK2's Settings window (dark brown, greyish-beige labels, golden values).
    /// The font is the same as the game's texts (the TextMeshPro source font), when available.
    /// </summary>
    internal static class GameUiTheme
    {
        public static readonly Color PanelBackground = new Color(0.055f, 0.035f, 0.03f, 0.90f);
        public static readonly Color PanelInner = new Color(0.105f, 0.112f, 0.14f, 0.96f);
        public static readonly Color Border = new Color(0.42f, 0.29f, 0.19f, 1f);
        public static readonly Color Row = new Color(0.16f, 0.10f, 0.08f, 0.85f);
        public static readonly Color Button = new Color(0.28f, 0.12f, 0.07f, 1f);
        public static readonly Color ButtonHover = new Color(0.42f, 0.20f, 0.10f, 1f);
        public static readonly Color ButtonActive = new Color(0.55f, 0.36f, 0.12f, 1f);
        public static readonly Color Label = new Color(0.59f, 0.55f, 0.53f, 1f);
        public static readonly Color Value = new Color(1f, 0.74f, 0f, 1f);
        public static readonly Color Title = new Color(0.95f, 0.89f, 0.78f, 1f);

        // Same colors in hexadecimal for rich text.
        public const string LabelHex = "#978C87";
        public const string ValueHex = "#FFBD00";
        public const string TitleHex = "#F2E3C7";
        public const string GoodHex = "#A6D05A";
        public const string BadHex = "#E07A5F";
        public const string MutedHex = "#7D726D";

        private static Font font;
        private static bool fontSearched;

        /// <summary>Font of the game's texts (null = Unity's default font).</summary>
        public static Font Font
        {
            get
            {
                if (!fontSearched)
                {
                    fontSearched = true;
                    font = FindGameFont();
                }
                return font;
            }
        }

        /// <summary>Allows a new search (e.g. fonts loaded only after the main menu).</summary>
        public static void ResetFontCache()
        {
            if (font == null)
            {
                fontSearched = false;
            }
        }

        private static Font FindGameFont()
        {
            try
            {
                TMP_FontAsset[] all = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
                // Preference: the "small" font of the game's windows; then any font with a source font file.
                TMP_FontAsset pick = all.FirstOrDefault(f => f != null && f.sourceFontFile != null
                        && f.name.IndexOf("small_font", StringComparison.OrdinalIgnoreCase) >= 0 && f.name.IndexOf("bold", StringComparison.OrdinalIgnoreCase) < 0)
                    ?? all.FirstOrDefault(f => f != null && f.sourceFontFile != null && f.name.IndexOf("bold", StringComparison.OrdinalIgnoreCase) < 0)
                    ?? all.FirstOrDefault(f => f != null && f.sourceFontFile != null);
                if (pick != null)
                {
                    ModLog.Debug(Lang.T($"Fonte do jogo para a UI do mod: {pick.name} ({pick.sourceFontFile.name})",
                        $"Game font for the mod UI: {pick.name} ({pick.sourceFontFile.name})"));
                    return pick.sourceFontFile;
                }
            }
            catch (Exception e)
            {
                ModLog.Debug(Lang.T("Fonte do jogo indisponível: " + e.Message, "Game font unavailable: " + e.Message));
            }
            return null;
        }

        public static Texture2D MakeTex(Color c)
        {
            var t = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        /// <summary>Draws a rectangle with a 2 px border (game-window-style frame).</summary>
        public static void DrawFramedBox(Rect r, Texture2D fill, Texture2D border)
        {
            GUI.DrawTexture(r, border);
            GUI.DrawTexture(new Rect(r.x + 2f, r.y + 2f, r.width - 4f, r.height - 4f), fill);
        }
    }
}
