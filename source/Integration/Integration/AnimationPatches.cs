using CombatOverhaul.Animations;
using CombatOverhaul.Colliders;
using CombatOverhaul.Utils;
using HarmonyLib;
using System.Reflection;
using System.Reflection.Emit;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace CombatOverhaul.Integration;

internal static class AnimationPatch
{
    public static event Action<Entity, float>? OnBeforeFrame;
    public static event Action<Entity, ElementPose>? OnFrame;

    public static void Patch(string harmonyId)
    {
        _animators.Clear();
        new Harmony(harmonyId).Patch(
                typeof(EntityShapeRenderer).GetMethod("RenderHeldItem", AccessTools.all),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(AnimationPatch), nameof(RenderHeldItem)))
            );

        new Harmony(harmonyId).Patch(
                typeof(EntityShapeRenderer).GetMethod("DoRender3DOpaque", AccessTools.all),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(AnimationPatch), nameof(DoRender3DOpaque)))
            );

        new Harmony(harmonyId).Patch(
                typeof(EntityPlayerShapeRenderer).GetMethod("DoRender3DOpaque", AccessTools.all),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(AnimationPatch), nameof(DoRender3DOpaquePlayer)))
            );

        new Harmony(harmonyId).Patch(
                typeof(EntityShapeRenderer).GetMethod("BeforeRender", AccessTools.all),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(AnimationPatch), nameof(BeforeRender)))
            );

        new Harmony(harmonyId).Patch(
                typeof(Vintagestory.API.Common.AnimationManager).GetMethod("OnClientFrame", AccessTools.all),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(AnimationPatch), nameof(AnimationPatch.CreateColliders)))
            );
    }

    public static void Unpatch(string harmonyId)
    {
        new Harmony(harmonyId).Unpatch(typeof(EntityShapeRenderer).GetMethod("RenderHeldItem", AccessTools.all), HarmonyPatchType.Prefix, harmonyId);
        new Harmony(harmonyId).Unpatch(typeof(EntityShapeRenderer).GetMethod("DoRender3DOpaque", AccessTools.all), HarmonyPatchType.Prefix, harmonyId);
        new Harmony(harmonyId).Unpatch(typeof(Vintagestory.API.Common.AnimationManager).GetMethod("OnClientFrame", AccessTools.all), HarmonyPatchType.Prefix, harmonyId);
        new Harmony(harmonyId).Unpatch(typeof(EntityPlayer).GetMethod("OnSelfBeforeRender", AccessTools.all), HarmonyPatchType.Prefix, harmonyId);
        new Harmony(harmonyId).Unpatch(typeof(EntityPlayer).GetMethod("updateEyeHeight", AccessTools.all), HarmonyPatchType.Prefix, harmonyId);
        _animators.Clear();
    }

    public static void OnFrameInvoke(ClientAnimator animator, ElementPose pose)
    {
        if (!_animators.ContainsKey(animator)) return;

        OnFrame?.Invoke(_animators[animator], pose);
    }

    private static readonly FieldInfo? _entity = typeof(Vintagestory.API.Common.AnimationManager).GetField("entity", BindingFlags.NonPublic | BindingFlags.Instance);

    private static void BeforeRender(EntityShapeRenderer __instance, float dt)
    {
        OnBeforeFrame?.Invoke(__instance.entity, dt);
    }

    private static void DoRender3DOpaque(EntityShapeRenderer __instance, float dt, bool isShadowPass)
    {
        CollidersEntityBehavior behavior = __instance.entity.GetBehavior<CollidersEntityBehavior>();
        behavior?.Render(__instance.entity.Api as ICoreClientAPI, __instance.entity as EntityAgent, __instance);
    }

    private static void DoRender3DOpaquePlayer(EntityPlayerShapeRenderer __instance, float dt, bool isShadowPass)
    {
        CollidersEntityBehavior behavior = __instance.entity.GetBehavior<CollidersEntityBehavior>();
        behavior?.Render(__instance.entity.Api as ICoreClientAPI, __instance.entity as EntityAgent, __instance);
    }

    private static void CreateColliders(Vintagestory.API.Common.AnimationManager __instance, float dt)
    {
        EntityAgent? entity = (Entity?)_entity?.GetValue(__instance) as EntityAgent;

        if (entity?.Api?.Side != EnumAppSide.Client) return;

        ClientAnimator? animator = __instance.Animator as ClientAnimator;

        if (animator != null && !_animators.ContainsKey(animator))
        {
            _animators.Add(animator, entity);
            CollidersEntityBehavior? colliders = entity.GetBehavior<CollidersEntityBehavior>();
            List<ElementPose> poses = animator.RootPoses;

            if (colliders != null)
            {
                colliders.Animator = animator;

                foreach (ElementPose pose in poses)
                {
                    AddPoseShapeElements(pose, colliders);
                }

                if (colliders.ShapeElementsToProcess.Any() && entity.Api.Side == EnumAppSide.Client)
                {
                    string missingColliders = colliders.ShapeElementsToProcess.Aggregate((first, second) => $"{first}, {second}");
                    LoggerUtil.Warn(entity.Api, typeof(AnimationPatch), $"({entity.GetName()}) Listed colliders that was not found in shape: {missingColliders}");
                }
            }
        }
    }

    internal static readonly Dictionary<ClientAnimator, EntityAgent> _animators = new();

    private static void AddPoseShapeElements(ElementPose pose, CollidersEntityBehavior colliders)
    {
        colliders.SetColliderElement(pose.ForElement);

        foreach (ElementPose childPose in pose.ChildElementPoses)
        {
            AddPoseShapeElements(childPose, colliders);
        }
    }

    private static bool RenderHeldItem(EntityShapeRenderer __instance, float dt, bool isShadowPass, bool right)
    {
        //if (isShadowPass) return true;

        ItemSlot? slot;

        if (right)
        {
            slot = (__instance.entity as EntityPlayer)?.RightHandItemSlot;
        }
        else
        {
            slot = (__instance.entity as EntityPlayer)?.LeftHandItemSlot;
        }

        if (slot?.Itemstack?.Item == null) return true;

        Animatable? behavior = slot.Itemstack.Item.GetCollectibleBehavior(typeof(Animatable), true) as Animatable;

        if (behavior == null) return true;

        ItemRenderInfo renderInfo = __instance.capi.Render.GetItemStackRenderInfo(slot, EnumItemRenderTarget.HandTp, dt);

        behavior.BeforeRender(__instance.capi, slot.Itemstack, __instance.entity, EnumItemRenderTarget.HandFp, dt);

        (string textureName, _) = slot.Itemstack.Item.Textures.First();

        TextureAtlasPosition atlasPos = __instance.capi.ItemTextureAtlas.GetPosition(slot.Itemstack.Item, textureName);

        renderInfo.TextureId = atlasPos.atlasTextureId;

        Vec4f? lightrgbs = (Vec4f?)typeof(EntityShapeRenderer)
                                          .GetField("lightrgbs", BindingFlags.NonPublic | BindingFlags.Instance)
                                          ?.GetValue(__instance);

        return !behavior.RenderHeldItem(__instance.ModelMat, __instance.capi, slot, __instance.entity, lightrgbs, dt, isShadowPass, right, renderInfo);
    }

    [HarmonyPatch(typeof(ClientAnimator), "calculateMatrices", typeof(int),
        typeof(float),
        typeof(List<ElementPose>),
        typeof(ShapeElementWeights[][]),
        typeof(float[]),
        typeof(List<ElementPose>[]),
        typeof(List<ElementPose>[]),
        typeof(int))]
    [HarmonyPatchCategory("combatoverhaul")]
    public class ClientAnimatorCalculateMatricesPatch
    {
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> code = new(instructions);
            MethodInfo onFrameInvokeMethod = AccessTools.Method(typeof(AnimationPatch), "OnFrameInvoke");
            MethodInfo getLocalTransformMatrixMethod = AccessTools.Method(typeof(ShapeElement), "GetLocalTransformMatrix");

            for (int i = 0; i < code.Count; i++)
            {
                if (code[i].Calls(getLocalTransformMatrixMethod))
                {
                    code.Insert(i, new CodeInstruction(OpCodes.Ldarg_0)); // Load own class onto the stack.
                    code.Insert(i + 1, new CodeInstruction(OpCodes.Ldloc, 4)); // Load shape.
                    code.Insert(i + 2, new CodeInstruction(OpCodes.Call, onFrameInvokeMethod));
                    break;
                }
            }

            return code;
        }
    }
}