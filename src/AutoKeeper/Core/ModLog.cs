using System;
using System.Collections.Generic;
using AutoKeeper.Config;
using BepInEx.Logging;

namespace AutoKeeper.Core
{
    /// <summary>A message for the status panel's event list.</summary>
    internal readonly struct LogEntry
    {
        public readonly float At;          // Time.unscaledTime of the latest occurrence
        public readonly string Clock;      // game clock ("HH:MM") of the latest occurrence; null outside a loaded game
        public readonly LogLevel Level;
        public readonly string Text;
        public readonly int Count;         // identical consecutive messages merged into this entry (1 = single)

        public LogEntry(float at, string clock, LogLevel level, string text, int count = 1)
        {
            At = at;
            Clock = clock;
            Level = level;
            Text = text;
            Count = count;
        }

        /// <summary>The same message again: one more repeat, time of the latest occurrence.</summary>
        public LogEntry Repeated(float at, string clock) => new LogEntry(at, clock, Level, Text, Count + 1);
    }

    /// <summary>
    /// Central log: writes to the BepInEx Logger and keeps the latest messages for the status panel.
    /// Debug messages only appear with [Debug] VerboseLogging = true. <see cref="Detail"/> goes only to the file.
    /// </summary>
    internal static class ModLog
    {
        private const int MaxRecent = 50;

        private static ManualLogSource source;
        private static Settings settings;
        private static readonly LinkedList<LogEntry> recent = new LinkedList<LogEntry>();
        private static readonly HashSet<string> onceKeys = new HashSet<string>();

        /// <summary>Events for the status panel, from oldest to newest.</summary>
        public static IEnumerable<LogEntry> Recent => recent;

        public static void Init(ManualLogSource logSource, Settings modSettings)
        {
            source = logSource;
            settings = modSettings;
        }

        public static void Debug(string msg)
        {
            if (settings != null && settings.VerboseLogging.Value)
            {
                Write(LogLevel.Debug, msg);
            }
        }

        public static void Info(string msg) => Write(LogLevel.Info, msg);

        /// <summary>Technical/step-by-step info: goes to LogOutput.log, but not to the status panel's event list.</summary>
        public static void Detail(string msg) => source?.Log(LogLevel.Info, msg);
        public static void Warn(string msg) => Write(LogLevel.Warning, msg);
        public static void Error(string msg) => Write(LogLevel.Error, msg);

        /// <summary>Logs only the first time for the same key (avoids spam every frame).</summary>
        public static void WarnOnce(string key, string msg)
        {
            if (onceKeys.Add(key))
            {
                Warn(msg);
            }
        }

        /// <summary>Lets the key's "once" warning appear again (e.g. when the bot is turned back on).</summary>
        public static void ResetOnce(string key) => onceKeys.Remove(key);

        private static void Write(LogLevel level, string msg)
        {
            if (level == LogLevel.Debug)
            {
                // BepInEx's default LogOutput.log filter drops Debug: writes it as tagged Info, only to the file.
                source?.Log(LogLevel.Info, "[dbg] " + msg);
                return;
            }
            source?.Log(level, msg);
            float now = UnityEngine.Time.unscaledTime;
            string clock = GameApi.GetClockText();
            LinkedListNode<LogEntry> last = recent.Last;
            if (last != null && last.Value.Level == level && last.Value.Text == msg)
            {
                // Same message again in a row: the panel shows one line with "×N" (the file keeps every line).
                last.Value = last.Value.Repeated(now, clock);
                return;
            }
            recent.AddLast(new LogEntry(now, clock, level, msg));
            while (recent.Count > MaxRecent)
            {
                recent.RemoveFirst();
            }
        }
    }
}
