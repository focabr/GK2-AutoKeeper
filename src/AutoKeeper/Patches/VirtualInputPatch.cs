using System.Reflection;
using AutoKeeper.Core;
using HarmonyLib;

namespace AutoKeeper.Patches
{
    /// <summary>
    /// Postfix on LazyBearTechnology.LazyInput.Update(): after the game reads keyboard/controller,
    /// adds the bot's virtual keys (see GameApi.Input). No Transpiler, no change to game logic.
    /// </summary>
    [HarmonyPatch]
    internal static class VirtualInputPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method("LazyBearTechnology.LazyInput:Update");
        }

        /// <summary>Skips the patch (with a clear log) if the method does not exist in this game version.</summary>
        private static bool Prepare()
        {
            if (TargetMethod() == null)
            {
                ModLog.Error(Lang.T("LazyInput.Update não encontrado — input virtual indisponível; o bot não conseguirá agir.", "LazyInput.Update not found — virtual input unavailable; the bot will not be able to act."));
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
