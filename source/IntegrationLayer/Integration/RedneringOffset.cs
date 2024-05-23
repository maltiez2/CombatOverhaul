using HarmonyLib;
using System.Reflection;
using System.Reflection.Emit;
using Vintagestory.GameContent;

namespace CombatOverhaul.Integration;

internal static class RenderingOffset
{
    public const float DefaultFpHandsOffset = -0.3f;
    public static float FpHandsOffset { get; set; } = DefaultFpHandsOffset;
    public static float ResetOffset() => FpHandsOffset = DefaultFpHandsOffset;

    
    [HarmonyPatch(typeof(EntityPlayerShapeRenderer), "DoRender3DOpaque")]
    public static class EntityShapeRendererPatch
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
                    codes[i].operand = typeof(RenderingOffset).GetMethod("GetOffset");

                    codes.Insert(i, new CodeInstruction(OpCodes.Ldarg_0));
                    codes.Insert(i + 1, new CodeInstruction(OpCodes.Ldfld, typeof(EntityPlayerShapeRenderer).GetField("modSys", BindingFlags.NonPublic | BindingFlags.Instance)));

                    break;
                }
            }

            return codes;
        }
    }

    [HarmonyPatch(typeof(EntityPlayerShapeRenderer), "getReadyShader")]
    public static class EntityShapeRendererShaderPatch
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
                    codes[i].operand = typeof(RenderingOffset).GetMethod("GetOffset");

                    codes.Insert(i, new CodeInstruction(OpCodes.Ldarg_0));
                    codes.Insert(i + 1, new CodeInstruction(OpCodes.Ldfld, typeof(EntityPlayerShapeRenderer).GetField("modSys", BindingFlags.NonPublic | BindingFlags.Instance)));

                    break;
                }
            }

            return codes;
        }
    }
}