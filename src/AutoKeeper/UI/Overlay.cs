using System.Collections.Generic;
using System.Linq;
using System.Text;
using AutoKeeper.Bot;
using AutoKeeper.Config;
using AutoKeeper.Core;
using UnityEngine;

namespace AutoKeeper.UI
{
    /// <summary>
    /// Painel de status (IMGUI) com a paleta e a fonte do jogo: fundo marrom escuro com moldura,
    /// rótulos bege-acinzentados e valores dourados. Reconstrói o texto no máximo 5x por segundo.
    /// </summary>
    internal sealed class Overlay
    {
        private const float RefreshSeconds = 0.2f;
        private const float Margin = 12f;

        private readonly Settings settings;
        private readonly BotController bot;
        private GUIStyle style;
        private GUIStyle buttonStyle;
        private Texture2D fillTex, borderTex, buttonTex, buttonHoverTex;
        private int styleFontSize = -1;
        private Font styleFont;
        private string cachedText = string.Empty;
        private float nextRefresh;

        public Overlay(Settings settings, BotController bot)
        {
            this.settings = settings;
            this.bot = bot;
        }

        /// <summary>Área ocupada pelo painel (GUI), para o clique nele não virar ataque no jogo.</summary>
        public Rect Rect { get; private set; }

        /// <summary>Chamado quando o jogador clica no botão de configurações do painel.</summary>
        public System.Action OnSettingsClicked;

        public void Draw()
        {
            if (!settings.ShowOverlay.Value || Event.current == null)
            {
                Rect = Rect.zero;
                return;
            }

            EnsureStyle();
            if (Event.current.type == EventType.Repaint && Time.unscaledTime >= nextRefresh)
            {
                nextRefresh = Time.unscaledTime + RefreshSeconds;
                cachedText = BuildText();
            }

            var content = new GUIContent(cachedText);
            float width = Mathf.Min(Screen.width * 0.4f, style.fontSize * 30f);
            float textHeight = style.CalcHeight(content, width);
            float buttonHeight = style.fontSize * 1.8f;
            float height = textHeight + buttonHeight + 10f;
            Rect = PlaceInCorner(width, height);

            var textRect = new Rect(Rect.x, Rect.y, width, textHeight);
            var buttonRect = new Rect(Rect.x + 10f, Rect.y + textHeight, Mathf.Min(width - 20f, style.fontSize * 15f), buttonHeight);

            if (Event.current.type == EventType.Repaint)
            {
                GameUiTheme.DrawFramedBox(Rect, fillTex, borderTex);
                GUI.Label(textRect, content, style);
            }

            string label = (GameApi.IsGameLanguagePortuguese() ? "Configurações (" : "Settings (") + settings.OpenSettingsKey.Value + ")";
            if (GUI.Button(buttonRect, label, buttonStyle))
            {
                OnSettingsClicked?.Invoke();
            }
        }

        private Rect PlaceInCorner(float w, float h)
        {
            switch (settings.OverlayPosition.Value)
            {
                case OverlayCorner.TopRight: return new Rect(Screen.width - w - Margin, Margin, w, h);
                case OverlayCorner.BottomLeft: return new Rect(Margin, Screen.height - h - Margin, w, h);
                case OverlayCorner.BottomRight: return new Rect(Screen.width - w - Margin, Screen.height - h - Margin, w, h);
                default: return new Rect(Margin, Margin, w, h);
            }
        }

