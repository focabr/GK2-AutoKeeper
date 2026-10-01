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
    /// Mod entry point. It is only "glue": config, Harmony, hotkeys and lifecycle.
    /// All game logic lives in Core/GameApi; all automation logic lives in Bot/.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    [BepInProcess("GraveyardKeeper2.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.focabr.gk2.autokeeper";
        public const string Name = "GK2 AutoKeeper";
        public const string Version = "0.3.28";

        /// <summary>Game version the mod was tested on (GameInfo.Version).</summary>
        public const string TestedGameVersion = "1.007.1";

        /// <summary>Active instance (used by the optional GK2 Mod Framework bridge).</summary>
        public static Plugin Instance { get; private set; }

        /// <summary>Mod options (same source for the mod's own window and for the Framework's Mods menu).</summary>
        public Settings Settings { get; private set; }

        internal BotController Bot { get; private set; }

        private Harmony harmony;
        private Overlay overlay;
        private SettingsWindow settingsWindow;          // simple window (IMGUI), used only if the native one fails
        private NativeSettingsWindow nativeWindow;      // window with the game's look
        private bool compatibilityChecked;
        private bool configDirty;
        private float configDirtySince;

        /// <summary>Bot state as text (for the Framework bridge).</summary>
        public string BotStatusText => Bot == null ? "-"
            : string.IsNullOrEmpty(Bot.StateDetail) ? Bot.State.ToString() : $"{Bot.State} — {Bot.StateDetail}";

        public bool IsBotOn => Bot != null && Bot.State != BotController.BotState.Off;

        /// <summary>Turns the bot on/off (same effect as the hotkey).</summary>
        public void ToggleBot() => Bot?.Toggle();

        private bool SettingsOpen => (nativeWindow != null && nativeWindow.IsShown) || (settingsWindow != null && settingsWindow.IsOpen);

        private bool CapturingKey => (nativeWindow != null && nativeWindow.IsCapturingKey) || (settingsWindow != null && settingsWindow.IsCapturingKey);

        /// <summary>
        /// Opens/closes the settings window. Preference: NATIVE window (pieces of the game's Settings window).
        /// If it cannot be built (e.g. the game changed), uses the simple window and warns in the log.
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
                    ModLog.WarnOnce("native-ui", Lang.T($"Tela nativa indisponível ({error}); usando a tela simples.", $"Native settings window unavailable ({error}); using the simple window."));
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
                    ModLog.Error(Lang.T($"Falha ao abrir a tela nativa: {e}", $"Failed to open the native settings window: {e}"));
                    Destroy(nativeWindow.gameObject);
                    nativeWindow = null;
                }
            }
            settingsWindow.Toggle();
        }

        private void Awake()
        {
            Instance = this;
            // Saving the .cfg on every slider move would be wasteful: we save with a delay (see SaveConfigIfDirty).
            Config.SaveOnConfigSet = false;
            Settings = new Settings(Config);
            Config.SettingChanged += (_, __) => { configDirty = true; configDirtySince = Time.unscaledTime; };
            Config.Save(); // writes new options/updated descriptions
            ModLog.Init(Logger, Settings);

            // A single Harmony with ID = GUID, so that UnpatchSelf removes only our patches.
            harmony = new Harmony(Guid);
            try
            {
                harmony.PatchAll(typeof(Plugin).Assembly);
                ModLog.Detail(Lang.T($"Patches Harmony aplicados: {harmony.GetPatchedMethods().CountSafe()}", $"Harmony patches applied: {harmony.GetPatchedMethods().CountSafe()}"));
            }
            catch (Exception e)
            {
                ModLog.Error(Lang.T($"Falha ao aplicar patches Harmony — o bot ficará desativado. {e}", $"Failed to apply Harmony patches — the bot will be disabled. {e}"));
            }

            Bot = new BotController(Settings);
            GameApi.HookGameLifecycle();
            Bot.Register(new ProcessBodiesTask(Settings, Bot.Navigator)); // order = priority
            overlay = new Overlay(Settings, Bot);
            settingsWindow = new SettingsWindow(Settings, Bot);
            overlay.OnSettingsClicked = ToggleSettingsWindow;

            // The game has not loaded its language yet: the panel message ("loaded, F8 = bot…") waits for
            // CheckCompatibilityOnce, so it comes out in the player's language. Here only the log file line.
            ModLog.Detail($"{Name} {Version} loaded.");
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

            // Tells GameApi whether the mouse is over the mod's UI (a click does not become an attack) and whether the window is open.
            if (Event.current != null && Event.current.type == EventType.Repaint)
            {
                Vector2 m = Event.current.mousePosition;
                bool over = overlay.Rect.Contains(m) || (settingsWindow.IsOpen && settingsWindow.Rect.Contains(m));
                GameApi.SetModUiState(settingsWindow.IsOpen, over);
            }
        }

        private void OnDestroy()
        {
            Bot?.Stop(Lang.T("plugin descarregado", "plugin unloaded"));
            GameApi.SetModUiState(false, false);
            SaveConfigIfDirty(force: true);
            harmony?.UnpatchSelf();
        }

        /// <summary>Writes the .cfg 1 s after the last change (or immediately when closing the window/quitting).</summary>
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
                    ModLog.Warn(Lang.T($"Não consegui salvar o .cfg: {e.Message}", $"Could not save the .cfg: {e.Message}"));
                }
            }
        }

        /// <summary>Compares the game version with the tested one, only once, after the game has initialized.</summary>
        private void CheckCompatibilityOnce()
        {
            if (compatibilityChecked || !GameApi.IsMainGameReady)
            {
                return;
            }
            compatibilityChecked = true;

            ModLog.Info(Lang.T($"{Name} {Version} carregado. {Settings.ToggleBotKey.Value} = bot, {Settings.OpenSettingsKey.Value} = configurações, {Settings.ToggleOverlayKey.Value} = painel.",
                $"{Name} {Version} loaded. {Settings.ToggleBotKey.Value} = bot, {Settings.OpenSettingsKey.Value} = settings, {Settings.ToggleOverlayKey.Value} = status panel."));

            string gameVersion = GameApi.GetGameVersion();
            if (gameVersion == null)
            {
                ModLog.Warn(Lang.T("Não consegui ler a versão do jogo (GameInfo). O mod pode estar desatualizado.", "Could not read the game version (GameInfo). The mod may be outdated."));
            }
            else if (gameVersion != TestedGameVersion)
            {
                ModLog.Warn(Lang.T($"Versão do jogo {gameVersion} difere da testada ({TestedGameVersion}). Se algo falhar, desligue o bot (F8) e verifique atualizações do mod.",
                    $"Game version {gameVersion} differs from the tested one ({TestedGameVersion}). If something fails, turn off the bot (F8) and check for mod updates."));
            }
            else
            {
                ModLog.Info(Lang.T($"Versão do jogo {gameVersion} — compatível (testada).", $"Game version {gameVersion} — compatible (tested)."));
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
                // E.g. Unity's legacy input disabled. Logs once instead of every frame.
                ModLog.WarnOnce("hotkeys", Lang.T($"Hotkeys indisponíveis ({e.GetType().Name}: {e.Message}).", $"Hotkeys unavailable ({e.GetType().Name}: {e.Message})."));
            }
        }

        private void HandleHotkeysUnsafe()
        {
            if (CapturingKey)
            {
                return; // the player is choosing a new key in the settings window
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
                    ModLog.Detail(Lang.T($"Dump de descoberta salvo em: {path}", $"Discovery dump saved to: {path}"));
                    ModLog.Info(Lang.T($"Dump salvo: {System.IO.Path.GetFileName(path)} (BepInEx/config/AutoKeeper/dumps)", $"Dump saved: {System.IO.Path.GetFileName(path)} (BepInEx/config/AutoKeeper/dumps)"));
                }
            }
        }
    }

    internal static class EnumerableExtensions
    {
        /// <summary>Count() that does not blow up if the enumerable is null.</summary>
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
