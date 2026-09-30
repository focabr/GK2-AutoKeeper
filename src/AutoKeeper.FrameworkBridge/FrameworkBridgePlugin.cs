using System;
using System.Runtime.CompilerServices;
using AutoKeeper.Config;
using BepInEx;
using BepInEx.Configuration;
using GK2.Framework;

namespace AutoKeeper.FrameworkBridge
{
    /// <summary>
    /// Ponte OPCIONAL: registra o AutoKeeper no botão "Mods" nativo do jogo (GK2 Mod Framework).
    /// Padrão recomendado pelo Framework (OPTIONAL_INTEGRATION.md): o AutoKeeper.dll não conhece o Framework;
    /// esta DLL depende dos dois. Sem o Framework instalado, a ponte só escreve uma linha de aviso e não faz nada
    /// (dependência "soft": nada de erro vermelho no log para quem não usa o Framework).
    /// As opções são as MESMAS ConfigEntry do AutoKeeper (mesma seção/chave), lidas de Settings.UiSettings.
    /// </summary>
    [BepInPlugin(Guid, Name, Plugin.Version)]
    [BepInDependency(Plugin.Guid, BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency(FrameworkPlugin.PluginGuid, BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class FrameworkBridgePlugin : BaseUnityPlugin
    {
        public const string Guid = "com.focabr.gk2.autokeeper.framework";
        public const string Name = "GK2 AutoKeeper - Mods menu";

        private void Awake()
        {
            Plugin main = Plugin.Instance;
            if (main == null || main.Settings == null)
            {
                Logger.LogError("AutoKeeper principal não está disponível; integração com o menu Mods desativada.");
                return;
            }
            if (!BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey(FrameworkPlugin.PluginGuid))
            {
                Logger.LogInfo("GK2 Mod Framework não instalado — menu Mods desativado (normal; as opções ficam no F11).");
                return;
            }
            try
            {
                RegisterWithFramework(main);
                Logger.LogInfo("AutoKeeper registrado no menu Mods do GK2 Mod Framework.");
            }
            catch (Exception e)
            {
                // O AutoKeeper continua funcionando com a janela própria (F11).
                Logger.LogError($"Falha ao registrar no GK2 Mod Framework: {e}");
            }
        }

        /// <summary>Separado do Awake para os tipos do Framework só serem carregados quando ele existe.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void RegisterWithFramework(Plugin main)
        {
            FrameworkApi.RegisterMod(new Bridge(main), main.Config);
        }

        private sealed class Bridge : Gk2ModBase
        {
            private readonly Plugin main;
            private readonly Gk2ModMetadata metadata;

            internal Bridge(Plugin main)
            {
                this.main = main;
                metadata = new Gk2ModMetadata(
                    Plugin.Guid,
                    Plugin.Name,
                    "focabr",
                    Plugin.Version,
                    "Automation bot: morgue body processing using only player actions. F8 toggles the bot, F11 opens its own settings window.",
                    supportsRuntimeToggle: false,
                    requiresKnownBuild: false,
                    frameworkManagesEnabledState: false);
            }

            public override Gk2ModMetadata Metadata => metadata;

            public override void OnRegister(Gk2ModContext context)
            {
                bool pt = (FrameworkLocalization.CurrentLanguage ?? "en").StartsWith("pt", StringComparison.OrdinalIgnoreCase);
                Gk2Settings ui = context.Settings;

                // Ações/estado no topo da seção Bot.
                ui.AddButton("Bot", "ToggleBotAction",
                    pt ? "Bot" : "Bot",
                    pt ? "Liga ou desliga o bot agora (mesmo que a tecla de atalho)." : "Turns the bot on or off now (same as the hotkey).",
                    () => main.IsBotOn ? (pt ? "Desligar bot" : "Stop bot") : (pt ? "Ligar bot" : "Start bot"),
                    main.ToggleBot, order: -20);
                ui.AddReadOnly("Bot", "StatusReadOnly",
                    pt ? "Estado" : "Status",
                    pt ? "Estado atual do bot." : "Current bot state.",
                    () => main.BotStatusText, order: -19);

                foreach (SettingInfo s in main.Settings.UiSettings)
                {
                    Register(ui, s, pt);
                }

                // Opções de corpos ficam acinzentadas quando a rotina está desligada.
                foreach (SettingInfo s in main.Settings.UiSettings)
                {
                    if ((s.Tab == SettingTab.Bodies || s.Tab == SettingTab.Autopsy) && s.Entry != main.Settings.BodiesEnabled)
                    {
                        ui.SetEnabledCondition(s.Entry.Definition.Section, s.Entry.Definition.Key, () => main.Settings.BodiesEnabled.Value);
                    }
                }
            }

            private void Register(Gk2Settings ui, SettingInfo s, bool pt)
            {
                ConfigDefinition def = s.Entry.Definition;
                string name = s.Label(pt);
                string help = s.Help(pt);
                Type type = s.Entry.SettingType;

                if (type == typeof(bool))
                {
                    ui.AddToggle(def.Section, def.Key, (bool)s.Entry.DefaultValue, name, help, s.Order);
                }
                else if (type == typeof(float))
                {
                    ui.AddFloatSlider(def.Section, def.Key, (float)s.Entry.DefaultValue, s.Min, s.Max, name, help, s.Step, s.Order);
                }
                else if (type == typeof(int))
                {
                    ui.AddIntSlider(def.Section, def.Key, (int)s.Entry.DefaultValue, (int)s.Min, (int)s.Max, name, help, Math.Max(1, (int)s.Step), s.Order);
                }
                else if (type == typeof(BodyDestination))
                {
                    ui.AddEnum(def.Section, def.Key, (BodyDestination)s.Entry.DefaultValue, name, help, s.Order);
                }
                else if (type == typeof(OverlayCorner))
                {
                    ui.AddEnum(def.Section, def.Key, (OverlayCorner)s.Entry.DefaultValue, name, help, s.Order);
                }
                else if (type == typeof(LackOfSleepAction))
                {
                    ui.AddEnum(def.Section, def.Key, (LackOfSleepAction)s.Entry.DefaultValue, name, help, s.Order);
                }
                else if (type == typeof(KeyboardShortcut))
                {
                    ui.AddKeybind(def.Section, def.Key, (KeyboardShortcut)s.Entry.DefaultValue, name, help, s.Order);
                }
                else if (type == typeof(string))
                {
                    ui.AddText(def.Section, def.Key, (string)s.Entry.DefaultValue, name, help, s.Order);
                }
            }
        }
    }
}
