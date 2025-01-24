using CombatOverhaul.Animations;
using HarmonyLib;
using System.Reflection;
using System.Reflection.Emit;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;
using Vintagestory.GameContent;

namespace CombatOverhaul.Integration;

internal static class PlayerRenderingPatches
{
    public static float HandsFovMultiplier { get; set; } = 1;
    public static float FpHandsOffset { get; set; } = DefaultFpHandsOffset;
    public const float DefaultFpHandsOffset = -0.3f;

    public static float ResetOffset() => FpHandsOffset = DefaultFpHandsOffset;
    public static float SetOffset(float offset) => FpHandsOffset = offset;
    public static float GetOffset(ModSystemFpHands modSys) => FpHandsOffset + GameMath.Max(0f, ClientSettings.FieldOfView / 90f - 1f) / 2f;
    public static float GetOffsetAdjusted(ModSystemFpHands modSys) => FpHandsOffset + GameMath.Max(0f, ClientSettings.FieldOfView / 90f - 1f) / 2f;
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
                    codes[i].operand = typeof(PlayerRenderingPatches).GetMethod("GetOffsetAdjusted");

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