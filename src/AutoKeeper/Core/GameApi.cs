using System;
using System.Collections.Generic;
using LazyBearTechnology;
using UnityEngine;

namespace AutoKeeper.Core
{
    /// <summary>
    /// The ONLY place in the mod that touches game classes (adapter). When the game updates, only the
    /// GameApi*.cs files should change. Every public member is guarded by <see cref="Safe{T}"/>: if the game
    /// renames/removes something, we log it clearly ONCE and return a neutral value (the bot stops).
    ///
    /// Part 1 (this file): state reading — read-only, no side effects.
    /// Reverse-engineering notes: docs/game-api-notes.md.
    /// </summary>
    internal static partial class GameApi
    {
        // ------------------------------------------------------------------ infrastructure

        /// <summary>
        /// Runs <paramref name="body"/> guarded against game changes. The lambda body is compiled into a
        /// separate method, so even MissingMethodException/MissingFieldException (JIT) is caught here.
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
                    $"GameApi.{member} falhou / failed ({e.GetType().Name}: {e.Message}). O jogo pode ter mudado — atualize o mod. / The game may have changed — update the mod.");
                return fallback;
            }
        }

        // ------------------------------------------------------------------ general state

        /// <summary>MainGame already exists (any scene, including the menu).</summary>
        public static bool IsMainGameReady => Safe(() => MainGame.Instance != null, false, nameof(IsMainGameReady));

        /// <summary>A save is loaded and the player exists.</summary>
        public static bool IsInGame => Safe(() =>
            MainGame.Instance != null
            && MainGame.Instance.gameState == MainGame.GameState.InGame
            && MainGame.PlayerController != null
            && MainGame.PlayerData != null, false, nameof(IsInGame));

        private static string cachedGameVersion;

        // ------------------------------------------------------------------ save lifecycle

        private static bool lifecycleHooked;
        private static string pendingLifecycleEvent;
        private static object lastWorldToken;

        /// <summary>Subscribes to the game's "game started" (load/new game) and "back to menu" events.</summary>
        public static void HookGameLifecycle()
        {
            if (lifecycleHooked)
            {
                return;
            }
            lifecycleHooked = true;
            Safe(() =>
            {
                MainGame.OnGameStarted += () => pendingLifecycleEvent = Lang.T("partida carregada", "game loaded");
                MainGame.OnGoToMainMenu += () => pendingLifecycleEvent = Lang.T("voltou ao menu principal", "back to the main menu");
                return true;
            }, false, nameof(HookGameLifecycle));
        }

        /// <summary>
        /// Was there a new load / exit to the menu since the last call? Uses the game's events and, as a safeguard,
        /// the replacement of the PlayerData object (each load creates a new one).
        /// </summary>
        public static bool ConsumeWorldChange(out string what)
        {
            what = pendingLifecycleEvent;
            pendingLifecycleEvent = null;
            object token = Safe(() => (object)MainGame.PlayerData, null, nameof(ConsumeWorldChange));
            if (what == null && token != null && lastWorldToken != null && !ReferenceEquals(token, lastWorldToken))
            {
                what = Lang.T("novo load (dados do jogador trocados)", "new load (player data replaced)");
            }
            if (token != null)
            {
                lastWorldToken = token;
            }
            if (what != null)
            {
                LoggedDockTargets.Clear();
                LastDockSpots.Clear();
                LoggedDockFallbacks.Clear();
            }
            return what != null;
        }

        /// <summary>Game version (GameInfo.Version, e.g. "1.007").</summary>
        public static string GetGameVersion()
        {
            if (cachedGameVersion == null)
            {
                cachedGameVersion = Safe(() => LazySingletonSO<GameInfo>.Instance.Version, null, nameof(GetGameVersion));
            }
            return cachedGameVersion;
        }

        // ------------------------------------------------------------------ HUD

        private static readonly List<UnityEngine.UI.Graphic> zoneGraphics = new List<UnityEngine.UI.Graphic>();
        private static readonly Vector3[] zoneCorners = new Vector3[4];
        private static float zoneRectAt = -1f;
        private static Rect zoneRect;

        /// <summary>
        /// Where the game's location name plate (e.g. "Writing Basement", top-right of the HUD) is drawn, in screen
        /// pixels with the origin at the top-left (like IMGUI). Rect.zero when it is not on screen. Only what is
        /// actually visible counts (the plate's images and the text itself), so the town line below it is included
        /// when the game shows it. Read at most 5 times a second.
        /// </summary>
        public static Rect GetZoneLabelRect()
        {
            float now = Time.unscaledTime;
            if (zoneRectAt >= 0f && now - zoneRectAt < 0.2f && now >= zoneRectAt)
            {
                return zoneRect;
            }
            zoneRectAt = now;
            zoneRect = Safe(ZoneLabelRectImpl, Rect.zero, nameof(GetZoneLabelRect));
            return zoneRect;
        }

        private static Rect ZoneLabelRectImpl()
        {
            GUIElements gui = GUIElements.Instance;
            WorldZoneWidget widget = gui != null ? gui.WorldZoneWidget : null;
            if (widget == null || !widget.isActiveAndEnabled)
            {
                return Rect.zero;
            }
            widget.GetComponentsInChildren(false, zoneGraphics);
            float xMin = float.MaxValue, yMin = float.MaxValue, xMax = float.MinValue, yMax = float.MinValue;
            foreach (UnityEngine.UI.Graphic g in zoneGraphics)
            {
                if (g == null || !g.isActiveAndEnabled || g.canvas == null || g.color.a * g.canvasRenderer.GetInheritedAlpha() < 0.05f)
                {
                    continue;
                }
                RectTransform rt = g.rectTransform;
                if (g is TMPro.TMP_Text text)
                {
                    // The text's own box may be much wider than the words: use what is drawn.
                    Bounds b = text.textBounds;
                    if (b.size.x <= 0.01f || b.size.y <= 0.01f)
                    {
                        continue;
                    }
                    zoneCorners[0] = rt.TransformPoint(b.min);
                    zoneCorners[2] = rt.TransformPoint(b.max);
                }
                else
                {
                    rt.GetWorldCorners(zoneCorners);
                }
                Canvas root = g.canvas.rootCanvas;
                Camera cam = root.renderMode == RenderMode.ScreenSpaceOverlay ? null : root.worldCamera;
                for (int i = 0; i <= 2; i += 2)
                {
                    Vector2 p = RectTransformUtility.WorldToScreenPoint(cam, zoneCorners[i]);
                    xMin = Mathf.Min(xMin, p.x);
                    xMax = Mathf.Max(xMax, p.x);
                    yMin = Mathf.Min(yMin, p.y);
                    yMax = Mathf.Max(yMax, p.y);
                }
            }
            zoneGraphics.Clear();
            if (xMax - xMin < 1f || yMax - yMin < 1f)
            {
                return Rect.zero;
            }
            // Screen y grows upwards; IMGUI's y grows downwards.
            return new Rect(xMin, Screen.height - yMax, xMax - xMin, yMax - yMin);
        }

        /// <summary>
        /// null = the player is free (the bot may act). Otherwise, the reason for the automatic pause:
        /// menu, pause, UI window, dialogue/script (ByFlow), cutscene, sleep, teleport, death.
        /// </summary>
        public static string GetBlockReason() => Safe(GetBlockReasonImpl, Lang.T("erro ao ler o estado do jogo", "error reading the game state"), nameof(GetBlockReason));

        private static string GetBlockReasonImpl()
        {
            if (!IsInGame)
            {
                return Lang.T("fora do jogo (menu/carregando)", "not in game (menu/loading)");
            }
            if (ModWindowOpen)
            {
                return Lang.T("configurações do AutoKeeper abertas", "AutoKeeper settings open");
            }
            if (MainGame.IsGamePaused)
            {
                return Lang.T("jogo pausado", "game paused");
            }
            LazyWidgetBase window = LazyWindowsStackController.ActiveWindow;
            if (window != null)
            {
                return Lang.T("janela aberta: ", "window open: ") + window.GetType().Name;
            }
            PlayerController pc = MainGame.PlayerController;
            if (!pc.IsControlEnabledByType(TakenControlType.ByCinematics)) return Lang.T("cinemática", "cutscene");
            if (!pc.IsControlEnabledByType(TakenControlType.ByFlow)) return Lang.T("diálogo/cena roteirizada", "dialogue/scripted scene");
            if (!pc.IsControlEnabledByType(TakenControlType.ByUI)) return "UI";
            if (!pc.IsControlEnabledByType(TakenControlType.BySleep)) return Lang.T("dormindo", "sleeping");
            if (!pc.IsControlEnabledByType(TakenControlType.ByTeleport)) return Lang.T("teleporte", "teleport");
            if (!pc.IsControlEnabledByType(TakenControlType.ByDeath)) return Lang.T("morte", "death");
            if (!pc.IsControlEnabledByType(TakenControlType.ByBuilding)) return Lang.T("modo construção", "build mode");
            if (!LazyInput.IsInputActive())
            {
                return Lang.T("input do jogo desativado", "game input disabled");
            }
            return null;
        }

        /// <summary>Is the game in Portuguese (pt-br)? Used to choose the mod's UI texts.</summary>
        public static bool IsGameLanguagePortuguese() => Safe(() =>
        {
            string lang = (LLBase.CurrentLang ?? "en").ToLowerInvariant();
            return lang.StartsWith("pt") || lang == "br";
        }, false, nameof(IsGameLanguagePortuguese));

        // ------------------------------------------------------------------ player

        public static string GetSceneId() => Safe(() => MainGame.PlayerData.currentGameSceneId, null, nameof(GetSceneId));

        /// <summary>
        /// Id of the world zone the player is in (e.g. "morgue"). The Unity "scene" almost never changes in GK2
        /// (the whole map is RuinedTemple); what changes when walking/teleporting is the zone — the same one the game shows in the corner.
        /// </summary>
        public static string GetZoneId() => Safe(() => MainGame.PlayerData.CurrentWorldZoneData?.id, null, nameof(GetZoneId));

        /// <summary>Zone name in the game's language, same as the top-right corner label (e.g. "Pátio").</summary>
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

        /// <summary>The game's Lack of sleep (`lack_of_sleep_debuff`, 2 days without sleep: half the energy spent turns into insanity).</summary>
        public static bool HasLackOfSleep() => Safe(() => MainGame.Instance.GameSave.perkSystemData.HasPerk("lack_of_sleep_debuff"), false, nameof(HasLackOfSleep));

        /// <summary>The character is sleeping (the game's energy system).</summary>
        public static bool IsSleeping() => Safe(() => MainGame.PlayerData.energySystem.IsSleeping, false, nameof(IsSleeping));

        /// <summary>In-game days since the last sleep (the debuff kicks in at 2). -1 if it cannot be read.</summary>
        public static float GetDaysWithoutSleep() => Safe(() => MainGame.PlayerData.energySystem.timeWithoutSleep, -1f, nameof(GetDaysWithoutSleep));

        /// <summary>Where the player is facing (x,z). While working, the game aligns it to the object's work spot.</summary>
        public static Vector2 GetPlayerFacing() => Safe(() => MainGame.PlayerData.Direction, Vector2.zero, nameof(GetPlayerFacing));

        /// <summary>Current energy (GameRes "energy").</summary>
        public static float GetEnergy() => Safe(() => MainGame.PlayerData.GetRes("energy"), -1f, nameof(GetEnergy));

        /// <summary>Maximum energy (GameRes "energy" definition).</summary>
        public static float GetEnergyMax() => Safe(() => PlayerEnergyGameResSystem.GetSystem().Max, -1f, nameof(GetEnergyMax));

        /// <summary>Any player GameRes (e.g. "insanity", "money", "stamina").</summary>
        public static float GetPlayerRes(string resName) => Safe(() => MainGame.PlayerData.GetRes(resName), -1f, nameof(GetPlayerRes));

        /// <summary>Ids of the items carried "overhead" (bodies, sacks, boxes...).</summary>
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

        /// <summary>Total count of an item in the player's main inventory (without bags/belt).</summary>
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

        // ------------------------------------------------------------------ clock

        /// <summary>Fraction of the day 0..1 (EnvironmentEngine.timeOfDay).</summary>
        public static float GetTimeOfDay() => Safe(() => EnvironmentEngine.Instance.timeOfDay, -1f, nameof(GetTimeOfDay));

        /// <summary>Game clock as "HH:MM" (assumes timeOfDay 0 = 00:00); "?" when unknown.</summary>
        public static string FormatClock(float timeOfDay)
        {
            if (timeOfDay < 0f)
            {
                return "?";
            }
            int minutes = UnityEngine.Mathf.FloorToInt(timeOfDay * 24f * 60f) % (24 * 60);
            return $"{minutes / 60:00}:{minutes % 60:00}";
        }

        /// <summary>Current game clock ("HH:MM"), or null outside a loaded game (menu, loading).</summary>
        public static string GetClockText() => IsInGame ? FormatClock(GetTimeOfDay()) : null;

        /// <summary>Current day of the save (EnvironmentData.Day).</summary>
        public static int GetDay() => Safe(() => MainGame.Instance.GameSave.environmentData.Day, -1, nameof(GetDay));

        /// <summary>Day-of-week index (EnvironmentData.CurrentDayNumber).</summary>
        public static int GetWeekDayNumber() => Safe(() => MainGame.Instance.GameSave.environmentData.CurrentDayNumber, -1, nameof(GetWeekDayNumber));
    }
}
