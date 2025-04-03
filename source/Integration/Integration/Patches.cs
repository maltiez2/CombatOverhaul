using CombatOverhaul.Animations;
using CombatOverhaul.Colliders;
using HarmonyLib;
using System.Reflection;
using System.Reflection.Emit;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace CombatOverhaul.Integration;

internal static class HarmonyPatches
{
    public static event Action<Entity, float>? OnBeforeFrame;
    public static event Action<Entity, ElementPose>? OnFrame;

    public static bool YawSmoothing { get; set; } = false;

    public static void Patch(string harmonyId, ICoreAPI api)
    {
        _animatorsLock.AcquireWriterLock(5000);
        _animators.Clear();
        _animatorsLock.ReleaseWriterLock();
        
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

        new Harmony(harmonyId).Patch(
                typeof(EntityPlayer).GetProperty("LightHsv", AccessTools.all).GetAccessors()[0],
                postfix: new HarmonyMethod(AccessTools.Method(typeof(HarmonyPatches), nameof(LightHsv)))
            );

        _cleanUpTickListener = api.World.RegisterGameTickListener(_ => OnCleanUpTick(), 5 * 60 * 1000, 5 * 60 * 1000);
    }

    public static void Unpatch(string harmonyId, ICoreAPI api)
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
        new Harmony(harmonyId).Unpatch(typeof(EntityPlayer).GetProperty("LightHsv", AccessTools.all).GetAccessors()[0], HarmonyPatchType.Postfix, harmonyId);

        _animatorsLock.AcquireWriterLock(5000);
        _animators.Clear();
        _animatorsLock.ReleaseWriterLock();

        _reportedEntities.Clear();

        api.World.UnregisterGameTickListener(_cleanUpTickListener);
    }

    public static void OnFrameInvoke(ClientAnimator animator, ElementPose pose)
    {
        if (animator == null) return;

        _animatorsLock.AcquireReaderLock(5000);
        if (_animators.TryGetValue(animator, out EntityAgent? entity))
        {
            _animatorsLock.ReleaseReaderLock();
            if (entity is EntityPlayer)
            {
                OnFrame?.Invoke(entity, pose);
            }
        }
        else
        {
            _animatorsLock.ReleaseReaderLock();
        }
    }

    private static readonly FieldInfo? _entity = typeof(Vintagestory.API.Common.AnimationManager).GetField("entity", BindingFlags.NonPublic | BindingFlags.Instance);
    private static long _cleanUpTickListener = 0;

    private static void OnCleanUpTick()
    {
        _animatorsLock.AcquireWriterLock(5000);

        try
        {
            List<ClientAnimator> animatorsToRemove = new();
            foreach (ClientAnimator animator in _animators.Where(entry => !entry.Value.Alive).Select(entry => entry.Key))
            {
                animatorsToRemove.Add(animator);
            }

            foreach (ClientAnimator animator in animatorsToRemove)
            {
                _animators.Remove(animator);
            }
        }
        finally
        {
            _animatorsLock.ReleaseWriterLock();
        }
    }

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

        _animatorsLock.AcquireWriterLock(5000);
        if (animator != null && !_animators.ContainsKey(animator))
        {
            _animators.Add(animator, entity);
        }
        _animatorsLock.ReleaseWriterLock();

        return true;
    }

    internal static readonly Dictionary<ClientAnimator, EntityAgent> _animators = new();
    internal static readonly ReaderWriterLock _animatorsLock = new();
    internal static readonly HashSet<long> _reportedEntities = new();

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
        IInventory? inventory = GetGearInventory(owner);

        if (inventory == null) return backpackSlots;

        if (backpackSlots.Any(slot => slot.Inventory == inventory)) return backpackSlots;

        ItemSlot[] gearSlots = inventory?.ToArray() ?? Array.Empty<ItemSlot>();

        return gearSlots.Concat(backpackSlots).ToArray();
    }
    private static IInventory? GetGearInventory(Entity entity)
    {
        return entity?.GetBehavior<EntityBehaviorPlayerInventory>()?.Inventory;
    }
    private static IInventory? GetBackpackInventory(EntityPlayer player)
    {
        return player.Player.InventoryManager.GetOwnInventory(GlobalConstants.backpackInvClassName);
    }

    private static void LightHsv(EntityPlayer __instance, ref byte[] __result)
    {
        if (__instance?.Player == null || !__instance.Alive || __instance.Player.WorldData.CurrentGameMode == EnumGameMode.Spectator) return;

        if (__result == null) __result = new byte[3] { 0, 0, 0 };

        IInventory? gearInventory = GetGearInventory(__instance);
        if (gearInventory == null) return;

        foreach (ItemSlot slot in gearInventory.Where(slot => slot?.Empty == false).Where(slot => slot.Itemstack?.Collectible.GetCollectibleInterface<IWearableLightSource>() != null))
        {
            AddLight(ref __result, slot.Itemstack.Collectible.GetCollectibleInterface<IWearableLightSource>().GetLightHsv(__instance, slot));
        }

        foreach (ItemSlot slot in gearInventory.Where(slot => slot?.Empty == false).Where(slot => slot.Itemstack?.Collectible?.LightHsv?[2] > 0))
        {
            AddLight(ref __result, slot.Itemstack.Collectible.LightHsv);
        }

        IInventory? backpackInventory = GetBackpackInventory(__instance);
        if (backpackInventory == null) return;

        for (int index = 0; index < 4; index++)
        {
            ItemSlot slot = backpackInventory[index];


            if (slot?.Empty == false && slot.Itemstack?.Collectible.GetCollectibleInterface<IWearableLightSource>() != null)
            {
                AddLight(ref __result, slot.Itemstack.Collectible.GetCollectibleInterface<IWearableLightSource>().GetLightHsv(__instance, slot));
            }

            if (slot?.Empty == false && slot.Itemstack?.Collectible?.LightHsv?[2] > 0)
            {
                AddLight(ref __result, slot.Itemstack.Collectible.LightHsv);
            }
        }
    }

    private static readonly byte[] _lightHsvBuffer = new byte[3] { 0, 0, 0 };
    private static void AddLight(ref byte[] result, byte[] hsv)
    {
        float totalBrightness = result[2] + hsv[2];
        float brightnessFraction = hsv[2] / totalBrightness;

        _lightHsvBuffer[0] = result[0];
        _lightHsvBuffer[1] = result[1];
        _lightHsvBuffer[2] = result[2];

        result = _lightHsvBuffer;

        result[0] = (byte)(hsv[0] * brightnessFraction + result[0] * (1 - brightnessFraction));
        result[1] = (byte)(hsv[1] * brightnessFraction + result[1] * (1 - brightnessFraction));
        result[2] = Math.Max(hsv[2], result[2]);
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

    [HarmonyPatch(typeof(Entity), "ReceiveDamage")]
    [HarmonyPatchCategory("combatoverhaul")]
    public class InvulnerabilityTimePatch
    {
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new(instructions);
            for (int i = 0; i < codes.Count; i++)
            {
                if (codes[i].opcode == OpCodes.Ldstr && codes[i].operand is string activity && activity == "invulnerable")
                {
                    codes[i].operand = "combat-overhaul-invulnerability-patch-fake-activity";
                }
            }

            return codes;
        }
    }
}