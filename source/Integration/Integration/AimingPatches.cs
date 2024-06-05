using HarmonyLib;
using System.Numerics;
using System.Reflection;
using Vintagestory.Client.NoObf;

namespace CombatOverhaul.Integration;

internal static class AimingPatches
{
    public delegate void UpdateCameraYawPitchDelegate(ClientMain client, Vector2 mouseDelta, Vector2 delayedMouseDelta, float dt);

    public static bool DrawDefaultReticle { get; set; } = true;
    public static event UpdateCameraYawPitchDelegate? UpdateCameraYawPitch;

    public static void Patch(string harmonyId)
    {
        new Harmony(harmonyId).Patch(typeof(ClientMain).GetMethod("UpdateCameraYawPitch", BindingFlags.Instance | BindingFlags.NonPublic),
            prefix: new HarmonyMethod(AimingPatches.UpdateCameraYawPitchPatch)
            );

        new Harmony(harmonyId).Patch(typeof(SystemRenderAim).GetMethod("DrawAim", BindingFlags.Instance | BindingFlags.NonPublic),
            prefix: new HarmonyMethod(AimingPatches.DrawAim)
            );
    }

    public static void Unpatch(string harmonyId)
    {
        new Harmony(harmonyId).Unpatch(typeof(ClientMain).GetMethod("UpdateCameraYawPitch", BindingFlags.Instance | BindingFlags.NonPublic), HarmonyPatchType.Prefix);
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
        UpdateCameraYawPitch?.Invoke(__instance, new((float)___MouseDeltaX, (float)___MouseDeltaY), new((float)___DelayedMouseDeltaX, (float)___DelayedMouseDeltaY), dt);
    }
}