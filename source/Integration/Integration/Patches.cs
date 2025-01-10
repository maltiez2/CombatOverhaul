using CombatOverhaul.Animations;
using CombatOverhaul.Colliders;
using CombatOverhaul.Utils;
using HarmonyLib;
using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using System.Reflection.Emit;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using static Microsoft.WindowsAPICodePack.Shell.PropertySystem.SystemProperties.System;

namespace CombatOverhaul.Integration;

internal static class HarmonyPatches
{
    public static event Action<Entity, float>? OnBeforeFrame;
    public static event Action<Entity, ElementPose>? OnFrame;

    public static bool YawSmoothing { get; set; } = false;

    public static void Patch(string harmonyId)
    {
        _animators.Clear();
        _reportedEntities.Clear();
        new Harmony(harmonyId).Patch(
                typeof(EntityShapeRenderer).GetMethod("RenderHeldItem", AccessTools.all),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(HarmonyPatches), nameof(RenderHeldItem)))
            );

        new Harmony(harmonyId).Patch(
                typeof(EntityShapeRenderer).GetMethod("DoRender3DOpaque", AccessTools.all),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(HarmonyPatches), nameof(DoRender3DOpaque)))
            );

        new Harmony(harmonyId).Patch(
                typeof(EntityPlayerShapeRenderer).GetMethod("DoRender3DOpaque", AccessTools.all),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(HarmonyPatches), nameof(DoRender3DOpaquePlayer)))
            );

        new Harmony(harmonyId).Patch(
                typeof(EntityShapeRenderer).GetMethod("BeforeRender", AccessTools.all),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(HarmonyPatches), nameof(BeforeRender)))
            );

        new Harmony(harmonyId).Patch(
                typeof(Vintagestory.API.Common.AnimationManager).GetMethod("OnClientFrame", AccessTools.all),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(HarmonyPatches), nameof(HarmonyPatches.CreateColliders)))
            );

        new Harmony(harmonyId).Patch(
                typeof(EntityPlayerShapeRenderer).GetMethod("smoothCameraTurning", AccessTools.all),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(HarmonyPatches), nameof(HarmonyPatches.SmoothCameraTurning)))
            );

        new Harmony(harmonyId).Patch(
                typeof(EntityBehaviorHealth).GetMethod("OnFallToGround", AccessTools.all),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(HarmonyPatches), nameof(HarmonyPatches.OnFallToGround)))
            );

        new Harmony(harmonyId).Patch(
                typeof(BlockDamageOnTouch).GetMethod("OnEntityInside", AccessTools.all),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(HarmonyPatches), nameof(OnEntityInside)))
            );

        new Harmony(harmonyId).Patch(
                typeof(BlockDamageOnTouch).GetMethod("OnEntityCollide", AccessTools.all),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(HarmonyPatches), nameof(OnEntityCollide)))
            );

        new Harmony(harmonyId).Patch(
                typeof(BagInventory).GetMethod("ReloadBagInventory", AccessTools.all),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(HarmonyPatches), nameof(ReloadBagInventory)))
            );
    }

    public static void Unpatch(string harmonyId)
    {
        new Harmony(harmonyId).Unpatch(typeof(EntityShapeRenderer).GetMethod("RenderHeldItem", AccessTools.all), HarmonyPatchType.Prefix, harmonyId);
        new Harmony(harmonyId).Unpatch(typeof(EntityShapeRenderer).GetMethod("DoRender3DOpaque", AccessTools.all), HarmonyPatchType.Prefix, harmonyId);
        new Harmony(harmonyId).Unpatch(typeof(Vintagestory.API.Common.AnimationManager).GetMethod("OnClientFrame", AccessTools.all), HarmonyPatchType.Prefix, harmonyId);
        new Harmony(harmonyId).Unpatch(typeof(EntityPlayer).GetMethod("OnSelfBeforeRender", AccessTools.all), HarmonyPatchType.Prefix, harmonyId);
        new Harmony(harmonyId).Unpatch(typeof(EntityPlayer).GetMethod("updateEyeHeight", AccessTools.all), HarmonyPatchType.Prefix, harmonyId);
        new Harmony(harmonyId).Unpatch(typeof(EntityPlayerShapeRenderer).GetMethod("smoothCameraTurning", AccessTools.all), HarmonyPatchType.Prefix, harmonyId);
        new Harmony(harmonyId).Unpatch(typeof(EntityBehaviorHealth).GetMethod("OnFallToGround", AccessTools.all), HarmonyPatchType.Prefix, harmonyId);
        new Harmony(harmonyId).Unpatch(typeof(BlockDamageOnTouch).GetMethod("OnEntityInside", AccessTools.all), HarmonyPatchType.Prefix, harmonyId);
        new Harmony(harmonyId).Unpatch(typeof(BlockDamageOnTouch).GetMethod("OnEntityCollide", AccessTools.all), HarmonyPatchType.Prefix, harmonyId);
        new Harmony(harmonyId).Unpatch(typeof(BagInventory).GetMethod("ReloadBagInventory", AccessTools.all), HarmonyPatchType.Prefix, harmonyId);
        _animators.Clear();
        _reportedEntities.Clear();
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

    private static bool CreateColliders(Vintagestory.API.Common.AnimationManager __instance, float dt)
    {
        EntityAgent? entity = (Entity?)_entity?.GetValue(__instance) as EntityAgent;

        if (entity?.Api?.Side != EnumAppSide.Client) return true;

        ClientAnimator? animator = __instance.Animator as ClientAnimator;

        if (animator != null && !_animators.ContainsKey(animator))
        {
            _animators.Add(animator, entity);
            CollidersEntityBehavior? colliders = entity.GetBehavior<CollidersEntityBehavior>();
            List<ElementPose> poses = animator.RootPoses;

            if (colliders != null)
            {
                colliders.Animator = animator;

                try
                {
                    foreach (ElementPose pose in poses)
                    {
                        AddPoseShapeElements(pose, colliders);
                    }

                    if (colliders.ShapeElementsToProcess.Any() && entity.Api.Side == EnumAppSide.Client)
                    {
                        string missingColliders = colliders.ShapeElementsToProcess.Aggregate((first, second) => $"{first}, {second}");
                        LoggerUtil.Warn(entity.Api, typeof(HarmonyPatches), $"({entity.Code}) Listed colliders that were not found in shape: {missingColliders}");
                    }
                }
                catch (Exception exception)
                {
                    LoggerUtil.Error(entity.Api, typeof(HarmonyPatches), $"({entity.Code}) Error during creating colliders: \n{exception}");
                }
                
            }
            else
            {
                LoggerUtil.Debug(entity.Api, typeof(HarmonyPatches), $"Entity '{entity.Code}' does not have colliders behavior");
            }
        }


        // To catch not reproducable bug with nullref in calculateMatrices
        try
        {
            ICoreClientAPI clientApi = entity.Api as ICoreClientAPI;

            if (!clientApi.IsGamePaused && __instance.Animator != null)
            {
                if (__instance.HeadController != null)
                {
                    __instance.HeadController.OnFrame(dt);
                }

                if (entity.IsRendered || entity.IsShadowRendered || !entity.Alive)
                {
                    __instance.Animator.OnFrame(__instance.ActiveAnimationsByAnimCode, dt);

                    if (__instance.Triggers != null)
                    {
                        for (int i = 0; i < __instance.Triggers.Count; i++)
                        {
                            AnimFrameCallback animFrameCallback = __instance.Triggers[i];
                            if (__instance.ActiveAnimationsByAnimCode.ContainsKey(animFrameCallback.Animation))
                            {
                                RunningAnimation animationState = __instance.Animator.GetAnimationState(animFrameCallback.Animation);
                                if (animationState != null && animationState.CurrentFrame >= animFrameCallback.Frame)
                                {
                                    __instance.Triggers.RemoveAt(i);
                                    animFrameCallback.Callback();
                                    i--;
                                }
                            }
                        }
                    }
                }
            }
        }
        catch (Exception exception)
        {
            if (!_reportedEntities.Contains(entity.EntityId))
            {
                LoggerUtil.Error(entity.Api, typeof(HarmonyPatches), $"({entity.Code}) Error during client frame (not directly related to CO): \n{exception}");
                _reportedEntities.Add(entity.EntityId);
            }
        }

        return false;
    }

    internal static readonly Dictionary<ClientAnimator, EntityAgent> _animators = new();
    internal static readonly HashSet<long> _reportedEntities = new();

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

    private static readonly FieldInfo? _smoothedBodyYaw = typeof(EntityPlayerShapeRenderer).GetField("smoothedBodyYaw", BindingFlags.NonPublic | BindingFlags.Instance);
    private static bool SmoothCameraTurning(EntityPlayerShapeRenderer __instance, float bodyYaw, float mdt)
    {
        if (!YawSmoothing)
        {
            _smoothedBodyYaw?.SetValue(__instance, bodyYaw);
            return false;
        }
        else
        {
            return true;
        }
    }

    private const string _fallDamageThresholdMultiplierStat = "fallDamageThreshold";
    private const float _fallDamageMultiplier = 0.2f;
    private const float _fallDamageSpeedThreshold = 0.1f;
    private static bool OnFallToGround(EntityBehaviorHealth __instance, ref Vec3d positionBeforeFalling, ref double withYMotion)
    {
        if ((__instance.entity as EntityAgent)?.ServerControls.Gliding == true)
        {
            return true;
        }
        
        if (__instance.entity is not EntityPlayer player)
        {
            return true;
        }

        double fallDistance = (positionBeforeFalling.Y - player.Pos.Y) / Math.Max(player.Stats.GetBlended(_fallDamageThresholdMultiplierStat), 0.001);

        if (fallDistance < EntityBehaviorHealth.FallDamageFallenDistanceThreshold) return false;

        if (Math.Abs(withYMotion) < _fallDamageSpeedThreshold) return false;

        double fallDamage = Math.Max(0, fallDistance - EntityBehaviorHealth.FallDamageFallenDistanceThreshold) * player.Properties.FallDamageMultiplier * _fallDamageMultiplier;

        player.ReceiveDamage(new DamageSource()
        {
            Source = EnumDamageSource.Fall,
            Type = EnumDamageType.Gravity
        }, (float)fallDamage);

        return false;

        /*if (!__instance.entity.Properties.FallDamage) return false;

        double speedThreshold = Math.Abs(EntityBehaviorHealth.FallDamageYMotionThreshold);
        double speed = Math.Abs(withYMotion);

        if (speed < speedThreshold) return false;

        if (__instance.entity is EntityPlayer player)
        {
            speedThreshold *= Math.Sqrt(player.Stats.GetBlended(_fallDamageMultiplierStat));
        }

        double fallDamage = Math.Sqrt((speed - speedThreshold) / speedThreshold);

        __instance.entity.ReceiveDamage(new DamageSource()
        {
            Source = EnumDamageSource.Fall,
            Type = EnumDamageType.Gravity
        }, (float)fallDamage);

        return false;*/
    }

    private static readonly FieldInfo? _immuneCreatures = typeof(BlockDamageOnTouch).GetField("immuneCreatures", BindingFlags.NonPublic | BindingFlags.Instance);
    private static bool OnEntityInside(BlockDamageOnTouch __instance, IWorldAccessor world, Entity entity, BlockPos pos)
    {
        if (world.Side == EnumAppSide.Server && entity is EntityAgent && (entity as EntityAgent).ServerControls.Sprint && entity.ServerPos.Motion.LengthSq() > 0.001)
        {
            HashSet<AssetLocation>? immuneCreatures = (HashSet<AssetLocation>?)_immuneCreatures?.GetValue(__instance);

            if (immuneCreatures?.Contains(entity.Code) == true) return false;

            if (world.Rand.NextDouble() < 0.2)
            {
                entity.ReceiveDamage(new DamageSource() { Source = EnumDamageSource.Block, SourceBlock = __instance, Type = EnumDamageType.PiercingAttack, SourcePos = pos.ToVec3d() }, __instance.Attributes["sprintIntoDamage"].AsFloat(1));
                entity.ServerPos.Motion.Set(0, 0, 0);
            }
        }

        return false;
    }
    private static bool OnEntityCollide(BlockDamageOnTouch __instance, IWorldAccessor world, Entity entity, BlockPos pos, BlockFacing facing, Vec3d collideSpeed, bool isImpact)
    {
        if (world.Side == EnumAppSide.Server && isImpact && -collideSpeed.Y >= 0.3)
        {
            HashSet<AssetLocation>? immuneCreatures = (HashSet<AssetLocation>?)_immuneCreatures?.GetValue(__instance);

            if (immuneCreatures?.Contains(entity.Code) == true) return false;

            entity.ReceiveDamage(
                new DamageSource() { Source = EnumDamageSource.Block, SourceBlock = __instance, Type = EnumDamageType.PiercingAttack, SourcePos = pos.ToVec3d() },
                (float)Math.Abs(collideSpeed.Y * __instance.Attributes["fallIntoDamageMul"].AsFloat(30) / 2)
            );
        }

        return false;
    }

    private static void ReloadBagInventory(BagInventory __instance, ref InventoryBase parentinv, ref ItemSlot[] bagSlots)
    {
        if (parentinv is not InventoryBasePlayer inventory) return;

        bagSlots = AppendGearInventorySlots(bagSlots, inventory.Owner);
    }
    private static ItemSlot[] AppendGearInventorySlots(ItemSlot[] backpackSlots, Entity owner)
    {
        InventoryBase? inventory = GetGearInventory(owner);

        if (inventory == null) return backpackSlots;

        if (backpackSlots.Any(slot => slot.Inventory == inventory)) return backpackSlots;

        ItemSlot[] gearSlots = inventory?.ToArray() ?? Array.Empty<ItemSlot>();

        return gearSlots.Concat(backpackSlots).ToArray();
    }
    private static InventoryBase? GetGearInventory(Entity entity)
    {
        return entity?.GetBehavior<EntityBehaviorPlayerInventory>()?.Inventory;
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
            MethodInfo onFrameInvokeMethod = AccessTools.Method(typeof(HarmonyPatches), "OnFrameInvoke");
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