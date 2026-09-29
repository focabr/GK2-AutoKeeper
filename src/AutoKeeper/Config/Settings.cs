using System;
using System.Collections.Generic;
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

    /// <summary>Canto da tela onde fica o painel de status.</summary>
    public enum OverlayCorner
    {
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight,
    }

    /// <summary>Abas da tela de configurações (e seções no menu Mods do GK2 Mod Framework).</summary>
    public enum SettingTab
    {
        Bot,
        Bodies,
        Autopsy,
        AutopsyOthers,
        Hotkeys,
        Overlay,
        Advanced,
    }

    /// <summary>
    /// Metadados de UI de uma opção: rótulo/ajuda em PT e EN, aba, faixa do slider.
    /// É a ÚNICA fonte usada tanto pela janela própria (UI/SettingsWindow) quanto pela ponte do
    /// GK2 Mod Framework — assim as duas telas mostram sempre as mesmas opções.
    /// </summary>
    public sealed class SettingInfo
    {
        internal SettingInfo(ConfigEntryBase entry, SettingTab tab, int order, string labelPt, string labelEn, string helpPt, string helpEn)
        {
            Entry = entry;
            Tab = tab;
            Order = order;
            LabelPt = labelPt;
            LabelEn = labelEn;
            HelpPt = helpPt;
            HelpEn = helpEn;
        }

        public ConfigEntryBase Entry { get; }
        public SettingTab Tab { get; }
        public int Order { get; }
        public string LabelPt { get; }
        public string LabelEn { get; }
        public string HelpPt { get; }
        public string HelpEn { get; }

        /// <summary>Faixa e passo para sliders (float/int).</summary>
        public float Min { get; internal set; }
        public float Max { get; internal set; }
        public float Step { get; internal set; } = 1f;

        public string Label(bool pt) => pt ? LabelPt : LabelEn;
        public string Help(bool pt) => pt ? HelpPt : HelpEn;
    }

    /// <summary>
    /// Todas as opções do mod. Persistidas em BepInEx/config/com.focabr.gk2.autokeeper.cfg (padrão BepInEx,
    /// compatível com mod managers), mas o jogador edita pela tela de configurações (F11) ou pelo menu Mods.
    /// </summary>
    public sealed class Settings
    {
        private readonly List<SettingInfo> ui = new List<SettingInfo>();
        private int order;

        // [Hotkeys]
        public ConfigEntry<KeyboardShortcut> ToggleBotKey { get; }
        public ConfigEntry<KeyboardShortcut> ToggleOverlayKey { get; }
        public ConfigEntry<KeyboardShortcut> OpenSettingsKey { get; }
        public ConfigEntry<KeyboardShortcut> DumpKey { get; }

        // [Bot]
        public ConfigEntry<float> MinEnergy { get; }
        public ConfigEntry<float> TickIntervalSeconds { get; }
        public ConfigEntry<float> MoveTimeoutSeconds { get; }
        public ConfigEntry<float> WorkStallSeconds { get; }
        public ConfigEntry<bool> TravelEnabled { get; }
        public ConfigEntry<bool> AutoEat { get; }
        public ConfigEntry<float> EatBelowEnergy { get; }

        // [Bodies]
        public ConfigEntry<bool> BodiesEnabled { get; }
        public ConfigEntry<BodyDestination> Destination { get; }
        public ConfigEntry<bool> RequireMastery { get; }
        public ConfigEntry<int> MinMasteryChance { get; }
        public ConfigEntry<bool> ExtractSkin { get; }
        public ConfigEntry<bool> ExtractBones { get; }
        public ConfigEntry<bool> ExtractSkull { get; }
        public ConfigEntry<bool> ExtractHeart { get; }
        public ConfigEntry<bool> ExtractBrain { get; }
        public ConfigEntry<bool> ExtractGuts { get; }
        public ConfigEntry<bool> ExtractFlesh { get; }
        public ConfigEntry<bool> ExtractFat { get; }
        public ConfigEntry<bool> ExtractBlood { get; }
        public ConfigEntry<bool> ExtractOtherPocket { get; }
        public ConfigEntry<float> SearchRadius { get; }
        public ConfigEntry<bool> CheckCrematoriumFirst { get; }
        public ConfigEntry<bool> UseChest { get; }
        public ConfigEntry<int> ChestFreeSlots { get; }
        public ConfigEntry<string> GraveCraftId { get; }

        // [Overlay]
        public ConfigEntry<bool> ShowOverlay { get; }
        public ConfigEntry<int> OverlayLogLines { get; }
        public ConfigEntry<OverlayCorner> OverlayPosition { get; }
        public ConfigEntry<bool> OverlayDetailed { get; }

        // [Safety] (reservado, fora da UI)
        public ConfigEntry<bool> AllowCheats { get; }

        // [Debug]
        public ConfigEntry<bool> VerboseLogging { get; }

        /// <summary>Opções visíveis nas telas de configuração, na ordem de exibição.</summary>
        public IReadOnlyList<SettingInfo> UiSettings => ui;

        public Settings(ConfigFile config)
        {
            // ---------------------------------------------------------------- Avançado
            AutoEat = Toggle(config, SettingTab.Bot, "Bot", "AutoEat", true,
                "Comer da barra rápida", "Eat from the hotbar",
                "Com energia baixa, usa um item que recupera energia da barra rápida (teclas 1–4), como o jogador faria. Pula itens que aumentam a insanidade.",
                "When energy is low, uses an energy item from the hotbar (keys 1–4), like the player would. Skips items that raise insanity.");
            EatBelowEnergy = Slider(config, SettingTab.Bot, "Bot", "EatBelowEnergy", 20f, 1f, 100f, 1f,
                "Comer quando a energia estiver abaixo de", "Eat when energy is below",
                "Come quando a energia fica abaixo deste valor. Deixe maior que o valor de desligar o bot.",
                "Eats when energy drops below this value. Keep it above the turn-off value.");
            MinEnergy = Slider(config, SettingTab.Bot, "Bot", "MinEnergy", 10f, 0f, 100f, 1f,
                "Desligar o bot com energia abaixo de", "Turn bot off below energy",
                "O bot desliga sozinho quando a energia fica abaixo deste valor (e não há comida na barra rápida).",
                "The bot turns itself off when energy drops below this value (and there is no food in the hotbar).");
            TravelEnabled = Toggle(config, SettingTab.Bot, "Bot", "UseDoors", true,
                "Atravessar portas até o trabalho", "Use doors to reach the work",
                "Atravessa portas (casa, necrotério…) pelo caminho mais curto até onde há trabalho, apertando E na porta como o jogador.",
                "Goes through doors (house, morgue…) along the shortest route to where there is work, pressing E on the door like the player.");
            // ---------------------------------------------------------------- Avançado
            BodiesEnabled = Toggle(config, SettingTab.Bodies, "Bodies", "Enabled", true,
                "Processar corpos", "Process bodies",
                "Palete → mesa de autópsia → extrair órgãos → destino.",
                "Pallet → autopsy table → extract organs → destination.");
            Destination = Bind(config, SettingTab.Bodies, "Bodies", "Destination", BodyDestination.Crematorium,
                "Destino do corpo depois da autópsia", "Body destination after autopsy",
                "Crematório (no necrotério), deixar na mesa, ou cova vazia (experimental, mesma área).",
                "Crematorium (inside the morgue), leave on the table, or empty grave (experimental, same area).");
            SearchRadius = Slider(config, SettingTab.Bodies, "Bodies", "SearchRadius", 80f, 5f, 300f, 5f,
                "Buscar corpos no chão até (m)", "Search ground bodies up to (m)",
                "Distância máxima para pegar corpos soltos no chão. Paletes, mesas e crematório são achados em qualquer lugar alcançável.",
                "Maximum distance to pick up loose bodies from the ground. Pallets, tables and crematorium are found anywhere reachable.");
            CheckCrematoriumFirst = Toggle(config, SettingTab.Bodies, "Bodies", "CheckCrematoriumFirst", true,
                "Checar o crematório primeiro", "Check crematorium first",
                "Ao chegar no necrotério, o bot vai ao crematório antes de começar e recolhe o que estiver pronto.",
                "On arriving at the morgue, the bot visits the crematorium before starting and collects anything ready.");
            UseChest = Toggle(config, SettingTab.Bodies, "Bodies", "UseChest", true,
                "Guardar no baú com inventário cheio", "Store in chest when inventory is full",
                "Com o inventário quase cheio, leva ao baú mais próximo SÓ o que o bot recolheu (extrações e crematório). O resto do inventário nunca é mexido.",
                "When the inventory is nearly full, moves ONLY what the bot collected (extractions and crematorium) to the nearest chest. The rest of your inventory is never touched.");
            ChestFreeSlots = SliderInt(config, SettingTab.Bodies, "Bodies", "ChestFreeSlots", 3, 1, 10,
                "Ir ao baú com menos de (espaços livres)", "Go to chest below (free slots)",
                "Vai ao baú quando sobrarem menos espaços livres que isso no inventário.",
                "Goes to the chest when fewer free inventory slots than this remain.");

            RequireMastery = Toggle(config, SettingTab.Autopsy, "Bodies", "RequireMastery", true,
                "Respeitar a maestria", "Respect mastery",
                "Vale por cima das opções abaixo: confere a maestria item por item (como na janela \"Remover …\") e pula o que ficar abaixo da chance mínima.",
                "Overrides the options below: checks mastery item by item (like the \"Remove …\" window) and skips anything below the minimum chance.");
            MinMasteryChance = SliderInt(config, SettingTab.Autopsy, "Bodies", "MinMasteryChance", 100, 1, 100,
                "Chance mínima de sucesso (%)", "Minimum success chance (%)",
                "100 = só com maestria total (sem %). Ex.: 60 aceita o cérebro a 62% mas pula as entranhas a 38%.",
                "100 = full mastery only (no %). E.g. 60 accepts the brain at 62% but skips guts at 38%.");
            ExtractSkin = Toggle(config, SettingTab.Autopsy, "Bodies", "ExtractSkin", true, "Extrair pele", "Extract skin",
                "Extrai a pele na autópsia.", "Extract skin during autopsy.");
            ExtractBones = Toggle(config, SettingTab.Autopsy, "Bodies", "ExtractBones", true, "Extrair ossos", "Extract bones",
                "Extrai os ossos na autópsia.", "Extract bones during autopsy.");
            ExtractSkull = Toggle(config, SettingTab.Autopsy, "Bodies", "ExtractSkull", true, "Extrair crânio", "Extract skull",
                "Extrai o crânio na autópsia.", "Extract the skull during autopsy.");
            ExtractHeart = Toggle(config, SettingTab.Autopsy, "Bodies", "ExtractHeart", true, "Extrair coração", "Extract heart",
                "Extrai o coração na autópsia.", "Extract the heart during autopsy.");
            ExtractBrain = Toggle(config, SettingTab.Autopsy, "Bodies", "ExtractBrain", true, "Extrair cérebro", "Extract brain",
                "Extrai o cérebro na autópsia.", "Extract the brain during autopsy.");
            ExtractGuts = Toggle(config, SettingTab.Autopsy, "Bodies", "ExtractGuts", true, "Extrair vísceras", "Extract guts",
                "Extrai as vísceras na autópsia.", "Extract guts during autopsy.");
            ExtractFlesh = Toggle(config, SettingTab.AutopsyOthers, "Bodies", "ExtractFlesh", true, "Extrair carne", "Extract flesh",
                "Tira a carne (seção \"Outros\" da mesa de autópsia).", "Takes out the flesh (\"Others\" section of the autopsy table).");
            ExtractFat = Toggle(config, SettingTab.AutopsyOthers, "Bodies", "ExtractFat", true, "Extrair gordura", "Extract fat",
                "Tira a gordura (seção \"Outros\").", "Takes out the fat (\"Others\" section).");
            ExtractBlood = Toggle(config, SettingTab.AutopsyOthers, "Bodies", "ExtractBlood", true, "Extrair sangue", "Extract blood",
                "Tira o sangue (seção \"Outros\").", "Takes out the blood (\"Others\" section).");
            ExtractOtherPocket = Toggle(config, SettingTab.AutopsyOthers, "Bodies", "ExtractOtherPocket", false, "Extrair demais itens", "Extract remaining items",
                "Tira qualquer outro item da seção \"Outros\" que não seja carne, gordura ou sangue.",
                "Takes out any other item of the \"Others\" section that is not flesh, fat or blood.");
            // ---------------------------------------------------------------- Avançado
            ToggleBotKey = Bind(config, SettingTab.Hotkeys, "Hotkeys", "ToggleBot", new KeyboardShortcut(KeyCode.F8),
                "Ligar/desligar o bot", "Toggle bot",
                "Kill switch: liga ou desliga o bot na hora.", "Kill switch: turns the bot on or off immediately.");
            OpenSettingsKey = Bind(config, SettingTab.Hotkeys, "Hotkeys", "OpenSettings", new KeyboardShortcut(KeyCode.F11),
                "Abrir configurações", "Open settings",
                "Abre/fecha esta tela.", "Opens/closes this window.");
            ToggleOverlayKey = Bind(config, SettingTab.Hotkeys, "Hotkeys", "ToggleOverlay", new KeyboardShortcut(KeyCode.F9),
                "Mostrar/esconder painel", "Toggle status panel",
                "Mostra ou esconde o painel de status no canto da tela.", "Shows or hides the status panel.");
            DumpKey = Bind(config, SettingTab.Hotkeys, "Hotkeys", "DiscoveryDump", new KeyboardShortcut(KeyCode.F10),
                "Salvar diagnóstico (dump)", "Save diagnostic (dump)",
                "Salva um JSON (somente leitura) da cena atual em BepInEx/config/AutoKeeper/dumps.",
                "Writes a read-only JSON of the current scene to BepInEx/config/AutoKeeper/dumps.");

            // ---------------------------------------------------------------- Avançado
            ShowOverlay = Toggle(config, SettingTab.Overlay, "Overlay", "ShowOverlay", true,
                "Mostrar painel de status", "Show status panel",
                "Painel no canto superior esquerdo com o estado do bot.", "Top-left panel with the bot state.");
            OverlayPosition = Bind(config, SettingTab.Overlay, "Overlay", "Position", OverlayCorner.TopLeft,
                "Posição do painel", "Panel position",
                "Canto da tela onde o painel aparece (para não cobrir o HUD do jogo).",
                "Screen corner for the panel (so it does not cover the game HUD).");
            OverlayDetailed = Toggle(config, SettingTab.Overlay, "Overlay", "Detailed", false,
                "Painel com detalhes técnicos", "Panel with technical details",
                "Mostra também posição, sanidade, dinheiro e ids técnicos (útil para depurar).",
                "Also shows position, sanity, money and technical ids (debugging).");
            OverlayLogLines = SliderInt(config, SettingTab.Overlay, "Overlay", "LogLines", 3, 0, 20,
                "Linhas de log no painel", "Log lines in panel",
                "Quantas mensagens recentes aparecem no painel.", "How many recent messages the panel shows.");

            // ---------------------------------------------------------------- Avançado
            VerboseLogging = Toggle(config, SettingTab.Advanced, "Debug", "VerboseLogging", false,
                "Log detalhado", "Verbose logging",
                "Grava mensagens de depuração no BepInEx/LogOutput.log.", "Writes debug messages to BepInEx/LogOutput.log.");
            TickIntervalSeconds = Slider(config, SettingTab.Advanced, "Bot", "TickIntervalSeconds", 0.25f, 0.05f, 2f, 0.05f,
                "Intervalo entre decisões (s)", "Decision interval (s)",
                "De quanto em quanto tempo o bot decide o próximo passo. Menor = mais rápido, mais CPU.",
                "How often the bot decides its next step. Lower = faster, more CPU.");
            MoveTimeoutSeconds = Slider(config, SettingTab.Advanced, "Bot", "MoveTimeoutSeconds", 45f, 5f, 300f, 5f,
                "Tempo máximo andando (s)", "Max walking time (s)",
                "Desiste de um alvo se não chegar nele neste tempo.",
                "Gives up on a target if it cannot reach it within this time.");
            WorkStallSeconds = Slider(config, SettingTab.Advanced, "Bot", "WorkStallSeconds", 20f, 5f, 120f, 5f,
                "Parar se o trabalho travar (s)", "Stop if work stalls (s)",
                "Se a receita não avançar por este tempo, o bot para e mostra o motivo.",
                "If a craft makes no progress for this long, the bot stops and shows why.");

            // Fora da UI (só serve para a cova experimental); editável no .cfg.
            GraveCraftId = config.Bind("Bodies", "GraveCraftId", "",
                "Opcional. Vazio = detectar a receita da cova vazia que exige um corpo. (Cova experimental.)");

            // Reservado: não aparece na UI.
            AllowCheats = config.Bind("Safety", "AllowCheats", false,
                "Reservado. O bot só executa ações que o jogador poderia fazer; nenhuma função de cheat existe hoje.");
        }

        /// <summary>Tipos de órgão marcados para extração (nomes do enum ItemType do jogo).</summary>
        public HashSet<string> SelectedOrganTypes()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (ExtractSkin.Value) set.Add("Skin");
            if (ExtractBones.Value) set.Add("Bones");
            if (ExtractSkull.Value) set.Add("Skull");
            if (ExtractHeart.Value) set.Add("Heart");
            if (ExtractBrain.Value) set.Add("Brain");
            if (ExtractGuts.Value) set.Add("Guts");
            return set;
        }

        /// <summary>Tipos da seção "Outros" (bolso do corpo) marcados para extração.</summary>
        public HashSet<string> SelectedPocketKinds()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (ExtractFlesh.Value) set.Add("Flesh");
            if (ExtractFat.Value) set.Add("Fat");
            if (ExtractBlood.Value) set.Add("Blood");
            if (ExtractOtherPocket.Value) set.Add("Other");
            return set;
        }

        /// <summary>Volta todas as opções de uma aba ao padrão.</summary>
        public void ResetTab(SettingTab tab)
        {
            foreach (SettingInfo s in ui)
            {
                if (s.Tab == tab)
                {
                    s.Entry.BoxedValue = s.Entry.DefaultValue;
                }
            }
        }

        // ------------------------------------------------------------------ helpers de registro

        private ConfigEntry<T> Bind<T>(ConfigFile config, SettingTab tab, string section, string key, T value,
            string labelPt, string labelEn, string helpPt, string helpEn, AcceptableValueBase range = null)
        {
            ConfigEntry<T> entry = config.Bind(section, key, value, new ConfigDescription($"{helpPt} / {helpEn}", range));
            ui.Add(new SettingInfo(entry, tab, order++, labelPt, labelEn, helpPt, helpEn));
            return entry;
        }

        private ConfigEntry<bool> Toggle(ConfigFile config, SettingTab tab, string section, string key, bool value,
            string labelPt, string labelEn, string helpPt, string helpEn)
            => Bind(config, tab, section, key, value, labelPt, labelEn, helpPt, helpEn);

        private ConfigEntry<float> Slider(ConfigFile config, SettingTab tab, string section, string key, float value,
            float min, float max, float step, string labelPt, string labelEn, string helpPt, string helpEn)
        {
            ConfigEntry<float> e = Bind(config, tab, section, key, value, labelPt, labelEn, helpPt, helpEn,
                new AcceptableValueRange<float>(min, max));
            SettingInfo info = ui[ui.Count - 1];
            info.Min = min;
            info.Max = max;
            info.Step = step;
            return e;
        }

        private ConfigEntry<int> SliderInt(ConfigFile config, SettingTab tab, string section, string key, int value,
            int min, int max, string labelPt, string labelEn, string helpPt, string helpEn)
        {
            ConfigEntry<int> e = Bind(config, tab, section, key, value, labelPt, labelEn, helpPt, helpEn,
                new AcceptableValueRange<int>(min, max));
            SettingInfo info = ui[ui.Count - 1];
            info.Min = min;
            info.Max = max;
            info.Step = 1f;
            return e;
        }
    }
}