        private string BuildText()
        {
            bool pt = GameApi.IsGameLanguagePortuguese();
            GameSnapshot s = bot.LastSnapshot ?? new GameSnapshot();
            var sb = new StringBuilder();

            sb.Append("<color=").Append(GameUiTheme.TitleHex).Append("><b>AutoKeeper ").Append(Plugin.Version).Append("</b></color>   ")
              .Append(StateColored(bot.State, pt));
            if (bot.State != BotController.BotState.Off && !string.IsNullOrEmpty(bot.StateDetail))
            {
                sb.Append(' ').Append(Muted("(" + bot.StateDetail + ")"));
            }
            sb.Append('\n');

            if (!s.InGame)
            {
                sb.Append(Label(pt ? "Menu / carregando" : "Menu / loading")).Append('\n');
            }
            else
            {
                sb.Append(Label(pt ? "Local: " : "Place: ")).Append(Value(string.IsNullOrEmpty(s.ZoneName) ? "?" : s.ZoneName))
                  .Append(Label("   " + (pt ? "Energia: " : "Energy: "))).Append(Value($"{s.Energy:0}/{s.EnergyMax:0}")).Append('\n');
                sb.Append(Label(pt ? "Controle: " : "Control: "))
                  .Append(s.BlockReason == null
                      ? "<color=" + GameUiTheme.GoodHex + ">" + (pt ? "livre" : "free") + "</color>"
                      : "<color=" + GameUiTheme.ValueHex + ">" + s.BlockReason + "</color>")
                  .Append('\n');
                sb.Append(Label(pt ? "Dia " : "Day ")).Append(Value(s.Day.ToString())).Append(Label("  ~")).Append(Value(s.ClockText))
                  .Append(Label(pt ? "   Carregando: " : "   Carrying: ")).Append(Value(s.Overhead.Count == 0 ? "-" : string.Join(", ", s.Overhead))).Append('\n');
                if (settings.OverlayDetailed.Value)
                {
                    sb.Append(Muted($"pos {s.Position.x:0.0}, {s.Position.y:0.0}, {s.Position.z:0.0} · {(pt ? "sanidade" : "sanity")} {s.Insanity:0} · {(pt ? "dinheiro" : "money")} {s.Money:0}")).Append('\n');
                    sb.Append(Muted($"zona {s.ZoneId ?? "-"} · cena {s.SceneId} · {(pt ? "jogo" : "game")} {s.GameVersion}")).Append('\n');
                }
            }

            sb.Append(Label(pt ? "Tarefa: " : "Task: ")).Append(Value(bot.CurrentTaskText)).Append('\n');
            sb.Append(Muted($"{settings.ToggleBotKey.Value} bot  ·  {settings.ToggleOverlayKey.Value} {(pt ? "painel" : "panel")}"));

            if (s.GameVersion != null && s.GameVersion != Plugin.TestedGameVersion)
            {
                sb.Append('\n').Append("<color=").Append(GameUiTheme.ValueHex).Append(">")
                  .Append(pt ? $"Jogo {s.GameVersion} não testado (testado: {Plugin.TestedGameVersion})" : $"Game {s.GameVersion} untested (tested: {Plugin.TestedGameVersion})")
                  .Append("</color>");
            }

            int n = settings.OverlayLogLines.Value;
            if (n > 0)
            {
                List<string> lines = ModLog.Recent.Reverse().Take(n).Reverse().ToList();
                if (lines.Count > 0)
                {
                    sb.Append("\n<size=").Append(Mathf.Max(10, style.fontSize - 3)).Append('>');
                    sb.Append(Muted(string.Join("\n", lines.Select(Escape))));
                    sb.Append("</size>");
                }
            }
            return sb.ToString();
        }

        private static string Label(string t) => "<color=" + GameUiTheme.LabelHex + ">" + t + "</color>";
        private static string Value(string t) => "<color=" + GameUiTheme.ValueHex + ">" + t + "</color>";
        private static string Muted(string t) => "<color=" + GameUiTheme.MutedHex + ">" + t + "</color>";

        private static string StateColored(BotController.BotState state, bool pt)
        {
            switch (state)
            {
                case BotController.BotState.Running: return "<color=" + GameUiTheme.GoodHex + "><b>" + (pt ? "TRABALHANDO" : "WORKING") + "</b></color>";
                case BotController.BotState.Idle: return "<color=" + GameUiTheme.GoodHex + ">" + (pt ? "LIGADO" : "ON") + "</color>";
                case BotController.BotState.Paused: return "<color=" + GameUiTheme.ValueHex + ">" + (pt ? "PAUSADO" : "PAUSED") + "</color>";
                default: return "<color=" + GameUiTheme.BadHex + ">" + (pt ? "DESLIGADO" : "OFF") + "</color>";
            }
        }

        /// <summary>Evita que "&lt;" em mensagens de log quebre o rich text.</summary>
        private static string Escape(string s) => s.Replace("<", "‹").Replace(">", "›");

        private void EnsureStyle()
        {
            int fontSize = Mathf.Clamp(Mathf.RoundToInt(Screen.height / 62f), 12, 22);
            Font gameFont = GameUiTheme.Font;
            if (style != null && styleFontSize == fontSize && styleFont == gameFont)
            {
                return;
            }
            styleFontSize = fontSize;
            styleFont = gameFont;

            fillTex = fillTex ?? GameUiTheme.MakeTex(GameUiTheme.PanelBackground);
            borderTex = borderTex ?? GameUiTheme.MakeTex(GameUiTheme.Border);
            buttonTex = buttonTex ?? GameUiTheme.MakeTex(GameUiTheme.Button);
            buttonHoverTex = buttonHoverTex ?? GameUiTheme.MakeTex(GameUiTheme.ButtonHover);

            style = new GUIStyle(GUI.skin.label)
            {
                richText = true,
                wordWrap = true,
                fontSize = fontSize,
                padding = new RectOffset(12, 12, 8, 6),
            };
            style.normal.textColor = GameUiTheme.Label;
            if (gameFont != null)
            {
                style.font = gameFont;
            }

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
    }
}
