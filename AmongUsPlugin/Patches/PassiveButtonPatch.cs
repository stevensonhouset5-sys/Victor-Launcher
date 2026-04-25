using HarmonyLib;

namespace AmongUsPlugin.Patches;

[HarmonyPatch(typeof(PassiveButton))]
internal static class PassiveButtonPatch
{
    [HarmonyPrefix]
    [HarmonyPatch(nameof(PassiveButton.ReceiveClickDown))]
    [HarmonyPatch(nameof(PassiveButton.ReceiveClickUp))]
    [HarmonyPatch(nameof(PassiveButton.ReceiveClickDownGraphic))]
    [HarmonyPatch(nameof(PassiveButton.ReceiveClickUpGraphic))]
    [HarmonyPatch(nameof(PassiveButton.ReceiveRepeatDown))]
    private static bool BlockClicksWhenLauncherIsOpen()
    {
        return !ModManagerBehaviour.ShouldBlockGameClickthrough();
    }
}
