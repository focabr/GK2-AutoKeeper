using System.Collections.Generic;
using AutoKeeper.Bot;
using AutoKeeper.Config;
using AutoKeeper.Core;
using UnityEngine;

namespace AutoKeeper.UI
{
    /// <summary>
    /// Simple status panel (IMGUI) with the game's palette — used when the game-styled panel (<see cref="NativeStatusPanel"/>)
    /// is turned off or cannot be built. Content from <see cref="StatusContent"/>. Organized in blocks:
    ///   title + state (and the reason, when off/paused/idle);
    ///   aligned "label: value" rows (task, place, energy, sleep, session summary, hands);
    ///   recent events in chronological order (newest at the bottom), each with the game clock time it happened at
    ///   (fixed, not a running "X ago"); identical consecutive messages are one line with "×N";
    ///   settings button + keys.
    /// The content is rebuilt at most 5x per second; drawing uses fixed rectangles (alignment and indent of wrapped lines).
    /// </summary>
    internal sealed class Overlay
    {
        private const float RefreshSeconds = 0.2f;
        private const float Margin = 12f;
        private const float PadX = 12f;
        private const float PadY = 8f;
        private const float RowGap = 2f;
        private const float SectionGap = 5f;

        private enum RowKind
        {
            Title,
            Pair,
            Event,
            Note,
            Separator,
        }

        private struct Row
        {
            public RowKind Kind;
            public string A;      // title / label / event time
            public string B;      // state / value / event text
            public bool Muted;
        }

        private readonly Settings settings;
        private readonly BotController bot;
        private readonly List<Row> rows = new List<Row>();
        private GUIStyle titleStyle, stateStyle, labelStyle, valueStyle, mutedValueStyle, timeStyle, eventStyle, noteStyle, footerStyle, buttonStyle;
        private Texture2D fillTex, borderTex, lineTex, buttonTex, buttonHoverTex;
        private int styleFontSize = -1;
        private Font styleFont;
        private float nextRefresh;
        private float labelWidth;
        private float timeWidth;

        public Overlay(Settings settings, BotController bot)
        {
            this.settings = settings;
            this.bot = bot;
        }

        /// <summary>Area taken by the panel (GUI), so a click on it doesn't turn into an attack in the game.</summary>
        public Rect Rect { get; private set; }

        /// <summary>Called when the player clicks the panel's settings button.</summary>
        public System.Action OnSettingsClicked;

        /// <summary>The game-styled panel is shown instead: draw nothing.</summary>
        public bool Suppressed { get; set; }

        public void Draw()
        {
            if (!settings.ShowOverlay.Value || Suppressed || Event.current == null)
            {
                Rect = Rect.zero;
                return;
            }

            EnsureStyle();
            if (Event.current.type == EventType.Repaint && Time.unscaledTime >= nextRefresh || rows.Count == 0)
            {
                nextRefresh = Time.unscaledTime + RefreshSeconds;
                BuildRows();
            }

            bool pt = GameApi.IsGameLanguagePortuguese();
            float width = Mathf.Min(Screen.width * 0.4f, styleFontSize * 30f);
            float inner = width - PadX * 2f;
            float buttonHeight = styleFontSize * 1.8f;

            // Heights (the same in every GUI event, so the button always stays in the same place).
            float contentHeight = 0f;
            var heights = new float[rows.Count];
            for (int i = 0; i < rows.Count; i++)
            {
                heights[i] = RowHeight(rows[i], inner);
                contentHeight += heights[i] + RowGap;
            }
            float height = PadY + contentHeight + SectionGap + buttonHeight + PadY;
            Rect = PlaceInCorner(width, height);

            float x = Rect.x + PadX;
            float y = Rect.y + PadY;
            if (Event.current.type == EventType.Repaint)
            {
                GameUiTheme.DrawFramedBox(Rect, fillTex, borderTex);
                for (int i = 0; i < rows.Count; i++)
                {
                    DrawRow(rows[i], new Rect(x, y, inner, heights[i]));
                    y += heights[i] + RowGap;
                }
            }
            else
            {
                y += contentHeight;
            }
            y += SectionGap;

            string label = (pt ? "Configurações (" : "Settings (") + settings.OpenSettingsKey.Value + ")";
            float buttonWidth = Mathf.Min(inner * 0.55f, styleFontSize * 15f);
            var buttonRect = new Rect(x, y, buttonWidth, buttonHeight);
            if (Event.current.type == EventType.Repaint)
            {
                string keys = $"{settings.ToggleBotKey.Value} {(pt ? "liga/desliga" : "on/off")} · {settings.ToggleOverlayKey.Value} {(pt ? "esconde" : "hides")}";
                GUI.Label(new Rect(x + buttonWidth + 8f, y, inner - buttonWidth - 8f, buttonHeight), keys, footerStyle);
            }
            if (GUI.Button(buttonRect, label, buttonStyle))
            {
                OnSettingsClicked?.Invoke();
            }
        }

