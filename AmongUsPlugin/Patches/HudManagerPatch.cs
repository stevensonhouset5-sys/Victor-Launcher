using HarmonyLib;
using UnityEngine;

namespace AmongUsPlugin.Patches;

[HarmonyPatch(typeof(HudManager), nameof(HudManager.Update))]
internal static class HudManagerPatch
{
    private static bool _attachAttempted;

    [HarmonyPostfix]
    private static void Postfix(HudManager __instance)
    {
        if (!_attachAttempted)
        {
            _attachAttempted = true;
            StarterPlugin.TryAttachManagerUi("HudManager.Update");
        }

        if (!Input.GetKeyDown(KeyCode.F7))
        {
            return;
        }

        StarterPlugin.HandleF7Pressed(__instance);
    }
}
