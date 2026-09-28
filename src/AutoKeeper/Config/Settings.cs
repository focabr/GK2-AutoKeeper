using BepInEx.Configuration;
using UnityEngine;

namespace AutoKeeper.Config
{
    /// <summary>Para onde o corpo vai depois da autópsia.</summary>
    public enum BodyDestination
    {
        Crematorium,
        LeaveOnTable,
        Grave,
    }

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

        public ConfigEntry<float> MoveTimeoutSeconds { get; }
        public ConfigEntry<float> WorkStallSeconds { get; }

        // [Bodies]
        public ConfigEntry<bool> BodiesEnabled { get; }
        public ConfigEntry<string> ExtractOrgans { get; }
        public ConfigEntry<BodyDestination> Destination { get; }
        public ConfigEntry<string> GraveCraftId { get; }
        public ConfigEntry<float> SearchRadius { get; }

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

            MoveTimeoutSeconds = config.Bind("Bot", "MoveTimeoutSeconds", 45f,
                new ConfigDescription("Tempo máximo andando até um alvo antes de desistir.",
                    new AcceptableValueRange<float>(5f, 300f)));
            WorkStallSeconds = config.Bind("Bot", "WorkStallSeconds", 20f,
                new ConfigDescription("Se o progresso de uma receita não mudar por este tempo segurando a ação, o bot para.",
                    new AcceptableValueRange<float>(5f, 300f)));

            BodiesEnabled = config.Bind("Bodies", "Enabled", true,
                "Rotina 'processar corpos': palete/chão -> mesa de autópsia -> extrair órgãos -> destino.");
            ExtractOrgans = config.Bind("Bodies", "ExtractOrgans", "all",
                "Órgãos a extrair: 'all', 'none' ou lista separada por vírgula de tipos (Bones, Brain, Heart, Guts, Skin, Skull) ou ids de item.");
            Destination = config.Bind("Bodies", "Destination", BodyDestination.Crematorium,
                "Destino do corpo após a autópsia: Crematorium (crematório do necrotério), LeaveOnTable (deixar na mesa) ou Grave (EXPERIMENTAL: cova vazia 'grave_empty' na mesma área; ainda não atravessa portas).");
            GraveCraftId = config.Bind("Bodies", "GraveCraftId", "",
                "Opcional: id da receita de enterro em grave_empty. Vazio = detectar a receita que exige um corpo.");
            SearchRadius = config.Bind("Bodies", "SearchRadius", 80f,
                new ConfigDescription("Distância máxima (m) para procurar corpos, mesas e covas na cena atual.",
                    new AcceptableValueRange<float>(5f, 500f)));

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