        // ------------------------------------------------------------------ content

        private void BuildRows()
        {
            bool pt = GameApi.IsGameLanguagePortuguese();
            StatusContent sc = StatusContent.Build(settings, bot, pt);
            rows.Clear();

            rows.Add(new Row { Kind = RowKind.Title, A = sc.Title, B = sc.State });
            if (sc.Reason != null)
            {
                rows.Add(Pair(pt ? "Motivo:" : "Reason:", sc.Reason));
            }
            rows.Add(new Row { Kind = RowKind.Separator });
            foreach (StatusContent.Pair p in sc.Rows)
            {
                rows.Add(new Row { Kind = RowKind.Pair, A = p.Label, B = p.Value, Muted = p.Muted });
            }
            if (sc.Note != null)
            {
                rows.Add(new Row { Kind = RowKind.Note, B = sc.Note });
            }
            if (sc.Events.Count > 0)
            {
                rows.Add(new Row { Kind = RowKind.Separator });
                foreach (StatusContent.EventLine e in sc.Events)
                {
                    rows.Add(new Row { Kind = RowKind.Event, A = e.Clock, B = e.Text });
                }
            }
            rows.Add(new Row { Kind = RowKind.Separator });

            // Label column: width of the widest label present.
            labelWidth = 0f;
            foreach (Row r in rows)
            {
                if (r.Kind == RowKind.Pair)
                {
                    labelWidth = Mathf.Max(labelWidth, labelStyle.CalcSize(new GUIContent(r.A)).x);
                }
            }
            labelWidth += 6f;
            timeWidth = timeStyle.CalcSize(new GUIContent("00:00")).x + 8f;
        }

        private static Row Pair(string label, string value) => new Row { Kind = RowKind.Pair, A = label, B = value };

        // ------------------------------------------------------------------ drawing

        private float RowHeight(Row r, float inner)
        {
            switch (r.Kind)
            {
                case RowKind.Title:
                    return titleStyle.CalcHeight(new GUIContent(r.A), inner);
                case RowKind.Pair:
                    return Mathf.Max(labelStyle.CalcHeight(new GUIContent(r.A), labelWidth),
                        (r.Muted ? mutedValueStyle : valueStyle).CalcHeight(new GUIContent(r.B), inner - labelWidth));
                case RowKind.Event:
                    return eventStyle.CalcHeight(new GUIContent(r.B), inner - timeWidth);
                case RowKind.Note:
                    return noteStyle.CalcHeight(new GUIContent(r.B), inner);
                default:
                    return SectionGap * 2f + 1f;
            }
        }

        private void DrawRow(Row r, Rect rect)
        {
            switch (r.Kind)
            {
                case RowKind.Title:
                    GUI.Label(rect, r.A, titleStyle);
                    GUI.Label(rect, r.B, stateStyle);
                    break;
                case RowKind.Pair:
                    GUI.Label(new Rect(rect.x, rect.y, labelWidth, rect.height), r.A, labelStyle);
                    GUI.Label(new Rect(rect.x + labelWidth, rect.y, rect.width - labelWidth, rect.height), r.B, r.Muted ? mutedValueStyle : valueStyle);
                    break;
                case RowKind.Event:
                    GUI.Label(new Rect(rect.x, rect.y, timeWidth, rect.height), r.A, timeStyle);
                    GUI.Label(new Rect(rect.x + timeWidth, rect.y, rect.width - timeWidth, rect.height), r.B, eventStyle);
                    break;
                case RowKind.Note:
                    GUI.Label(rect, r.B, noteStyle);
                    break;
                default:
                    GUI.DrawTexture(new Rect(rect.x, rect.y + SectionGap, rect.width, 1f), lineTex);
                    break;
            }
        }

