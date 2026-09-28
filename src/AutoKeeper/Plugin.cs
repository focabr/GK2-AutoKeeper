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
        public const string Version = "0.2.0";

        /// <summary>Versão do jogo em que o mod foi testado (GameInfo.Version).</summary>
        public const string TestedGameVersion = "1.007";

        internal static Plugin Instance { get; private set; }

        internal Settings Settings { get; private set; }
        internal BotController Bot { get; private set; }

        private Harmony harmony;
        private Overlay overlay;
        private bool compatibilityChecked;

        private void Awake()
        {
            Instance = this;
            Settings = new Settings(Config);
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
            Bot.Register(new ProcessBodiesTask(Settings)); // ordem = prioridade
            overlay = new Overlay(Settings, Bot);

            ModLog.Info($"{Name} {Version} carregado. F8 = bot liga/desliga, F9 = overlay, F10 = dump de descoberta (teclas configuráveis).");
        }

        private void Update()
        {
            CheckCompatibilityOnce();
            HandleHotkeys();
            Bot.Update(Time.unscaledDeltaTime);
        }

        private void OnGUI()
        {
            overlay.Draw();
        }

        private void OnDestroy()
        {
            Bot?.Stop("plugin descarregado");
            harmony?.UnpatchSelf();
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
