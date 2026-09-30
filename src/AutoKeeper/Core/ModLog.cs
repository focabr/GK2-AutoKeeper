using System;
using System.Collections.Generic;
using AutoKeeper.Config;
using BepInEx.Logging;

namespace AutoKeeper.Core
{
    /// <summary>Uma mensagem para a lista de eventos do painel.</summary>
    internal readonly struct LogEntry
    {
        public readonly float At;          // Time.unscaledTime (para "há X s")
        public readonly LogLevel Level;
        public readonly string Text;

        public LogEntry(float at, LogLevel level, string text)
        {
            At = at;
            Level = level;
            Text = text;
        }
    }

    /// <summary>
    /// Log central: escreve no Logger do BepInEx e guarda as últimas mensagens para o painel.
    /// Mensagens Debug só aparecem com [Debug] VerboseLogging = true. <see cref="Detail"/> vai só para o arquivo.
    /// </summary>
    internal static class ModLog
    {
        private const int MaxRecent = 50;

        private static ManualLogSource source;
        private static Settings settings;
        private static readonly LinkedList<LogEntry> recent = new LinkedList<LogEntry>();
        private static readonly HashSet<string> onceKeys = new HashSet<string>();

        /// <summary>Eventos para o painel, do mais antigo para o mais novo.</summary>
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

        /// <summary>Informação técnica/passo a passo: vai para o LogOutput.log, mas não para a lista de eventos do painel.</summary>
        public static void Detail(string msg) => source?.Log(LogLevel.Info, msg);
        public static void Warn(string msg) => Write(LogLevel.Warning, msg);
        public static void Error(string msg) => Write(LogLevel.Error, msg);

        /// <summary>Loga apenas na primeira vez para a mesma chave (evita spam a cada frame).</summary>
        public static void WarnOnce(string key, string msg)
        {
            if (onceKeys.Add(key))
            {
                Warn(msg);
            }
        }

        /// <summary>Permite que o aviso "uma vez" da chave volte a aparecer (ex.: ao religar o bot).</summary>
        public static void ResetOnce(string key) => onceKeys.Remove(key);

        private static void Write(LogLevel level, string msg)
        {
            if (level == LogLevel.Debug)
            {
                // O filtro padrão do LogOutput.log do BepInEx descarta Debug: grava como Info marcado, só no arquivo.
                source?.Log(LogLevel.Info, "[dbg] " + msg);
                return;
            }
            source?.Log(level, msg);
            recent.AddLast(new LogEntry(UnityEngine.Time.unscaledTime, level, msg));
            while (recent.Count > MaxRecent)
            {
                recent.RemoveFirst();
            }
        }
    }
}
