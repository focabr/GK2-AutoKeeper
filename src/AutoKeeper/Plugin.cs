using System;
using AutoKeeper.Bot;
using AutoKeeper.Bot.Tasks;
using AutoKeeper.Config;
using AutoKeeper.Core;
using AutoKeeper.UI;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace AutoKeeper
{
    /// <summary>
    /// Ponto de entrada do mod. Só faz "cola": config, Harmony, hotkeys e ciclo de vida.
    /// Toda lógica de jogo fica em Core/GameApi; toda lógica de automação fica em Bot/.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    [BepInProcess("GraveyardKeeper2.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.focabr.gk2.autokeeper";
        public const string Name = "GK2 AutoKeeper";
        public const string Version = "0.3.14";

        /// <summary>Versão do jogo em que o mod foi testado (GameInfo.Version).</summary>
        public const string TestedGameVersion = "1.007.1";

        /// <summary>Instância ativa (usada pela ponte opcional do GK2 Mod Framework).</summary>
        public static Plugin Instance { get; private set; }

        /// <summary>Opções do mod (mesma fonte para a janela própria e para o menu Mods do Framework).</summary>
        public Settings Settings { get; private set; }

        internal BotController Bot { get; private set; }

        private Harmony harmony;
        private Overlay overlay;
        private SettingsWindow settingsWindow;          // tela simples (IMGUI), usada só se a nativa falhar
        private NativeSettingsWindow nativeWindow;      // tela com o visual do jogo
        private bool compatibilityChecked;
        private bool configDirty;
        private float configDirtySince;

        /// <summary>Estado do bot em texto (para a ponte do Framework).</summary>
        public string BotStatusText => Bot == null ? "-" : $"{Bot.State} — {Bot.StateDetail}";

        public bool IsBotOn => Bot != null && Bot.State != BotController.BotState.Off;

        /// <summary>Liga/desliga o bot (mesmo efeito da hotkey).</summary>
        public void ToggleBot() => Bot?.Toggle();

        private bool SettingsOpen => (nativeWindow != null && nativeWindow.IsShown) || (settingsWindow != null && settingsWindow.IsOpen);

        private bool CapturingKey => (nativeWindow != null && nativeWindow.IsCapturingKey) || (settingsWindow != null && settingsWindow.IsCapturingKey);

        /// <summary>
        /// Abre/fecha a tela de configurações. Preferência: tela NATIVA (peças da janela de Configurações do jogo).
        /// Se ela não puder ser montada (ex.: jogo mudou), usa a tela simples e avisa no log.
        /// </summary>
        public void ToggleSettingsWindow()
        {
            if (nativeWindow != null && nativeWindow.IsShown)
            {
                nativeWindow.Close();
                return;
            }
            if (settingsWindow.IsOpen)
            {
                settingsWindow.Close();
                return;
            }
            if (nativeWindow == null)
            {
                nativeWindow = NativeSettingsWindow.TryCreate(Settings, Bot, out string error);
                if (nativeWindow == null)
                {
                    ModLog.WarnOnce("native-ui", $"Tela nativa indisponível ({error}); usando a tela simples.");
                }
            }
            if (nativeWindow != null)
            {
                try
                {
                    nativeWindow.Open(null);
                    return;
                }
                catch (Exception e)
                {
                    ModLog.Error($"Falha ao abrir a tela nativa: {e}");
                    Destroy(nativeWindow.gameObject);
                    nativeWindow = null;
                }
            }
            settingsWindow.Toggle();
        }

        private void Awake()
        {
            Instance = this;
            // Salvar o .cfg a cada movimento de slider seria desperdício: salvamos com atraso (ver SaveConfigIfDirty).
            Config.SaveOnConfigSet = false;
            Settings = new Settings(Config);
            Config.SettingChanged += (_, __) => { configDirty = true; configDirtySince = Time.unscaledTime; };
            Config.Save(); // grava opções novas/descrições atualizadas
            ModLog.Init(Logger, Settings);

            // Um único Harmony com ID = GUID, para que UnpatchSelf remova só os nossos patches.
            harmony = new Harmony(Guid);
            try
            {
                harmony.PatchAll(typeof(Plugin).Assembly);
                ModLog.Info($"Patches Harmony aplicados: {harmony.GetPatchedMethods().CountSafe()}");
            }
            catch (Exception e)
            {
                ModLog.Error($"Falha ao aplicar patches Harmony — o bot ficará desativado. {e}");
            }

            Bot = new BotController(Settings);
            GameApi.HookGameLifecycle();
            Bot.Register(new ProcessBodiesTask(Settings, Bot.Navigator)); // ordem = prioridade
            overlay = new Overlay(Settings, Bot);
            settingsWindow = new SettingsWindow(Settings, Bot);
            overlay.OnSettingsClicked = ToggleSettingsWindow;

            ModLog.Info($"{Name} {Version} carregado. {Settings.ToggleBotKey.Value} = bot, {Settings.OpenSettingsKey.Value} = configurações, {Settings.ToggleOverlayKey.Value} = painel.");
        }

        private void Update()
        {
            CheckCompatibilityOnce();
            HandleHotkeys();
            Bot.Update(Time.unscaledDeltaTime);
            SaveConfigIfDirty(force: !SettingsOpen);
        }

        private void OnGUI()
        {
            overlay.Draw();
            settingsWindow.Draw();

            // Informa a GameApi se o mouse está sobre a UI do mod (clique não vira ataque) e se a janela está aberta.
            if (Event.current != null && Event.current.type == EventType.Repaint)
            {
                Vector2 m = Event.current.mousePosition;
                bool over = overlay.Rect.Contains(m) || (settingsWindow.IsOpen && settingsWindow.Rect.Contains(m));
                GameApi.SetModUiState(settingsWindow.IsOpen, over);
            }
        }

        private void OnDestroy()
        {
            Bot?.Stop("plugin descarregado");
            GameApi.SetModUiState(false, false);
            SaveConfigIfDirty(force: true);
            harmony?.UnpatchSelf();
        }

        /// <summary>Grava o .cfg 1 s depois da última alteração (ou na hora ao fechar a janela/sair).</summary>
        private void SaveConfigIfDirty(bool force)
        {
            if (!configDirty)
            {
                return;
            }
            if (force || Time.unscaledTime - configDirtySince > 1f)
            {
                configDirty = false;
                try
                {
                    Config.Save();
                }
                catch (Exception e)
                {
                    ModLog.Warn($"Não consegui salvar o .cfg: {e.Message}");
                }
            }
        }

        /// <summary>Compara a versão do jogo com a testada, uma única vez, quando o jogo já inicializou.</summary>
        private void CheckCompatibilityOnce()
        {
            if (compatibilityChecked || !GameApi.IsMainGameReady)
            {
                return;
            }
            compatibilityChecked = true;

            string gameVersion = GameApi.GetGameVersion();
            if (gameVersion == null)
            {
                ModLog.Warn("Não consegui ler a versão do jogo (GameInfo). O mod pode estar desatualizado.");
            }
            else if (gameVersion != TestedGameVersion)
            {
                ModLog.Warn($"Versão do jogo {gameVersion} difere da testada ({TestedGameVersion}). Se algo falhar, desligue o bot (F8) e verifique atualizações do mod.");
            }
            else
            {
                ModLog.Info($"Versão do jogo {gameVersion} — compatível (testada).");
            }
        }

        private void HandleHotkeys()
        {
            try
            {
                HandleHotkeysUnsafe();
            }
            catch (Exception e)
            {
                // Ex.: input legado do Unity desativado. Loga uma vez em vez de a cada frame.
                ModLog.WarnOnce("hotkeys", $"Hotkeys indisponíveis ({e.GetType().Name}: {e.Message}).");
            }
        }

        private void HandleHotkeysUnsafe()
        {
            if (CapturingKey)
            {
                return; // o jogador está escolhendo uma tecla nova na tela de configurações
            }
            if (Settings.OpenSettingsKey.Value.IsDown())
            {
                ToggleSettingsWindow();
            }
            if (Settings.ToggleBotKey.Value.IsDown())
            {
                Bot.Toggle();
            }
            if (Settings.ToggleOverlayKey.Value.IsDown())
            {
                Settings.ShowOverlay.Value = !Settings.ShowOverlay.Value;
            }
            if (Settings.DumpKey.Value.IsDown())
            {
                string path = GameApi.WriteDiscoveryDump();
                if (path != null)
                {
                    ModLog.Info($"Dump de descoberta salvo em: {path}");
                }
            }
        }
    }

    internal static class EnumerableExtensions
    {
        /// <summary>Count() que não explode se o enumerável for nulo.</summary>
        public static int CountSafe<T>(this System.Collections.Generic.IEnumerable<T> source)
        {
            if (source == null)
            {
                return 0;
            }
            int n = 0;
            foreach (T _ in source)
            {
                n++;
            }
            return n;
        }
    }
}
