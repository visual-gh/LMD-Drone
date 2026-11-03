using HarmonyLib;
using Il2CppMegagon.Downhill.Vehicle.Controller;

namespace LMDDrone.Game;

/// <summary>
/// Keeps the bike exactly as it is during a flight. The level's ground follows the camera, so a bike left behind falls through it and crashes in under 2 s (measured). Skipping
/// its step skips all of that: it integrates itself, so nothing else moves it.
/// </summary>
internal static class BikeHold
{
    /// <summary>On from launch to the moment the ground under the bike is back.</summary>
    public static bool On { get; set; }

    [HarmonyPatch(typeof(BikeLocomotion), nameof(BikeLocomotion.FixedUpdate))]
    private static class PhysicsStep
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix() => !On;
    }

    /// <summary>The suicide key and anything else the game could crash it with on its own.</summary>
    [HarmonyPatch(typeof(BikeLocomotion), nameof(BikeLocomotion.Method_Private_Void_CrashReason_0))]
    private static class CrashAttempt
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix() => !On;
    }

    /// <summary>The game's check for the run's first input, asked every frame while waiting. What the drone's pilot presses must not start the clock.</summary>
    [HarmonyPatch(typeof(Il2CppMegagon.Downhill.Input.InputMethod_AdaptiveSwitch), nameof(Il2CppMegagon.Downhill.Input.InputMethod_AdaptiveSwitch.prop_Boolean_4), MethodType.Getter)]
    private static class FirstInput
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(ref bool __result)
        {
            if (On)
            {
                __result = false;
            }
        }
    }
}
