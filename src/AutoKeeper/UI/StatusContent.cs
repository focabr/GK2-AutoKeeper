using System.Collections.Generic;
using System.Linq;
using AutoKeeper.Bot;
using AutoKeeper.Config;
using AutoKeeper.Core;
using BepInEx.Logging;
using UnityEngine;

namespace AutoKeeper.UI
{
    /// <summary>
    /// What the status panel shows, as rich text (the same tags work in IMGUI and TextMeshPro). Built by one place and
    /// drawn by either the game-styled panel (<see cref="NativeStatusPanel"/>) or the simple one (<see cref="Overlay"/>).
    /// </summary>
    internal sealed class StatusContent
    {
        internal struct Pair
        {
            public string Label;
            public string Value;
            public bool Muted;     // technical rows: smaller, label color
            public bool Session;   // the one-line session summary (its own section on the game-styled panel)
        }

        internal struct EventLine
        {
            public string Clock;   // game clock "HH:MM", or "—" outside a loaded game
            public string Text;    // colored by level, "×N" for repeats
        }

        public string Title;
        public string State;       // colored state word
        public string Reason;      // why it is off/paused/idle; null when there is nothing to say
        public string Note;        // untested game version, or null
        public readonly List<Pair> Rows = new List<Pair>();
        public readonly List<EventLine> Events = new List<EventLine>();

        public static StatusContent Build(Settings settings, BotController bot, bool pt)
        {
            var c = new StatusContent();
            GameSnapshot s = bot.LastSnapshot ?? new GameSnapshot();
            c.Title = $"{Plugin.Name} {Plugin.Version}";
            c.State = StateColored(bot.State, pt);
            string d = bot.StateDetail;
            // Running: the task line says it all. Off with no detail = initial state (the keys say how to turn it on).
            c.Reason = bot.State == BotController.BotState.Running || string.IsNullOrEmpty(d) ? null : d;

            string step = bot.CurrentStepText;
            c.Add(pt ? "Tarefa:" : "Task:", string.IsNullOrEmpty(step) ? "—" : step);
            if (!s.InGame)
            {
                c.Add(pt ? "Local:" : "Place:", pt ? "menu / carregando" : "menu / loading");
            }
            else
            {
                string place = string.IsNullOrEmpty(s.ZoneName) ? "?" : s.ZoneName;
                c.Add(pt ? "Local:" : "Place:", $"{place} · {(pt ? "dia" : "day")} {s.Day}, {s.ClockText}");

                bool lowEnergy = s.EnergyMax > 0f && s.Energy < settings.EatBelowEnergy.Value;
                bool highInsanity = s.Insanity >= settings.MaxInsanity.Value * 0.8f;
                c.Add(pt ? "Energia:" : "Energy:",
                    Warn($"{s.Energy:0}/{s.EnergyMax:0}", lowEnergy) + Muted(" · ") + Warn($"{(pt ? "insanidade" : "insanity")} {s.Insanity:0}", highInsanity));

                if (s.DaysWithoutSleep >= 0f)
                {
                    string days = s.DaysWithoutSleep.ToString("0.0");
                    string sleep = pt ? $"{days} dia(s) sem dormir" : $"{days} day(s) without sleep";
                    sleep = s.LackOfSleep
                        ? Bad(sleep + (pt ? " — Privação de Sono" : " — Lack of sleep"))
                        : Warn(sleep, s.DaysWithoutSleep >= 1.75f);
                    c.Add(pt ? "Sono:" : "Sleep:", sleep);
                }
                string session = SessionLine(pt);
                if (session != null)
                {
                    c.Rows.Add(new Pair { Label = pt ? "Sessão:" : "Session:", Value = session, Session = true });
                }
                if (s.Overhead.Count > 0)
                {
                    c.Add(pt ? "Carregando:" : "Carrying:", string.Join(", ", s.Overhead.Select(id => ItemName(id, pt))));
                }
                if (settings.OverlayDetailed.Value)
                {
                    c.AddMuted(pt ? "Posição:" : "Position:", $"{s.Position.x:0.0}, {s.Position.y:0.0}, {s.Position.z:0.0}");
                    c.AddMuted(pt ? "Zona:" : "Zone:", $"{s.ZoneId ?? "-"} · {(pt ? "cena" : "scene")} {s.SceneId}");
                    c.AddMuted(pt ? "Dinheiro:" : "Money:", $"{s.Money:0}");
                }
            }
            if (s.GameVersion != null && s.GameVersion != Plugin.TestedGameVersion)
            {
                c.Note = Warn(pt ? $"Jogo {s.GameVersion} não testado (testado: {Plugin.TestedGameVersion})"
                                 : $"Game {s.GameVersion} untested (tested: {Plugin.TestedGameVersion})", true);
            }

            // Events: the last N, oldest first (reads top to bottom like a chat; the newest is at the bottom).
            int n = settings.OverlayLogLines.Value;
            if (n > 0)
            {
                List<LogEntry> all = ModLog.Recent.ToList();
                foreach (LogEntry e in all.Skip(Mathf.Max(0, all.Count - n)))
                {
                    c.Events.Add(new EventLine { Clock = e.Clock ?? "—", Text = EventText(e) });
                }
            }
            return c;
        }

