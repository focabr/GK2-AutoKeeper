using System;
using System.Collections.Generic;
using LazyBearTechnology;
using UnityEngine;

namespace AutoKeeper.Core
{
    /// <summary>
    /// ÚNICO ponto do mod que toca classes do jogo (adapter). Quando o jogo atualizar, só os arquivos
    /// GameApi*.cs devem mudar. Cada membro público é protegido por <see cref="Safe{T}"/>: se o jogo
    /// renomear/remover algo, logamos UMA vez de forma clara e devolvemos um valor neutro (o bot para).
    ///
    /// Parte 1 (este arquivo): leitura de estado — somente leitura, nenhum efeito colateral.
    /// Notas de engenharia reversa: docs/game-api-notes.md.
    /// </summary>
    internal static partial class GameApi
    {
        // ------------------------------------------------------------------ infraestrutura

        /// <summary>
        /// Executa <paramref name="body"/> protegendo contra mudanças do jogo. O corpo do lambda é compilado
        /// em um método separado, então até MissingMethodException/MissingFieldException (JIT) é capturada aqui.
        /// </summary>
        private static T Safe<T>(Func<T> body, T fallback, string member)
        {
            try
            {
                return body();
            }
            catch (Exception e)
            {
                ModLog.WarnOnce("GameApi." + member,
                    $"GameApi.{member} falhou ({e.GetType().Name}: {e.Message}). O jogo pode ter mudado — atualize o mod.");
                return fallback;
            }
        }

        // ------------------------------------------------------------------ estado geral

        /// <summary>MainGame já existe (qualquer cena, inclusive menu).</summary>
        public static bool IsMainGameReady => Safe(() => MainGame.Instance != null, false, nameof(IsMainGameReady));

        /// <summary>Um save está carregado e o jogador existe.</summary>
        public static bool IsInGame => Safe(() =>
            MainGame.Instance != null
            && MainGame.Instance.gameState == MainGame.GameState.InGame
            && MainGame.PlayerController != null
            && MainGame.PlayerData != null, false, nameof(IsInGame));

        private static string cachedGameVersion;

        // ------------------------------------------------------------------ ciclo de vida do save

        private static bool lifecycleHooked;
        private static string pendingLifecycleEvent;
        private static object lastWorldToken;

        /// <summary>Assina os eventos do jogo de "partida iniciada" (load/novo jogo) e "voltou ao menu".</summary>
        public static void HookGameLifecycle()
        {
            if (lifecycleHooked)
            {
                return;
            }
            lifecycleHooked = true;
            Safe(() =>
            {
                MainGame.OnGameStarted += () => pendingLifecycleEvent = "partida carregada";
                MainGame.OnGoToMainMenu += () => pendingLifecycleEvent = "voltou ao menu principal";
                return true;
            }, false, nameof(HookGameLifecycle));
        }

        /// <summary>
        /// Houve um novo load / saída para o menu desde a última chamada? Usa os eventos do jogo e, por garantia,
        /// a troca do objeto PlayerData (cada load cria um novo).
        /// </summary>
        public static bool ConsumeWorldChange(out string what)
        {
            what = pendingLifecycleEvent;
            pendingLifecycleEvent = null;
            object token = Safe(() => (object)MainGame.PlayerData, null, nameof(ConsumeWorldChange));
            if (what == null && token != null && lastWorldToken != null && !ReferenceEquals(token, lastWorldToken))
            {
                what = "novo load (dados do jogador trocados)";
            }
            if (token != null)
            {
                lastWorldToken = token;
            }
            if (what != null)
            {
                LoggedDockTargets.Clear();
            }
            return what != null;
        }

        /// <summary>Versão do jogo (GameInfo.Version, ex.: "1.007").</summary>
        public static string GetGameVersion()
        {
            if (cachedGameVersion == null)
            {
                cachedGameVersion = Safe(() => LazySingletonSO<GameInfo>.Instance.Version, null, nameof(GetGameVersion));
            }
            return cachedGameVersion;
        }

        /// <summary>
        /// null = o jogador está livre (o bot pode agir). Caso contrário, o motivo da pausa automática:
        /// menu, pausa, janela de UI, diálogo/roteiro (ByFlow), cinemática, sono, teleporte, morte.
        /// </summary>
        public static string GetBlockReason() => Safe(GetBlockReasonImpl, "erro ao ler o estado do jogo", nameof(GetBlockReason));

        private static string GetBlockReasonImpl()
        {
            if (!IsInGame)
            {
                return "fora do jogo (menu/carregando)";
            }
            if (ModWindowOpen)
            {
                return "configurações do AutoKeeper abertas";
            }
            if (MainGame.IsGamePaused)
            {
                return "jogo pausado";
            }
            LazyWidgetBase window = LazyWindowsStackController.ActiveWindow;
            if (window != null)
            {
                return "janela aberta: " + window.GetType().Name;
            }
            PlayerController pc = MainGame.PlayerController;
            if (!pc.IsControlEnabledByType(TakenControlType.ByCinematics)) return "cinemática";
            if (!pc.IsControlEnabledByType(TakenControlType.ByFlow)) return "diálogo/cena roteirizada";
            if (!pc.IsControlEnabledByType(TakenControlType.ByUI)) return "UI";
            if (!pc.IsControlEnabledByType(TakenControlType.BySleep)) return "dormindo";
            if (!pc.IsControlEnabledByType(TakenControlType.ByTeleport)) return "teleporte";
            if (!pc.IsControlEnabledByType(TakenControlType.ByDeath)) return "morte";
            if (!pc.IsControlEnabledByType(TakenControlType.ByBuilding)) return "modo construção";
            if (!LazyInput.IsInputActive())
            {
                return "input do jogo desativado";
            }
            return null;
        }

