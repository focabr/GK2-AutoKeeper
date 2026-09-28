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
    /// Overlay IMGUI (OnGUI) com status do bot, estado do jogo e últimas linhas de log.
    /// Só desenha no evento Repaint e reconstrói o texto no máximo 5x por segundo.
    /// </summary>
    internal sealed class Overlay
    {
        private const float RefreshSeconds = 0.2f;

        private readonly Settings settings;
        private readonly BotController bot;
        private GUIStyle style;
        private int styleFontSize;
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
            float width = Mathf.Min(Screen.width * 0.45f, style.fontSize * 38f);
            float height = style.CalcHeight(content, width);
            float buttonHeight = style.fontSize * 1.9f;
            var textRect = new Rect(10f, 10f, width, height);
            var buttonRect = new Rect(18f, 10f + height, Mathf.Min(width - 16f, style.fontSize * 16f), buttonHeight);
            Rect = new Rect(10f, 10f, width, height + buttonHeight + 8f);

            if (Event.current.type == EventType.Repaint)
            {
                Color old = GUI.color;
                GUI.color = new Color(0f, 0f, 0f, 0.6f);
                GUI.DrawTexture(Rect, Texture2D.whiteTexture);
                GUI.color = old;
                GUI.Label(textRect, content, style);
            }

            string label = (GameApi.IsGameLanguagePortuguese() ? "Configurações (" : "Settings (") + settings.OpenSettingsKey.Value + ")";
            if (GUI.Button(buttonRect, label, buttonStyle))
            {
                OnSettingsClicked?.Invoke();
            }
        }

        private string BuildText()
        {
            GameSnapshot s = bot.LastSnapshot ?? new GameSnapshot();
            var sb = new StringBuilder();

            sb.Append("<b>").Append(Plugin.Name).Append(' ').Append(Plugin.Version).Append("</b>   bot: ")
              .Append(StateColored(bot.State)).Append("  <i>(").Append(bot.StateDetail).Append(")</i>\n");

            string ver = s.GameVersion ?? "?";
            string verColor = ver == Plugin.TestedGameVersion ? "#9f9" : "#fc6";
            sb.Append("Jogo <color=").Append(verColor).Append('>').Append(ver).Append("</color>");

            if (!s.InGame)
            {
                sb.Append("  |  menu / carregando\n");
            }
            else
            {
                sb.Append("  |  local: ").Append(string.IsNullOrEmpty(s.ZoneName) ? "?" : s.ZoneName)
                  .Append(" <color=#aaa>(").Append(s.ZoneId ?? "-").Append(" · cena ").Append(s.SceneId).Append(")</color>\n");
                sb.Append("Controle: ").Append(s.BlockReason == null ? "<color=#9f9>livre</color>" : "<color=#fc6>" + s.BlockReason + "</color>").Append('\n');
                sb.AppendFormat("Pos: {0:0.0}, {1:0.0}, {2:0.0}\n", s.Position.x, s.Position.y, s.Position.z);
                sb.AppendFormat("Energia: {0:0}/{1:0}   Sanidade(insanity): {2:0}   Dinheiro: {3:0}\n", s.Energy, s.EnergyMax, s.Insanity, s.Money);
                sb.AppendFormat("Dia {0}  ~{1}  (timeOfDay {2:0.000})\n", s.Day, s.ClockText, s.TimeOfDay);
                sb.Append("Carregando: ").Append(s.Overhead.Count == 0 ? "-" : string.Join(", ", s.Overhead)).Append('\n');
            }

            sb.Append("Tarefa: ").Append(bot.CurrentTaskText).Append('\n');
            sb.Append("<size=").Append(Mathf.Max(10, style.fontSize - 2)).Append("><color=#aaa>")
              .Append(settings.ToggleBotKey.Value).Append(" bot  ·  ")
              .Append(settings.ToggleOverlayKey.Value).Append(" overlay  ·  ")
              .Append(settings.OpenSettingsKey.Value).Append(" config  ·  ")
              .Append(settings.DumpKey.Value).Append(" dump</color></size>");

            int n = settings.OverlayLogLines.Value;
            if (n > 0)
            {
                List<string> lines = ModLog.Recent.Reverse().Take(n).Reverse().ToList();
                if (lines.Count > 0)
                {
                    sb.Append("\n<size=").Append(Mathf.Max(10, style.fontSize - 3)).Append("><color=#ccc>");
                    sb.Append(string.Join("\n", lines.Select(Escape)));
                    sb.Append("</color></size>");
                }
            }
            return sb.ToString();
        }

        private static string StateColored(BotController.BotState state)
        {
            switch (state)
            {
                case BotController.BotState.Running: return "<color=#6f6><b>RODANDO</b></color>";
                case BotController.BotState.Idle: return "<color=#9cf>LIGADO</color>";
                case BotController.BotState.Paused: return "<color=#fc6>PAUSADO</color>";
                default: return "<color=#f88>DESLIGADO</color>";
            }
        }

        /// <summary>Evita que "&lt;" em mensagens de log quebre o rich text.</summary>
        private static string Escape(string s) => s.Replace("<", "‹").Replace(">", "›");

        private GUIStyle buttonStyle;

        private void EnsureStyle()
        {
            int fontSize = Mathf.Clamp(Screen.height / 60, 12, 24);
            if (style != null && styleFontSize == fontSize)
            {
                return;
            }
            styleFontSize = fontSize;
            style = new GUIStyle(GUI.skin.label)
            {
                richText = true,
                wordWrap = true,
                fontSize = fontSize,
                padding = new RectOffset(8, 8, 6, 6),
            };
            style.normal.textColor = Color.white;
            buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = Mathf.Max(11, fontSize - 2) };
        }
    }
}
