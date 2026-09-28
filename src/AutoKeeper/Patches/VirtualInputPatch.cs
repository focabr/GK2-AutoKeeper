using System.Reflection;
using AutoKeeper.Core;
using HarmonyLib;

namespace AutoKeeper.Patches
{
    /// <summary>
    /// Postfix em LazyBearTechnology.LazyInput.Update(): depois que o jogo lê teclado/controle,
    /// acrescenta as teclas virtuais do bot (ver GameApi.Input). Sem Transpiler, sem alterar lógica do jogo.
    /// </summary>
    [HarmonyPatch]
    internal static class VirtualInputPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method("LazyBearTechnology.LazyInput:Update");
        }

        /// <summary>Pula o patch (com log claro) se o método não existir nesta versão do jogo.</summary>
        private static bool Prepare()
        {
            if (TargetMethod() == null)
            {
                ModLog.Error("LazyInput.Update não encontrado — input virtual indisponível; o bot não conseguirá agir.");
                return false;
            }
            return true;
        }

        private static void Postfix(object __instance)
        {
            GameApi.InjectVirtualKeys(__instance);
        }
    }
}