        /// <summary>O jogo está em português (pt-br)? Usado para escolher os textos da UI do mod.</summary>
        public static bool IsGameLanguagePortuguese() => Safe(() =>
        {
            string lang = (LLBase.CurrentLang ?? "en").ToLowerInvariant();
            return lang.StartsWith("pt") || lang == "br";
        }, false, nameof(IsGameLanguagePortuguese));

        // ------------------------------------------------------------------ jogador

        public static string GetSceneId() => Safe(() => MainGame.PlayerData.currentGameSceneId, null, nameof(GetSceneId));

        /// <summary>
        /// Id da zona do mundo onde o jogador está (ex.: "morgue"). A "cena" do Unity quase nunca muda no GK2
        /// (o mapa todo é RuinedTemple); o que muda ao andar/teleportar é a zona — a mesma que o jogo mostra no canto.
        /// </summary>
        public static string GetZoneId() => Safe(() => MainGame.PlayerData.CurrentWorldZoneData?.id, null, nameof(GetZoneId));

        /// <summary>Nome da zona no idioma do jogo, igual ao rótulo do canto superior direito (ex.: "Pátio").</summary>
        public static string GetZoneName() => Safe(() =>
        {
            PlayerData pd = MainGame.PlayerData;
            if (pd.insideTownZones.Count > 0)
            {
                return LLBase.L("town_zone");
            }
            string id = pd.CurrentWorldZoneData?.id;
            return string.IsNullOrEmpty(id) ? null : LLBase.L("wz_" + id);
        }, null, nameof(GetZoneName));

        public static Vector3 GetPlayerPosition() => Safe(() => MainGame.PlayerData.position.Value, Vector3.zero, nameof(GetPlayerPosition));

        /// <summary>Debuff "Falta de sono" do jogo (2 dias sem dormir: energia gasta vira insanidade).</summary>
        public static bool HasLackOfSleep() => Safe(() => MainGame.Instance.GameSave.perkSystemData.HasPerk("lack_of_sleep_debuff"), false, nameof(HasLackOfSleep));

        /// <summary>O personagem está dormindo (sistema de energia do jogo).</summary>
        public static bool IsSleeping() => Safe(() => MainGame.PlayerData.energySystem.IsSleeping, false, nameof(IsSleeping));

        /// <summary>Dias de jogo desde a última vez que dormiu (o debuff entra em 2). -1 se não der para ler.</summary>
        public static float GetDaysWithoutSleep() => Safe(() => MainGame.PlayerData.energySystem.timeWithoutSleep, -1f, nameof(GetDaysWithoutSleep));

        /// <summary>Para onde o jogador está virado (x,z). No trabalho, o jogo alinha ao ponto de trabalho dele.</summary>
        public static Vector2 GetPlayerFacing() => Safe(() => MainGame.PlayerData.Direction, Vector2.zero, nameof(GetPlayerFacing));

        /// <summary>Energia atual (GameRes "energy").</summary>
        public static float GetEnergy() => Safe(() => MainGame.PlayerData.GetRes("energy"), -1f, nameof(GetEnergy));

        /// <summary>Energia máxima (definição do GameRes "energy").</summary>
        public static float GetEnergyMax() => Safe(() => PlayerEnergyGameResSystem.GetSystem().Max, -1f, nameof(GetEnergyMax));

        /// <summary>Qualquer GameRes do jogador (ex.: "insanity", "money", "stamina").</summary>
        public static float GetPlayerRes(string resName) => Safe(() => MainGame.PlayerData.GetRes(resName), -1f, nameof(GetPlayerRes));

        /// <summary>Ids dos itens carregados "sobre a cabeça" (corpos, sacos, caixas...).</summary>
        public static List<string> GetOverheadItemIds() => Safe(() =>
        {
            var list = new List<string>();
            foreach (Item item in MainGame.PlayerData.OverheadItems)
            {
                if (item != null && !item.IsEmpty)
                {
                    list.Add(item.id);
                }
            }
            return list;
        }, new List<string>(), nameof(GetOverheadItemIds));

        /// <summary>Quantidade total de um item no inventário principal do jogador (sem bolsas/cinto).</summary>
        public static int CountPlayerItem(string itemId) => Safe(() =>
        {
            int total = 0;
            foreach (Item item in MainGame.PlayerData.inventory.Data.Inventory)
            {
                if (item != null && item.id == itemId)
                {
                    total += item.Count;
                }
            }
            return total;
        }, 0, nameof(CountPlayerItem));

        // ------------------------------------------------------------------ relógio

        /// <summary>Fração do dia 0..1 (EnvironmentEngine.timeOfDay).</summary>
        public static float GetTimeOfDay() => Safe(() => EnvironmentEngine.Instance.timeOfDay, -1f, nameof(GetTimeOfDay));

        /// <summary>Dia corrente do save (EnvironmentData.Day).</summary>
        public static int GetDay() => Safe(() => MainGame.Instance.GameSave.environmentData.Day, -1, nameof(GetDay));

        /// <summary>Índice do dia da semana (EnvironmentData.CurrentDayNumber).</summary>
        public static int GetWeekDayNumber() => Safe(() => MainGame.Instance.GameSave.environmentData.CurrentDayNumber, -1, nameof(GetWeekDayNumber));
    }
}
