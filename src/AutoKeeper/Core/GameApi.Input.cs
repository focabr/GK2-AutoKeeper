using System;
using System.Reflection;
using HarmonyLib;
using LazyBearTechnology;

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
            holdAction = false;
            actionDownSent = false;
        }

        /// <summary>Chamado pelo Postfix de LazyInput.Update. Não faz nada se o jogo desativou o input.</summary>
        internal static void InjectVirtualKeys(object lazyInputInstance)
        {
            if (pendingInteractFrames <= 0 && pendingActionFrames <= 0 && !holdAction)
            {
                return;
            }
            try
            {
                if (!LazyInput.IsInputActive() || !EnsureInputReflection())
                {
                    return;
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
