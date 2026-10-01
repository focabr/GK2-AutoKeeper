using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace AutoKeeper.Config
{
    /// <summary>Enum value that does not appear in the settings windows (feature not released yet).</summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class HiddenOptionAttribute : Attribute
    {
    }

    /// <summary>Where the body goes after the autopsy.</summary>
    public enum BodyDestination
    {
        Crematorium,
        LeaveOnTable,

        /// <summary>Next development step (burying in a grave): the code exists, hidden until testing is done.</summary>
        [HiddenOption]
        Grave,
    }

    /// <summary>What to do when the game applies Lack of sleep (2 days awake).</summary>
    public enum LackOfSleepAction
    {
        Stop,
        Sleep,
        KeepWorking,
    }

    /// <summary>Screen corner where the status panel sits.</summary>
    public enum OverlayCorner
    {
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight,
    }

    /// <summary>Tabs of the settings window (and sections in the GK2 Mod Framework Mods menu).</summary>
    public enum SettingTab
    {
        Bot,
        Bodies,
        Autopsy,
        Hotkeys,
        Overlay,
        Advanced,
    }

    /// <summary>
    /// UI metadata of an option: label/help in PT and EN, tab, slider range.
    /// It is the ONLY source used both by the mod's own window (UI/SettingsWindow) and by the
    /// GK2 Mod Framework bridge — so both screens always show the same options.
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

        /// <summary>Range and step for sliders (float/int).</summary>
        public float Min { get; internal set; }
        public float Max { get; internal set; }
        public float Step { get; internal set; } = 1f;

        public string Label(bool pt) => pt ? LabelPt : LabelEn;
        public string Help(bool pt) => pt ? HelpPt : HelpEn;
    }

    /// <summary>
    /// All mod options. Persisted in BepInEx/config/com.focabr.gk2.autokeeper.cfg (BepInEx standard,
    /// compatible with mod managers), but the player edits them in the settings window (F11) or the Mods menu.
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
        public ConfigEntry<float> MaxInsanity { get; }
        public ConfigEntry<LackOfSleepAction> OnLackOfSleep { get; }

        // [Bodies]
        public ConfigEntry<bool> BodiesEnabled { get; }
        public ConfigEntry<BodyDestination> Destination { get; }
        public ConfigEntry<bool> DigGraves { get; }
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
        public ConfigEntry<bool> FetchRemoteBodies { get; }
        public ConfigEntry<bool> WaitInMorgue { get; }
        public ConfigEntry<bool> CheckCrematoriumFirst { get; }
        public ConfigEntry<bool> UseChest { get; }
        public ConfigEntry<int> ChestFreeSlots { get; }

        // [Overlay]
        public ConfigEntry<bool> ShowOverlay { get; }
        public ConfigEntry<int> OverlayLogLines { get; }
        public ConfigEntry<OverlayCorner> OverlayPosition { get; }
        public ConfigEntry<bool> OverlayDetailed { get; }

        // [Safety] (reserved, not in the UI)
        public ConfigEntry<bool> AllowCheats { get; }

        // [Debug]
        public ConfigEntry<bool> VerboseLogging { get; }

        /// <summary>Options visible in the settings windows, in display order.</summary>
        public IReadOnlyList<SettingInfo> UiSettings => ui;

        public Settings(ConfigFile config)
        {
            // ---------------------------------------------------------------- Bot
            AutoEat = Toggle(config, SettingTab.Bot, "Bot", "AutoEat", true,
                "Comer da barra de atalhos", "Eat from the hot bar",
                "Com energia baixa, usa um item que recupera energia da barra de atalhos (teclas 1 a 4), como o jogador faria. Come vários seguidos sem passar do máximo e pula itens que aumentam a insanidade.",
                "When energy is low, uses an energy item from the hot bar (keys 1 to 4), like the player would. Eats several in a row without going over the maximum and skips items that raise insanity.");
            EatBelowEnergy = Slider(config, SettingTab.Bot, "Bot", "EatBelowEnergy", 20f, 1f, 100f, 1f,
                "Comer com energia abaixo de", "Eat below energy",
                "Come quando a energia fica abaixo deste valor. Deixe maior que o valor de desligar o bot.",
                "Eats when energy drops below this value. Keep it above the turn-off value.");
            MinEnergy = Slider(config, SettingTab.Bot, "Bot", "MinEnergy", 10f, 0f, 100f, 1f,
                "Parar com energia abaixo de", "Turn off the bot below energy",
                "Sem comida na barra de atalhos (ou com \"Comer da barra de atalhos\" desligado), o bot desliga quando a energia fica abaixo deste valor. Com \"Dormir e continuar\", ele vai dormir na cama de casa e depois continua.",
                "With no food on the hot bar (or \"Eat from the hot bar\" off), the bot turns off when energy drops below this value. With \"Sleep, then resume\", it sleeps in the home bed and then carries on.");
            MaxInsanity = Slider(config, SettingTab.Bot, "Bot", "MaxInsanity", 60f, 10f, 80f, 1f,
                "Parar com insanidade acima de", "Turn off the bot above insanity",
                "Cada ponto de insanidade tira 1 da energia máxima, e perto de 80 o jogo bloqueia autópsia e túmulos. O bot desliga ao passar deste valor.",
                "Each insanity point lowers max energy by 1, and near 80 the game blocks autopsy and grave work. The bot turns off above this value.");
            OnLackOfSleep = Bind(config, SettingTab.Bot, "Bot", "OnLackOfSleep", LackOfSleepAction.Stop,
                "Ao ficar com Privação de Sono", "When Lack of sleep hits",
                "Privação de Sono é o efeito do jogo depois de 2 dias sem dormir: metade da energia gasta vira insanidade. Desligar o bot (padrão). Dormir e continuar: termina de levar o corpo que estiver carregando, vai à cama de casa, dorme e volta ao trabalho de onde parou (também quando a comida acaba e a energia fica baixa). Continuar trabalhando: segue normalmente (só o limite de insanidade protege).",
                "Lack of sleep is the game's effect after 2 days awake: half of the energy you spend turns into insanity. Turn off the bot (default). Sleep, then resume: finishes placing any body it carries, walks to the home bed, sleeps and resumes where it stopped (also when the food runs out and energy is low). Keep working: carries on (only the insanity limit protects).");
            MigrateLackOfSleep(config);
            TravelEnabled = Toggle(config, SettingTab.Bot, "Bot", "UseDoors", true,
                "Ir até o trabalho pelas portas", "Walk to the work (through doors)",
                "Atravessa portas (casa, necrotério…) pelo caminho mais curto até onde há trabalho, apertando E na porta como o jogador.",
                "Goes through doors (house, morgue…) along the shortest route to where there is work, pressing E on the door like the player.");
            // ---------------------------------------------------------------- Bodies
            BodiesEnabled = Toggle(config, SettingTab.Bodies, "Bodies", "Enabled", true,
                "Processar corpos", "Process bodies",
                "Leva cada corpo do palete à mesa de autópsia, extrai os órgãos e manda o corpo para o destino escolhido abaixo.",
                "Takes each body from the pallet to the autopsy table, extracts the organs and sends the body to the destination chosen below.");
            Destination = Bind(config, SettingTab.Bodies, "Bodies", "Destination", BodyDestination.Crematorium,
                "Destino após a autópsia", "Body destination after autopsy",
                "Crematório (no necrotério) ou deixar na mesa. Enterrar no túmulo é o próximo passo do desenvolvimento.",
                "Crematorium (inside the morgue) or leave on the table. Burying in a grave is the next development step.");
            KeepVisibleDestination();
            DigGraves = BindHidden(config, "Bodies", "DigGraves", true,
                "Cavar túmulos já marcados", "Dig graves you placed",
                "Com destino Túmulo e nenhum túmulo aberto, o bot cava com a pá um túmulo que você já marcou com o construtor do cemitério. Ele nunca marca túmulos novos nem desenterra corpos.",
                "With the Grave destination and no open grave, the bot digs with the shovel a grave you already placed with the graveyard builder. It never places new graves nor exhumes bodies.");
            SearchRadius = Slider(config, SettingTab.Bodies, "Bodies", "SearchRadius", 80f, 5f, 300f, 5f,
                "Alcance para corpos no chão (m)", "Ground body range (m)",
                "Distância máxima para pegar corpos soltos no chão da área onde o bot está. Paletes, mesas e crematório são achados em qualquer lugar alcançável; corpos em outras áreas dependem de \"Buscar corpos em outras áreas\".",
                "Maximum distance to pick up loose bodies on the ground of the bot's current area. Pallets, tables and crematorium are found anywhere reachable; bodies in other areas depend on \"Fetch bodies from other areas\".");
            FetchRemoteBodies = Toggle(config, SettingTab.Bodies, "Bodies", "FetchRemoteBodies", true,
                "Buscar corpos em outras áreas", "Fetch bodies from other areas",
                "Como última tarefa (sem corpo no palete), o bot sai do necrotério pelas portas para buscar corpos largados no chão lá fora (ex.: entregues pela Inquisição) e os traz para a mesa ou para um palete vazio.",
                "As a last task (no body on the pallets), the bot leaves the morgue through the doors to fetch bodies left on the ground outside (e.g. delivered by the Inquisition) and brings them to a table or an empty pallet.");
            WaitInMorgue = Toggle(config, SettingTab.Bodies, "Bodies", "WaitInMorgue", true,
                "Esperar no necrotério", "Wait in the morgue",
                "Sem trabalho (por exemplo, ao acordar em casa), o bot vai até o necrotério pelas portas e espera lá os próximos corpos. Desligado, ele fica onde está.",
                "With nothing to do (e.g. after waking up at home), the bot walks to the morgue through the doors and waits there for the next bodies. Off: it stays where it is.");
            CheckCrematoriumFirst = Toggle(config, SettingTab.Bodies, "Bodies", "CheckCrematoriumFirst", true,
                "Recolher o crematório primeiro", "Collect the crematorium first",
                "Ao chegar no necrotério, o bot recolhe o que já estiver pronto no crematório antes de começar (só vai lá se houver algo).",
                "On arriving at the morgue, the bot collects anything already finished in the crematorium before starting (it only goes if there is something).");
            UseChest = Toggle(config, SettingTab.Bodies, "Bodies", "UseChest", true,
                "Guardar no baú o que recolheu", "Store what the bot collected",
                "Com o inventário quase cheio (limite abaixo), leva ao baú mais próximo SÓ o que o bot recolheu (extrações e crematório). O resto do inventário nunca é mexido.",
                "When the inventory is nearly full (limit below), moves ONLY what the bot collected (extractions and crematorium) to the nearest chest. The rest of your inventory is never touched.");
            ChestFreeSlots = SliderInt(config, SettingTab.Bodies, "Bodies", "ChestFreeSlots", 3, 1, 10,
                "Espaços livres mínimos", "Go to the chest below free slots",
                "Vai ao baú quando sobrarem menos espaços livres que isso no inventário.",
                "Goes to the chest when fewer free inventory slots than this remain.");

            RequireMastery = Toggle(config, SettingTab.Autopsy, "Bodies", "RequireMastery", true,
                "Pular extrações arriscadas", "Skip low-chance extractions",
                "Vale por cima das opções abaixo: confere a chance de sucesso de cada item (a mesma da janela de extração do jogo) e pula o que ficar abaixo do mínimo.",
                "Overrides the options below: checks each item's success chance (the same shown in the game's extraction window) and skips anything below the minimum.");
            MinMasteryChance = SliderInt(config, SettingTab.Autopsy, "Bodies", "MinMasteryChance", 100, 1, 100,
                "Chance de sucesso mínima (%)", "Minimum success chance (%)",
                "100 = só quando o sucesso é garantido (o jogo não mostra %). Ex.: 60 aceita o cérebro a 62% mas pula as entranhas a 38%.",
                "100 = only guaranteed success (the game shows no %). E.g. 60 accepts the brain at 62% but skips guts at 38%.");
            ExtractSkin = Toggle(config, SettingTab.Autopsy, "Bodies", "ExtractSkin", true, "Extrair pele", "Extract skin",
                "Extrai a pele na autópsia.", "Extract skin during autopsy.");
            ExtractBones = Toggle(config, SettingTab.Autopsy, "Bodies", "ExtractBones", true, "Extrair ossos", "Extract bones",
                "Extrai os ossos na autópsia.", "Extract bones during autopsy.");
            ExtractSkull = Toggle(config, SettingTab.Autopsy, "Bodies", "ExtractSkull", true, "Extrair caveira", "Extract skull",
                "Extrai a caveira na autópsia.", "Extract the skull during autopsy.");
            ExtractHeart = Toggle(config, SettingTab.Autopsy, "Bodies", "ExtractHeart", true, "Extrair coração", "Extract heart",
                "Extrai o coração na autópsia.", "Extract the heart during autopsy.");
            ExtractBrain = Toggle(config, SettingTab.Autopsy, "Bodies", "ExtractBrain", true, "Extrair cérebro", "Extract brain",
                "Extrai o cérebro na autópsia.", "Extract the brain during autopsy.");
            ExtractGuts = Toggle(config, SettingTab.Autopsy, "Bodies", "ExtractGuts", true, "Extrair entranhas", "Extract guts",
                "Extrai as entranhas na autópsia.", "Extract guts during autopsy.");
            ExtractFlesh = Toggle(config, SettingTab.Autopsy, "Bodies", "ExtractFlesh", true, "Extrair carne", "Extract flesh",
                "Tira a carne (seção \"Outros\" da mesa de autópsia).", "Takes out the flesh (\"Others\" section of the autopsy table).");
            ExtractFat = Toggle(config, SettingTab.Autopsy, "Bodies", "ExtractFat", true, "Extrair gordura", "Extract fat",
                "Tira a gordura (seção \"Outros\").", "Takes out the fat (\"Others\" section).");
            ExtractBlood = Toggle(config, SettingTab.Autopsy, "Bodies", "ExtractBlood", true, "Extrair sangue", "Extract blood",
                "Tira o sangue (seção \"Outros\").", "Takes out the blood (\"Others\" section).");
            ExtractOtherPocket = Toggle(config, SettingTab.Autopsy, "Bodies", "ExtractOtherPocket", false, "Extrair o resto de \"Outros\"", "Extract the rest of \"Others\"",
                "Tira qualquer outro item da seção \"Outros\" que não seja carne, gordura ou sangue.",
                "Takes out any other item of the \"Others\" section that is not flesh, fat or blood.");
            // ---------------------------------------------------------------- Hotkeys
            ToggleBotKey = Bind(config, SettingTab.Hotkeys, "Hotkeys", "ToggleBot", new KeyboardShortcut(KeyCode.F8),
                "Ligar/desligar o bot", "Toggle bot",
                "Liga ou desliga o bot na hora (parada imediata).", "Turns the bot on or off immediately (instant stop).");
            ToggleOverlayKey = Bind(config, SettingTab.Hotkeys, "Hotkeys", "ToggleOverlay", new KeyboardShortcut(KeyCode.F9),
                "Mostrar/esconder painel", "Toggle status panel",
                "Mostra ou esconde o painel de status no canto da tela.", "Shows or hides the status panel.");
            DumpKey = Bind(config, SettingTab.Hotkeys, "Hotkeys", "DiscoveryDump", new KeyboardShortcut(KeyCode.F10),
                "Salvar arquivo de diagnóstico", "Save diagnostic file",
                "Salva um JSON (somente leitura) da cena atual em BepInEx/config/AutoKeeper/dumps.",
                "Writes a read-only JSON of the current scene to BepInEx/config/AutoKeeper/dumps.");
            OpenSettingsKey = Bind(config, SettingTab.Hotkeys, "Hotkeys", "OpenSettings", new KeyboardShortcut(KeyCode.F11),
                "Abrir configurações", "Open settings",
                "Abre/fecha esta tela.", "Opens/closes this window.");

            // ---------------------------------------------------------------- Overlay
            ShowOverlay = Toggle(config, SettingTab.Overlay, "Overlay", "ShowOverlay", true,
                "Mostrar painel de status", "Show status panel",
                "Painel com o estado do bot, no canto escolhido em \"Posição do painel\".", "Panel with the bot state, in the corner chosen in \"Panel position\".");
            OverlayPosition = Bind(config, SettingTab.Overlay, "Overlay", "Position", OverlayCorner.TopLeft,
                "Posição do painel", "Panel position",
                "Canto da tela onde o painel aparece (para não cobrir o HUD do jogo).",
                "Screen corner for the panel (so it does not cover the game HUD).");
            OverlayDetailed = Toggle(config, SettingTab.Overlay, "Overlay", "Detailed", false,
                "Painel com detalhes técnicos", "Panel with technical details",
                "Mostra também posição, zona, cena e dinheiro (útil para depurar).",
                "Also shows position, zone, scene and money (debugging).");
            OverlayLogLines = SliderInt(config, SettingTab.Overlay, "Overlay", "LogLines", 3, 0, 20,
                "Eventos no painel", "Events in panel",
                "Quantos eventos recentes aparecem no painel (0 = nenhum).", "How many recent events the panel shows (0 = none).");

            // ---------------------------------------------------------------- Advanced
            VerboseLogging = Toggle(config, SettingTab.Advanced, "Debug", "VerboseLogging", false,
                "Log detalhado", "Verbose logging",
                "Grava mensagens de depuração no BepInEx/LogOutput.log.", "Writes debug messages to BepInEx/LogOutput.log.");
            TickIntervalSeconds = Slider(config, SettingTab.Advanced, "Bot", "TickIntervalSeconds", 0.25f, 0.05f, 2f, 0.05f,
                "Intervalo entre decisões (s)", "Decision interval (s)",
                "De quanto em quanto tempo o bot decide o próximo passo. Menor = mais rápido, mais CPU.",
                "How often the bot decides its next step. Lower = faster, more CPU.");
            MoveTimeoutSeconds = Slider(config, SettingTab.Advanced, "Bot", "MoveTimeoutSeconds", 45f, 5f, 300f, 5f,
                "Tempo máximo para chegar (s)", "Max time to arrive (s)",
                "Desiste de um alvo se não chegar nele neste tempo.",
                "Gives up on a target if it cannot reach it within this time.");
            WorkStallSeconds = Slider(config, SettingTab.Advanced, "Bot", "WorkStallSeconds", 20f, 5f, 120f, 5f,
                "Parar se o trabalho travar (s)", "Stop if work stalls (s)",
                "Se a receita não avançar por este tempo, o bot para e mostra o motivo.",
                "If a craft makes no progress for this long, the bot stops and shows why.");

            // Reserved: not shown in the UI.
            AllowCheats = config.Bind("Safety", "AllowCheats", false,
                "Reservado. O bot só executa ações que o jogador poderia fazer; nenhuma função de cheat existe hoje. / Reserved. The bot only performs actions the player could do; no cheat feature exists today.");
        }

        /// <summary>Organ types selected for extraction (names from the game's ItemType enum).</summary>
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

        /// <summary>Types of the "Others" section (body pocket) selected for extraction.</summary>
        public HashSet<string> SelectedPocketKinds()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (ExtractFlesh.Value) set.Add("Flesh");
            if (ExtractFat.Value) set.Add("Fat");
            if (ExtractBlood.Value) set.Add("Blood");
            if (ExtractOtherPocket.Value) set.Add("Other");
            return set;
        }

        /// <summary>Resets all options of a tab to their defaults.</summary>
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

        // ------------------------------------------------------------------ registration helpers

        /// <summary>
        /// 0.3.18 had two keys ([Bot] StopOnLackOfSleep and SleepWhenTired). Reads the old ones from the .cfg (orphaned entries),
        /// converts them to OnLackOfSleep only once and deletes them from the file.
        /// </summary>
        private void MigrateLackOfSleep(ConfigFile config)
        {
            try
            {
                var orphans = HarmonyLib.Traverse.Create(config).Property("OrphanedEntries").GetValue<Dictionary<ConfigDefinition, string>>();
                if (orphans == null)
                {
                    return;
                }
                var stopDef = new ConfigDefinition("Bot", "StopOnLackOfSleep");
                var sleepDef = new ConfigDefinition("Bot", "SleepWhenTired");
                bool hasStop = orphans.TryGetValue(stopDef, out string stop);
                bool hasSleep = orphans.TryGetValue(sleepDef, out string sleep);
                if (!hasStop && !hasSleep)
                {
                    return;
                }
                if (string.Equals(sleep, "true", StringComparison.OrdinalIgnoreCase))
                {
                    OnLackOfSleep.Value = LackOfSleepAction.Sleep;
                }
                else if (string.Equals(stop, "false", StringComparison.OrdinalIgnoreCase))
                {
                    OnLackOfSleep.Value = LackOfSleepAction.KeepWorking;
                }
                orphans.Remove(stopDef);
                orphans.Remove(sleepDef);
                config.Save();
            }
            catch (Exception)
            {
                // Migration is a convenience: without it the new option just stays at its default.
            }
        }

        /// <summary>A hidden destination (grave, still in testing) becomes Crematorium — also when chosen from the Framework's Mods menu.</summary>
        private void KeepVisibleDestination()
        {
            if (IsHidden(Destination.Value))
            {
                Destination.Value = BodyDestination.Crematorium;
            }
            Destination.SettingChanged += (s, e) =>
            {
                if (IsHidden(Destination.Value))
                {
                    Destination.Value = BodyDestination.Crematorium;
                }
            };
        }

        /// <summary>Enum values shown in the screens (without those marked with <see cref="HiddenOptionAttribute"/>).</summary>
        public static Array VisibleValues(Type enumType)
        {
            var list = new List<object>();
            foreach (object v in Enum.GetValues(enumType))
            {
                if (!IsHidden(v))
                {
                    list.Add(v);
                }
            }
            Array result = Array.CreateInstance(enumType, list.Count);
            for (int i = 0; i < list.Count; i++)
            {
                result.SetValue(list[i], i);
            }
            return result;
        }

        private static bool IsHidden(object enumValue)
        {
            var field = enumValue.GetType().GetField(enumValue.ToString());
            return field != null && field.IsDefined(typeof(HiddenOptionAttribute), false);
        }

        /// <summary>Option stored in the .cfg but kept out of the screens (feature not released yet).</summary>
        private static ConfigEntry<T> BindHidden<T>(ConfigFile config, string section, string key, T value, string labelPt, string labelEn, string helpPt, string helpEn)
            => config.Bind(section, key, value, new ConfigDescription($"(em testes / in testing) {helpPt} / {helpEn}"));

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
