using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using LazyBearTechnology;
using UnityEngine;

namespace AutoKeeper.Core
{
    /// <summary>
    /// Part 3: virtual input. The bot "presses" the game's own keys (GameKey.Interaction = E,
    /// GameKey.Action = hold to work). The keys are added to LazyInput's internal lists
    /// right after its Update (Patches/VirtualInputPatch), so the game runs exactly
    /// the same code as when the player presses the key: rules, energy, tool, animations.
    /// </summary>
    internal static partial class GameApi
    {
        private static int pendingInteractFrames;
        private static int pendingActionFrames;
        private static bool holdAction;
        private static bool actionDownSent;
        private static int pendingHotBarSlot = -1;

        // Mod window/panel: they block the game's input so a click does not turn into an attack/interaction.
        private static bool modWindowOpen;
        private static bool mouseOverModUi;
        private static AccessTools.FieldRef<LazyInput, List<GameKey>> pressedKeysRef;
        private static AccessTools.FieldRef<LazyInput, List<GameKey>> holdedKeysRef;
        private static AccessTools.FieldRef<LazyInput, Vector2> directionRef;
        private static AccessTools.FieldRef<LazyInput, Vector2> direction2Ref;

        private static MethodInfo addPressedMethod;
        private static MethodInfo addHoldedMethod;
        private static bool inputReflectionReady;
        private static bool inputReflectionFailed;

        /// <summary>Is the bot holding the action key (working)?</summary>
        public static bool IsHoldingAction => holdAction;

        /// <summary>"Presses" E for one frame (interact with the target in front of the player).</summary>
        public static void PressInteract()
        {
            pendingInteractFrames = 1;
        }

        /// <summary>"Presses" the action key for one frame (e.g. collect what the crematorium produced).</summary>
        public static void PressAction()
        {
            pendingActionFrames = 1;
        }

        /// <summary>"Presses" the hot bar key (0..3 = keys 1..4) for one frame: uses the item pinned there.</summary>
        public static void PressHotBar(int slot)
        {
            pendingHotBarSlot = slot >= 0 && slot < 4 ? slot : -1;
        }

        /// <summary>Holds/releases the action key (work on the object in front). The first frame also counts as "pressed".</summary>
        public static void SetHoldAction(bool hold)
        {
            if (hold && !holdAction)
            {
                actionDownSent = false;
            }
            holdAction = hold;
        }

        /// <summary>Releases everything immediately (kill switch / pause / error).</summary>
        public static void ReleaseAllVirtualKeys()
        {
            pendingInteractFrames = 0;
            pendingActionFrames = 0;
            pendingHotBarSlot = -1;
            holdAction = false;
            actionDownSent = false;
        }

        /// <summary>The mod's settings window is open (the bot pauses and the game receives no keys).</summary>
        public static bool ModWindowOpen => modWindowOpen;

        /// <summary>Reported by the mod's UI every frame.</summary>
        public static void SetModUiState(bool windowOpen, bool mouseOverUi)
        {
            modWindowOpen = windowOpen;
            mouseOverModUi = mouseOverUi;
        }

