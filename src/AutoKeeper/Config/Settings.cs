using BepInEx.Configuration;
using UnityEngine;

namespace AutoKeeper.Config
{
    /// <summary>
    /// Todas as opções do mod. Arquivo gerado em BepInEx/config/com.focabr.gk2.autokeeper.cfg.
    /// As mesmas seções/chaves serão reaproveitadas pela ponte opcional do GK2 Mod Framework.
    /// </summary>
    internal sealed class Settings
    {
        // [Hotkeys]
        public ConfigEntry<KeyboardShortcut> ToggleBotKey { get; }
        public ConfigEntry<KeyboardShortcut> ToggleOverlayKey { get; }
        public ConfigEntry<KeyboardShortcut> DumpKey { get; }

        // [Bot]
        public ConfigEntry<float> TickIntervalSeconds { get; }
        public ConfigEntry<float> MinEnergy { get; }

        // [Overlay]
        public ConfigEntry<bool> ShowOverlay { get; }
        public ConfigEntry<int> OverlayLogLines { get; }

        // [Safety]
        public ConfigEntry<bool> AllowCheats { get; }

        // [Debug]
        public ConfigEntry<bool> VerboseLogging { get; }

        public Settings(ConfigFile config)
        {
            ToggleBotKey = config.Bind("Hotkeys", "ToggleBot", new KeyboardShortcut(KeyCode.F8),
                "Liga/desliga o bot (kill switch).");
            ToggleOverlayKey = config.Bind("Hotkeys", "ToggleOverlay", new KeyboardShortcut(KeyCode.F9),
                "Mostra/esconde o overlay de status.");
            DumpKey = config.Bind("Hotkeys", "DiscoveryDump", new KeyboardShortcut(KeyCode.F10),
                "Salva um JSON (somente leitura) com objetos/itens da cena atual em BepInEx/config/AutoKeeper/dumps.");

            TickIntervalSeconds = config.Bind("Bot", "TickIntervalSeconds", 0.25f,
                new ConfigDescription("Intervalo entre decisões do bot, em segundos (tempo real).",
                    new AcceptableValueRange<float>(0.05f, 5f)));
            MinEnergy = config.Bind("Bot", "MinEnergy", 10f,
                new ConfigDescription("O bot para sozinho quando a energia do jogador fica abaixo deste valor.",
                    new AcceptableValueRange<float>(0f, 1000f)));

            ShowOverlay = config.Bind("Overlay", "ShowOverlay", true,
                "Mostrar o overlay de status na tela.");
            OverlayLogLines = config.Bind("Overlay", "LogLines", 6,
                new ConfigDescription("Quantas linhas recentes de log aparecem no overlay.",
                    new AcceptableValueRange<int>(0, 20)));

            AllowCheats = config.Bind("Safety", "AllowCheats", false,
                "Reservado. O bot só executa ações que o jogador poderia fazer; nenhuma função de cheat existe hoje.");

            VerboseLogging = config.Bind("Debug", "VerboseLogging", false,
                "Log detalhado (nível Debug) no BepInEx/LogOutput.log.");
        }
    }
}
