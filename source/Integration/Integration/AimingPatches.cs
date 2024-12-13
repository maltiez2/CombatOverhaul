using HarmonyLib;
using System.Numerics;
using System.Reflection;
using System.Reflection.Emit;
using Vintagestory.Client.NoObf;
using Vintagestory.GameContent;

namespace Bullseye.Integration;

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

internal static class PlayerRenderingPatches
{
    public static float HandsFovMultiplier { get; set; } = 1;
    public static float FpHandsOffset { get; set; } = DefaultFpHandsOffset;
    public const float DefaultFpHandsOffset = -0.3f;

    public static float ResetOffset() => FpHandsOffset = DefaultFpHandsOffset;
    public static float SetOffset(float offset) => FpHandsOffset = offset;
    public static float GetOffset(ModSystemFpHands modSys) => FpHandsOffset;
    public static float GetMultiplier() => HandsFovMultiplier;

    [HarmonyPatch(typeof(EntityPlayerShapeRenderer), "DoRender3DOpaque")]
    public class EntityShapeRendererPatch
    {
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new(instructions);
            for (int i = 0; i < codes.Count; i++)
            {
                if (codes[i].opcode == OpCodes.Callvirt && codes[i].operand is MethodInfo propertyInfo && propertyInfo.Name == "get_HandRenderFov")
                {
                    codes.Insert(i + 1, new CodeInstruction(OpCodes.Call, typeof(PlayerRenderingPatches).GetMethod("GetMultiplier")));
                    codes.Insert(i + 2, new CodeInstruction(OpCodes.Mul));
                }

                if (codes[i].opcode == OpCodes.Ldc_R4 && (float)codes[i].operand == -0.3f)
                {
                    codes[i].opcode = OpCodes.Call;
                    codes[i].operand = typeof(PlayerRenderingPatches).GetMethod("GetOffset");

                    codes.Insert(i, new CodeInstruction(OpCodes.Ldarg_0));
                    codes.Insert(i + 1, new CodeInstruction(OpCodes.Ldfld, typeof(EntityPlayerShapeRenderer).GetField("modSys", BindingFlags.NonPublic | BindingFlags.Instance)));

                    break;
                }
            }

            return codes;
        }
    }

    [HarmonyPatch(typeof(EntityPlayerShapeRenderer), "getReadyShader")]
    public class EntityShapeRendererShaderPatch
    {
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new(instructions);
            for (int i = 0; i < codes.Count; i++)
            {
                if (codes[i].opcode == OpCodes.Ldc_R4 && (float)codes[i].operand == -0.3f)
                {
                    codes[i].opcode = OpCodes.Call;
                    codes[i].operand = typeof(PlayerRenderingPatches).GetMethod("GetOffset");

                    codes.Insert(i, new CodeInstruction(OpCodes.Ldarg_0));
                    codes.Insert(i + 1, new CodeInstruction(OpCodes.Ldfld, typeof(EntityPlayerShapeRenderer).GetField("modSys", BindingFlags.NonPublic | BindingFlags.Instance)));

                    break;
                }
            }

            return codes;
        }
    }
}