        private Rect PlaceInCorner(float w, float h)
        {
            switch (settings.OverlayPosition.Value)
            {
                case OverlayCorner.TopRight: return BelowZoneLabel(new Rect(Screen.width - w - Margin, Margin, w, h));
                case OverlayCorner.BottomLeft: return new Rect(Margin, Screen.height - h - Margin, w, h);
                case OverlayCorner.BottomRight: return new Rect(Screen.width - w - Margin, Screen.height - h - Margin, w, h);
                default: return BelowZoneLabel(new Rect(Margin, Margin, w, h));
            }
        }

        /// <summary>A top-corner panel moves down below the game's location name plate when they would overlap sideways.</summary>
        private static Rect BelowZoneLabel(Rect r)
        {
            Rect label = GameApi.GetZoneLabelRect();
            if (label.width > 0f && label.xMax > r.xMin && label.xMin < r.xMax)
            {
                r.y = Mathf.Max(r.y, label.yMax + Margin);
            }
            return r;
        }

        private void EnsureStyle()
        {
            int fontSize = Mathf.Clamp(Mathf.RoundToInt(Screen.height / 62f), 12, 22);
            Font gameFont = GameUiTheme.Font;
            if (titleStyle != null && styleFontSize == fontSize && styleFont == gameFont)
            {
                return;
            }
            styleFontSize = fontSize;
            styleFont = gameFont;
            rows.Clear(); // widths depend on the font: rebuild the content

            fillTex = fillTex ?? GameUiTheme.MakeTex(GameUiTheme.PanelBackground);
            borderTex = borderTex ?? GameUiTheme.MakeTex(GameUiTheme.Border);
            lineTex = lineTex ?? GameUiTheme.MakeTex(new Color(GameUiTheme.Border.r, GameUiTheme.Border.g, GameUiTheme.Border.b, 0.8f));
            buttonTex = buttonTex ?? GameUiTheme.MakeTex(GameUiTheme.Button);
            buttonHoverTex = buttonHoverTex ?? GameUiTheme.MakeTex(GameUiTheme.ButtonHover);

            int small = Mathf.Max(10, fontSize - 3);
            titleStyle = Make(fontSize + 1, GameUiTheme.Title, wrap: false);
            titleStyle.fontStyle = FontStyle.Bold;
            stateStyle = Make(fontSize, GameUiTheme.Value, wrap: false);
            stateStyle.alignment = TextAnchor.UpperRight;
            labelStyle = Make(fontSize, GameUiTheme.Label, wrap: false);
            valueStyle = Make(fontSize, GameUiTheme.Value, wrap: true);
            mutedValueStyle = Make(small, GameUiTheme.Label, wrap: true);
            timeStyle = Make(small, GameUiTheme.Label, wrap: false);
            eventStyle = Make(small, GameUiTheme.Title, wrap: true);
            noteStyle = Make(small, GameUiTheme.Value, wrap: true);
            footerStyle = Make(small, GameUiTheme.Label, wrap: true);
            footerStyle.alignment = TextAnchor.MiddleRight;

            buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = Mathf.Max(11, fontSize - 1),
                alignment = TextAnchor.MiddleCenter,
                border = new RectOffset(0, 0, 0, 0),
            };
            buttonStyle.normal.background = buttonTex;
            buttonStyle.hover.background = buttonHoverTex;
            buttonStyle.active.background = buttonHoverTex;
            buttonStyle.normal.textColor = GameUiTheme.Value;
            buttonStyle.hover.textColor = GameUiTheme.Title;
            buttonStyle.active.textColor = GameUiTheme.Title;
            if (gameFont != null)
            {
                buttonStyle.font = gameFont;
            }
        }

        private GUIStyle Make(int size, Color color, bool wrap)
        {
            var st = new GUIStyle(GUI.skin.label)
            {
                richText = true,
                wordWrap = wrap,
                fontSize = size,
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0),
                alignment = TextAnchor.UpperLeft,
            };
            st.normal.textColor = color;
            if (styleFont != null)
            {
                st.font = styleFont;
            }
            return st;
        }
    }
}
