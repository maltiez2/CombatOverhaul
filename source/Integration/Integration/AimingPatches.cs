using HarmonyLib;
using OpenTK.Mathematics;
using System.Reflection;
using Vintagestory.Client.NoObf;

namespace CombatOverhaul.Integration;

internal static class AimingPatches
{
    public delegate void UpdateCameraYawPitchDelegate(ClientMain __instance, ref double ___MouseDeltaX, ref double ___MouseDeltaY, ref double ___DelayedMouseDeltaX, ref double ___DelayedMouseDeltaY, float dt);

    public static bool DrawDefaultReticle { get; set; } = true;
    public static event UpdateCameraYawPitchDelegate? UpdateCameraYawPitch;

    public static void Patch(string harmonyId)
    {
        new Harmony(harmonyId).Patch(typeof(ClientMain).GetMethod("UpdateCameraYawPitch", BindingFlags.Instance | BindingFlags.Public),
            prefix: new HarmonyMethod(AimingPatches.UpdateCameraYawPitchPatch)
            );

        new Harmony(harmonyId).Patch(typeof(SystemRenderAim).GetMethod("DrawAim", BindingFlags.Instance | BindingFlags.NonPublic),
            prefix: new HarmonyMethod(AimingPatches.DrawAim)
            );
    }

    public static void Unpatch(string harmonyId)
    {
        new Harmony(harmonyId).Unpatch(typeof(ClientMain).GetMethod("UpdateCameraYawPitch", BindingFlags.Instance | BindingFlags.Public), HarmonyPatchType.Prefix);
        new Harmony(harmonyId).Unpatch(typeof(SystemRenderAim).GetMethod("DrawAim", BindingFlags.Instance | BindingFlags.NonPublic), HarmonyPatchType.Prefix);
    }

    private static bool DrawAim()
    {
        return DrawDefaultReticle;
    }

    private static void UpdateCameraYawPitchPatch(ClientMain __instance,
            ref double ___MouseDeltaX, ref double ___MouseDeltaY,
            ref double ___DelayedMouseDeltaX, ref double ___DelayedMouseDeltaY,
            float dt)
    {
        UpdateCameraYawPitch?.Invoke(__instance, ref ___MouseDeltaX, ref ___MouseDeltaY, ref ___DelayedMouseDeltaX, ref ___DelayedMouseDeltaY, dt);
    }
}