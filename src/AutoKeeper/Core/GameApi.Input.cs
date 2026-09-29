using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using LazyBearTechnology;
using UnityEngine;

namespace AutoKeeper.Core
{
    /// <summary>
    /// Parte 3: input virtual. O bot "aperta" teclas do próprio jogo (GameKey.Interaction = E,
    /// GameKey.Action = segurar para trabalhar). As teclas são acrescentadas às listas internas do
    /// LazyInput logo depois do Update dele (Patches/VirtualInputPatch), então o jogo executa exatamente
    /// o mesmo código de quando o jogador aperta a tecla: regras, energia, ferramenta, animações.
    /// </summary>
    internal static partial class GameApi
    {
        private static int pendingInteractFrames;
        private static int pendingActionFrames;
        private static bool holdAction;
        private static bool actionDownSent;
        private static int pendingHotBarSlot = -1;

        // Janela/painel do mod: bloqueiam o input do jogo para um clique não virar ataque/interação.
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

        /// <summary>O bot está segurando a tecla de ação (trabalhando)?</summary>
        public static bool IsHoldingAction => holdAction;

        /// <summary>"Aperta" E por um frame (interagir com o alvo à frente do jogador).</summary>
        public static void PressInteract()
        {
            pendingInteractFrames = 1;
        }

        /// <summary>"Aperta" a tecla de ação por um frame (ex.: recolher o que o crematório produziu).</summary>
        public static void PressAction()
        {
            pendingActionFrames = 1;
        }

        /// <summary>"Aperta" a tecla da barra rápida (0..3 = teclas 1..4) por um frame: usa o item fixado ali.</summary>
        public static void PressHotBar(int slot)
        {
            pendingHotBarSlot = slot >= 0 && slot < 4 ? slot : -1;
        }

        /// <summary>Segura/solta a tecla de ação (trabalhar no objeto à frente). O primeiro frame também conta como "apertou".</summary>
        public static void SetHoldAction(bool hold)
        {
            if (hold && !holdAction)
            {
                actionDownSent = false;
            }
            holdAction = hold;
        }

        /// <summary>Solta tudo imediatamente (kill switch / pausa / erro).</summary>
        public static void ReleaseAllVirtualKeys()
        {
            pendingInteractFrames = 0;
            pendingActionFrames = 0;
            pendingHotBarSlot = -1;
            holdAction = false;
            actionDownSent = false;
        }

        /// <summary>A janela de configurações do mod está aberta (bot pausa e o jogo não recebe teclas).</summary>
        public static bool ModWindowOpen => modWindowOpen;

        /// <summary>Informado pela UI do mod a cada frame.</summary>
        public static void SetModUiState(bool windowOpen, bool mouseOverUi)
        {
            modWindowOpen = windowOpen;
            mouseOverModUi = mouseOverUi;
        }

        /// <summary>Chamado pelo Postfix de LazyInput.Update. Não faz nada se o jogo desativou o input.</summary>
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
                    // Janela aberta: o jogo não recebe nenhuma tecla/movimento (como uma janela modal do próprio jogo).
                    pressedKeysRef(input).Clear();
                    holdedKeysRef(input).Clear();
                    directionRef(input) = Vector2.zero;
                    direction2Ref(input) = Vector2.zero;
                    return;
                }
                if (mouseOverModUi)
                {
                    // Clique no painel do mod não pode virar ataque/mira no jogo.
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
                ModLog.WarnOnce("VirtualInput", $"Input virtual falhou e foi desativado: {e.GetType().Name}: {e.Message}");
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
            // Métodos privados do LazyInput que já aplicam as regras do jogo (ignora GameKey.None, duplicadas, "esperar soltar").
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
                ModLog.Error($"Campos internos do LazyInput não encontrados ({e.Message}) — input virtual desativado.");
                inputReflectionFailed = true;
                return false;
            }
            if (addPressedMethod == null || addHoldedMethod == null)
            {
                inputReflectionFailed = true;
                ModLog.Error("LazyInput.AddPressed/AddHolded não encontrados — o jogo mudou; input virtual desativado.");
                return false;
            }
            inputReflectionReady = true;
            return true;
        }
    }
}