        private void Add(string label, string value) => Rows.Add(new Pair { Label = label, Value = value });

        private void AddMuted(string label, string value) => Rows.Add(new Pair { Label = label, Value = value, Muted = true });

        private static string EventText(LogEntry e)
        {
            string t = Escape(e.Display);
            switch (e.Level)
            {
                case LogLevel.Warning: t = Warn(t, true); break;
                case LogLevel.Error: t = Bad(t); break;
            }
            return e.Count > 1 ? t + Muted($" ×{e.Count}") : t;
        }

        /// <summary>
        /// One line with what the bot did since the save was loaded (0.3.36, user's request: after 5 h away he wanted to
        /// know how many bodies were done). Null before the bot has run.
        /// </summary>
        private static string SessionLine(bool pt)
        {
            if (SessionStats.OnSeconds < 1f && SessionStats.Bodies == 0)
            {
                return null;
            }
            int mins = Mathf.FloorToInt(SessionStats.OnSeconds / 60f);
            string time = mins >= 60 ? $"{mins / 60}h{mins % 60:00}" : $"{mins} min";
            int b = SessionStats.Bodies;
            var parts = new List<string>
            {
                pt ? (b == 1 ? "1 corpo" : $"{b} corpos") : (b == 1 ? "1 body" : $"{b} bodies"),
            };
            if (SessionStats.Sleeps > 0)
            {
                parts.Add(pt ? $"dormiu {SessionStats.Sleeps}×" : $"slept {SessionStats.Sleeps}×");
            }
            parts.Add(pt ? $"{time} ligado" : $"on for {time}");
            return string.Join(Muted(" · "), parts);
        }

        private static string ItemName(string id, bool pt) => id != null && id.StartsWith("body") ? (pt ? "corpo" : "body") : id;

        public static string Muted(string t) => "<color=" + GameUiTheme.MutedHex + ">" + t + "</color>";
        public static string Bad(string t) => "<color=" + GameUiTheme.BadHex + ">" + t + "</color>";
        public static string Warn(string t, bool warn) => warn ? Bad(t) : t;
        public static string Color(string hex, string t) => "<color=" + hex + ">" + t + "</color>";

        public static string StateColored(BotController.BotState state, bool pt)
        {
            switch (state)
            {
                case BotController.BotState.Running: return "<color=" + GameUiTheme.GoodHex + "><b>" + (pt ? "TRABALHANDO" : "WORKING") + "</b></color>";
                case BotController.BotState.Idle: return "<color=" + GameUiTheme.GoodHex + ">" + (pt ? "AGUARDANDO" : "WAITING") + "</color>";
                case BotController.BotState.Paused: return "<color=" + GameUiTheme.ValueHex + ">" + (pt ? "PAUSADO" : "PAUSED") + "</color>";
                default: return "<color=" + GameUiTheme.BadHex + ">" + (pt ? "DESLIGADO" : "OFF") + "</color>";
            }
        }

        /// <summary>Keeps "&lt;" in log messages from breaking the rich text.</summary>
        public static string Escape(string s) => s.Replace("<", "‹").Replace(">", "›");
    }
}