        /// <summary>Called by the LazyInput.Update Postfix. Does nothing if the game disabled input.</summary>
        internal static void InjectVirtualKeys(object lazyInputInstance)
        {
            if (pendingInteractFrames <= 0 && pendingActionFrames <= 0 && pendingHotBarSlot < 0 && !holdAction && !modWindowOpen && !mouseOverModUi)
            {
                return;
            }
            try
            {
                if (!LazyInput.IsInputActive() || !EnsureInputReflection())
                {
                    return;
                }
                var input = (LazyInput)lazyInputInstance;
                if (modWindowOpen)
                {
                    // Window open: the game receives no key/movement (like one of the game's own modal windows).
                    pressedKeysRef(input).Clear();
                    holdedKeysRef(input).Clear();
                    directionRef(input) = Vector2.zero;
                    direction2Ref(input) = Vector2.zero;
                    return;
                }
                if (mouseOverModUi)
                {
                    // A click on the mod panel must not turn into an attack/aim in the game.
                    foreach (GameKey k in new[] { GameKey.LeftClick, GameKey.RightClick, GameKey.DoubleClick, GameKey.Attack, GameKey.AttackFocus })
                    {
                        pressedKeysRef(input).Remove(k);
                        holdedKeysRef(input).Remove(k);
                    }
                }
                if (pendingInteractFrames > 0)
                {
                    addPressedMethod.Invoke(lazyInputInstance, new object[] { GameKey.Interaction });
                    addHoldedMethod.Invoke(lazyInputInstance, new object[] { GameKey.Interaction });
                    pendingInteractFrames--;
                }
                if (pendingActionFrames > 0 && !holdAction)
                {
                    addPressedMethod.Invoke(lazyInputInstance, new object[] { GameKey.Action });
                    addHoldedMethod.Invoke(lazyInputInstance, new object[] { GameKey.Action });
                    pendingActionFrames--;
                }
                if (pendingHotBarSlot >= 0 && !holdAction)
                {
                    GameKey key = HotBarKey(pendingHotBarSlot);
                    addPressedMethod.Invoke(lazyInputInstance, new object[] { key });
                    addHoldedMethod.Invoke(lazyInputInstance, new object[] { key });
                    pendingHotBarSlot = -1;
                }
                if (holdAction)
                {
                    if (!actionDownSent)
                    {
                        addPressedMethod.Invoke(lazyInputInstance, new object[] { GameKey.Action });
                        actionDownSent = true;
                    }
                    addHoldedMethod.Invoke(lazyInputInstance, new object[] { GameKey.Action });
                }
            }
            catch (Exception e)
            {
                ReleaseAllVirtualKeys();
                ModLog.WarnOnce("VirtualInput", Lang.T($"Input virtual falhou e foi desativado: {e.GetType().Name}: {e.Message}", $"Virtual input failed and was disabled: {e.GetType().Name}: {e.Message}"));
            }
        }

        private static GameKey HotBarKey(int slot)
        {
            switch (slot)
            {
                case 0: return GameKey.UseHotBarItem1;
                case 1: return GameKey.UseHotBarItem2;
                case 2: return GameKey.UseHotBarItem3;
                default: return GameKey.UseHotBarItem4;
            }
        }

        private static bool EnsureInputReflection()
        {
            if (inputReflectionReady)
            {
                return true;
            }
            if (inputReflectionFailed)
            {
                return false;
            }
            // LazyInput private methods that already apply the game's rules (ignore GameKey.None, duplicates, "wait for release").
            addPressedMethod = AccessTools.Method(typeof(LazyInput), "AddPressed", new[] { typeof(GameKey) });
            addHoldedMethod = AccessTools.Method(typeof(LazyInput), "AddHolded", new[] { typeof(GameKey) });
            try
            {
                pressedKeysRef = AccessTools.FieldRefAccess<LazyInput, List<GameKey>>("pressedKeys");
                holdedKeysRef = AccessTools.FieldRefAccess<LazyInput, List<GameKey>>("holdedKeys");
                directionRef = AccessTools.FieldRefAccess<LazyInput, Vector2>("direction");
                direction2Ref = AccessTools.FieldRefAccess<LazyInput, Vector2>("direction2");
            }
            catch (Exception e)
            {
                ModLog.Error(Lang.T($"Campos internos do LazyInput não encontrados ({e.Message}) — input virtual desativado.", $"LazyInput internal fields not found ({e.Message}) — virtual input disabled."));
                inputReflectionFailed = true;
                return false;
            }
            if (addPressedMethod == null || addHoldedMethod == null)
            {
                inputReflectionFailed = true;
                ModLog.Error(Lang.T("LazyInput.AddPressed/AddHolded não encontrados — o jogo mudou; input virtual desativado.", "LazyInput.AddPressed/AddHolded not found — the game changed; virtual input disabled."));
                return false;
            }
            inputReflectionReady = true;
            return true;
        }
    }
}
