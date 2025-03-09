using CombatOverhaul.Animations;
using CombatOverhaul.Colliders;
using CombatOverhaul.Inputs;
using CombatOverhaul.MeleeSystems;
using ProtoBuf;
using OpenTK.Mathematics;
using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.Client;
using Vintagestory.Client.NoObf;
using Vintagestory.Common;
using Vintagestory.GameContent;

namespace CombatOverhaul.Implementations;

public class AxeStats
{
    public string ReadyAnimation { get; set; } = "";
    public string IdleAnimation { get; set; } = "";
    public string SwingForwardAnimation { get; set; } = "";
    public string SwingBackAnimation { get; set; } = "";
    public string SplitAnimation { get; set; } = "";
    public string SplitBackAnimation { get; set; } = "";
    public string SwingTpAnimation { get; set; } = "";
    public string SplitTpAnimation { get; set; } = "";
    public bool RenderingOffset { get; set; } = false;
    public float[] Collider { get; set; } = Array.Empty<float>();
    public Dictionary<string, string> HitParticleEffects { get; set; } = new();
    public float HitStaggerDurationMs { get; set; } = 100;
    public bool CanSplitLogs { get; set; } = true;
    public bool HandleLMBInputs { get; set; } = true;

    public MeleeAttackStats Attack { get; set; } = new();
    public string AttackAnimation { get; set; } = "";
    public string AttackTpAnimation { get; set; } = "";
    public float AnimationStaggerOnHitDurationMs { get; set; } = 100;
}

public enum AxeState
{
    Idle,
    SwingForward,
    SwingBack,
    SplittingWindUp,
    Splitting,
    SplittingBack,
    AttackWindup,
    Attacking,
    AttackCooldown
}

public class AxeClient : IClientWeaponLogic, IOnGameTick, IRestrictAction
{
    public AxeClient(ICoreClientAPI api, Axe item)
    {
        Item = item;
        ItemId = item.Id;
        Stats = item.Attributes.AsObject<AxeStats>();
        Api = api;

        Collider = new(Stats.Collider);

        CombatOverhaulSystem system = api.ModLoader.GetModSystem<CombatOverhaulSystem>();
        SoundsSystem = system.ClientSoundsSynchronizer ?? throw new Exception();
        BlockBreakingNetworking = system.ClientBlockBreakingSystem ?? throw new Exception();
        BlockBreakingSystem = new(api);

        MeleeAttack = new(api, Stats.Attack);

#if DEBUG
        DebugWindowManager.RegisterCollider(item.Code.ToString(), "tool head", value => Collider = value, () => Collider);
#endif
    }
    public int ItemId { get; }
    public DirectionsConfiguration DirectionsType => DirectionsConfiguration.None;

    public virtual void OnSelected(ItemSlot slot, EntityPlayer player, bool mainHand, ref int state)
    {

    }
    public virtual void OnDeselected(EntityPlayer player, bool mainHand, ref int state)
    {
        AnimationBehavior?.StopVanillaAnimation(Stats.SwingTpAnimation, mainHand);
    }
    public virtual void OnRegistered(ActionsManagerPlayerBehavior behavior, ICoreClientAPI api)
    {
        PlayerBehavior = behavior;
        AnimationBehavior = behavior.entity.GetBehavior<FirstPersonAnimationsBehavior>();
        TpAnimationBehavior = behavior.entity.GetBehavior<ThirdPersonAnimationsBehavior>();
    }
    public virtual void RenderDebugCollider(ItemSlot inSlot, IClientPlayer byPlayer)
    {
        if (DebugWindowManager._currentCollider != null)
        {
            DebugWindowManager._currentCollider.Value.Transform(byPlayer.Entity.Pos, byPlayer.Entity, inSlot, Api, right: true)?.Render(Api, byPlayer.Entity, ColorUtil.ColorFromRgba(255, 125, 125, 255));
            return;
        }

        Collider.Transform(byPlayer.Entity.Pos, byPlayer.Entity, inSlot, Api, right: true)?.Render(Api, byPlayer.Entity);
    }

    public virtual void OnGameTick(ItemSlot slot, EntityPlayer player, ref int state, bool mainHand)
    {
        if (state == (int)AxeState.Attacking)
        {
            TryAttack(slot, player, mainHand);
        }
    }

    public bool RestrictRightHandAction() => PlayerBehavior?.GetState(mainHand: false) != (int)AxeState.Idle;
    public bool RestrictLeftHandAction() => PlayerBehavior?.GetState(mainHand: true) != (int)AxeState.Idle;

    protected AxeStats Stats;
    protected LineSegmentCollider Collider;
    protected ICoreClientAPI Api;
    protected FirstPersonAnimationsBehavior? AnimationBehavior;
    protected ThirdPersonAnimationsBehavior? TpAnimationBehavior;
    protected ActionsManagerPlayerBehavior? PlayerBehavior;
    protected SoundsSynchronizerClient SoundsSystem;
    protected BlockBreakingSystemClient BlockBreakingNetworking;
    protected BlockBreakingController BlockBreakingSystem;
    protected Axe Item;
    protected TimeSpan SwingStart;
    protected TimeSpan SwingEnd;
    protected TimeSpan ExtraSwingTime;
    protected readonly TimeSpan MaxDelta = TimeSpan.FromSeconds(0.1);
    protected readonly MeleeAttack MeleeAttack;

    [ActionEventHandler(EnumEntityAction.LeftMouseDown, ActionState.Active)]
    protected virtual bool Swing(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
    {
        if (eventData.AltPressed && !mainHand) return false;
        if (player.BlockSelection?.Block == null) return false;
        if (ActionRestricted(player, mainHand)) return false;

        switch ((AxeState)state)
        {
            case AxeState.Idle:
                {
                    AnimationBehavior?.Play(
                        mainHand,
                        Stats.SwingForwardAnimation,
                        animationSpeed: PlayerBehavior?.ManipulationSpeed ?? 1,
                        category: AnimationCategory(mainHand),
                        callback: () => SwingForwardAnimationCallback(slot, player, mainHand));
                    //AnimationBehavior?.PlayVanillaAnimation(Stats.SwingTpAnimation, mainHand);
                    TpAnimationBehavior?.Play(mainHand, Stats.SwingForwardAnimation, AnimationCategory(mainHand), PlayerBehavior?.ManipulationSpeed ?? 1);

                    state = (int)AxeState.SwingForward;

                    ExtraSwingTime = SwingEnd - SwingStart;
                    SwingStart = TimeSpan.FromMilliseconds(Api.ElapsedMilliseconds);
                    if (SwingStart - SwingEnd > MaxDelta) ExtraSwingTime = TimeSpan.Zero;

                    break;
                }
            case AxeState.SwingForward:
                {
                    SwingForward(slot, player, ref state, eventData, mainHand, direction);
                    break;
                }
            case AxeState.SwingBack:
                {
                    break;
                }
        }

        return Stats.HandleLMBInputs;
    }
    protected virtual bool SwingForwardAnimationCallback(ItemSlot slot, EntityPlayer player, bool mainHand)
    {
        AnimationBehavior?.Play(
            mainHand,
            Stats.SwingBackAnimation,
            animationSpeed: PlayerBehavior?.ManipulationSpeed ?? 1,
            category: AnimationCategory(mainHand),
            callback: () => SwingBackAnimationCallback(mainHand));
        TpAnimationBehavior?.Play(mainHand, Stats.SwingBackAnimation, AnimationCategory(mainHand), PlayerBehavior?.ManipulationSpeed ?? 1);
        PlayerBehavior?.SetState((int)AxeState.SwingBack, mainHand);
        AnimationBehavior?.StopVanillaAnimation(Stats.SwingTpAnimation, mainHand);

        BlockSelection selection = player.BlockSelection;

        if (selection?.Position == null) return true;

        SoundsSystem.Play(selection.Block.Sounds.GetHitSound(Item.Tool ?? EnumTool.Pickaxe).ToString(), randomizedPitch: true);
        TimeSpan currentTime = TimeSpan.FromMilliseconds(Api.ElapsedMilliseconds);
        TimeSpan delta = currentTime - SwingStart + ExtraSwingTime;

        float miningSpeed = GetMiningSpeed(slot.Itemstack, selection, selection.Block, player);

        AnimationBehavior?.SetSpeedModifier(HitImpactFunction);

        BlockBreakingSystem?.DamageBlock(selection, selection.Block, miningSpeed * (float)delta.TotalSeconds, Item.Tool ?? 0, Item.ToolTier);

        SwingStart = currentTime;

        return true;
    }
    protected virtual bool SwingBackAnimationCallback(bool mainHand)
    {
        AnimationBehavior?.PlayReadyAnimation(mainHand);
        TpAnimationBehavior?.PlayReadyAnimation(mainHand);
        PlayerBehavior?.SetState((int)AxeState.Idle, mainHand);
        AnimationBehavior?.StopVanillaAnimation(Stats.SwingTpAnimation, mainHand);

        SwingEnd = TimeSpan.FromMilliseconds(Api.ElapsedMilliseconds);

        return true;
    }
    protected virtual bool SwingForward(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
    {
        LineSegmentCollider? collider = Collider.TransformLineSegment(player.Pos, player, slot, Api, mainHand);

        if (collider == null) return false;

        BlockSelection selection = player.BlockSelection;

        if (selection?.Position == null) return false;

        (Block block, Vector3d position, double parameter)? collision = collider.Value.IntersectBlock(Api, selection.Position);

        if (collision == null) return true;

        SwingForwardAnimationCallback(slot, player, mainHand);

        return true;
    }
    protected virtual bool HitImpactFunction(TimeSpan duration, ref TimeSpan delta)
    {
        TimeSpan totalDuration = TimeSpan.FromMilliseconds(Stats.HitStaggerDurationMs);

        delta = TimeSpan.Zero;

        return duration < totalDuration;
    }

    [ActionEventHandler(EnumEntityAction.RightMouseDown, ActionState.Active)]
    protected virtual bool Split(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
    {
        if (eventData.AltPressed && !mainHand) return false;
        if (!Stats.CanSplitLogs) return false;
        if (player.BlockSelection?.Block == null) return false;
        if (!IsSplittable(player.BlockSelection.Block)) return false;
        if (state != (int)AxeState.Idle && state != (int)AxeState.Splitting && state != (int)AxeState.SplittingWindUp) return false;

        if (!Api.World.Claims.TryAccess(Api.World.Player, player.BlockSelection.Position, EnumBlockAccessFlags.BuildOrBreak)) return false; // @TODO add error message "TriggerIngameError"
        if (Api.ModLoader.GetModSystem<ModSystemBlockReinforcement>().IsReinforced(player.BlockSelection.Position)) return false;

        switch ((AxeState)state)
        {
            case AxeState.Idle:
                {
                    AnimationBehavior?.Play(
                        mainHand,
                        Stats.SplitAnimation,
                        animationSpeed: PlayerBehavior?.ManipulationSpeed ?? 1,
                        category: AnimationCategory(mainHand),
                        callback: () => SplitAnimationCallback(slot, player, mainHand),
                        callbackHandler: code => SplitAnimationCallbackHandler(code, mainHand));
                    TpAnimationBehavior?.Play(mainHand, Stats.SplitAnimation, AnimationCategory(mainHand), PlayerBehavior?.ManipulationSpeed ?? 1);
                    //AnimationBehavior?.PlayVanillaAnimation(Stats.SplitTpAnimation, mainHand);

                    state = (int)AxeState.SplittingWindUp;

                    break;
                }
            case AxeState.Splitting:
                {
                    LineSegmentCollider? collider = Collider.TransformLineSegment(player.Pos, player, slot, Api, mainHand);
                    if (collider == null) return true;
                    BlockSelection selection = player.BlockSelection;
                    if (selection?.Position == null) return true;
                    (Block block, Vector3d position, double parameter)? collision = collider.Value.IntersectBlock(Api, selection.Position);
                    if (collision == null) return true;

                    SplitAnimationCallback(slot, player, mainHand);

                    break;
                }
        }
        return true;
    }
    protected virtual bool SplitAnimationCallbackHandler(string code, bool mainHand)
    {
        if (code == "start") PlayerBehavior?.SetState((int)AxeState.Splitting, mainHand);
        return true;
    }
    protected virtual bool SplitAnimationCallback(ItemSlot slot, EntityPlayer player, bool mainHand)
    {
        AnimationBehavior?.StopVanillaAnimation(Stats.SplitTpAnimation, mainHand);
        AnimationBehavior?.SetSpeedModifier(HitImpactFunction);
        AnimationBehavior?.Play(
                        mainHand,
                        Stats.SplitBackAnimation,
                        animationSpeed: PlayerBehavior?.ManipulationSpeed ?? 1,
                        category: AnimationCategory(mainHand),
                        callback: () => SplitBackAnimationCallback(mainHand));
        TpAnimationBehavior?.Play(mainHand, Stats.SplitBackAnimation, AnimationCategory(mainHand), PlayerBehavior?.ManipulationSpeed ?? 1);
        PlayerBehavior?.SetState((int)AxeState.SplittingBack, mainHand);

        BlockSelection selection = player.BlockSelection;
        if (selection?.Position == null) return true;

        Splittable? behavior = selection.Block.GetBehavior<Splittable>();
        if (behavior == null) return true;

        SoundsSystem.Play(selection.Block.Sounds.GetHitSound(Item.Tool ?? EnumTool.Pickaxe).ToString(), randomizedPitch: true);

        Api.World.BlockAccessor.BreakBlock(selection.Position, Api.World.Player, 0f);
        Api.World.BlockAccessor.MarkBlockDirty(selection.Position, Api.World.Player);

        Api.World.SpawnItemEntity(behavior.GetDrop(Api), selection.Position.ToVec3d());

        BlockBreakingNetworking.SplitBlock();

        slot.Itemstack.Item.DamageItem(Api.World, player, slot, 1);

        return true;
    }
    protected virtual bool SplitBackAnimationCallback(bool mainHand)
    {
        AnimationBehavior?.PlayReadyAnimation(mainHand);
        TpAnimationBehavior?.PlayReadyAnimation(mainHand);
        PlayerBehavior?.SetState((int)AxeState.Idle, mainHand);
        return true;
    }

    [ActionEventHandler(EnumEntityAction.LeftMouseDown, ActionState.Active)]
    protected virtual bool Attack(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
    {
        if (eventData.AltPressed && !mainHand) return false;
        if (player.BlockSelection?.Block != null) return false;
        if (ActionRestricted(player, mainHand)) return false;

        switch ((AxeState)state)
        {
            case AxeState.Idle:
                state = (int)AxeState.AttackWindup;
                MeleeAttack.Start(player.Player);
                AnimationBehavior?.Play(
                    mainHand,
                    Stats.AttackAnimation,
                    animationSpeed: PlayerBehavior?.ManipulationSpeed ?? 1,
                    category: AnimationCategory(mainHand),
                    callback: () => AttackAnimationCallback(mainHand),
                    callbackHandler: code => AttackAnimationCallbackHandler(code, mainHand));
                TpAnimationBehavior?.Play(mainHand, Stats.AttackAnimation, AnimationCategory(mainHand), PlayerBehavior?.ManipulationSpeed ?? 1);
                //AnimationBehavior?.PlayVanillaAnimation(Stats.AttackTpAnimation, mainHand);

                return true;
            default:
                return false;
        }
    }
    protected virtual void TryAttack(ItemSlot slot, EntityPlayer player, bool mainHand)
    {
        MeleeAttack.Attack(
            player.Player,
            slot,
            mainHand,
            out IEnumerable<(Block block, Vector3d point)> terrainCollision,
            out IEnumerable<(Vintagestory.API.Common.Entities.Entity entity, Vector3d point)> entitiesCollision);

        if (entitiesCollision.Any() && Stats.AnimationStaggerOnHitDurationMs > 0)
        {
            AnimationBehavior?.SetSpeedModifier(AttackImpactFunction);
        }
    }
    protected virtual bool AttackImpactFunction(TimeSpan duration, ref TimeSpan delta)
    {
        TimeSpan totalDuration = TimeSpan.FromMilliseconds(Stats.AnimationStaggerOnHitDurationMs);

        delta = TimeSpan.Zero;

        return duration < totalDuration;
    }
    protected virtual bool AttackAnimationCallback(bool mainHand)
    {
        AnimationBehavior?.PlayReadyAnimation(mainHand);
        TpAnimationBehavior?.PlayReadyAnimation(mainHand);
        PlayerBehavior?.SetState((int)AxeState.Idle, mainHand);

        return true;
    }
    protected virtual void AttackAnimationCallbackHandler(string callbackCode, bool mainHand)
    {
        switch (callbackCode)
        {
            case "start":
                PlayerBehavior?.SetState((int)AxeState.Attacking, mainHand);
                break;
            case "stop":
                PlayerBehavior?.SetState((int)AxeState.AttackCooldown, mainHand);
                break;
            case "ready":
                PlayerBehavior?.SetState((int)AxeState.Idle, mainHand);
                break;
        }
    }

    protected static string AnimationCategory(bool mainHand = true) => mainHand ? "main" : "mainOffhand";
    protected virtual float GetMiningSpeed(IItemStack itemStack, BlockSelection blockSel, Block block, EntityPlayer forPlayer)
    {
        float traitRate = 1f;

        EnumBlockMaterial mat = block.GetBlockMaterial(Api.World.BlockAccessor, blockSel.Position);

        if (mat == EnumBlockMaterial.Ore || mat == EnumBlockMaterial.Stone)
        {
            traitRate = forPlayer.Stats.GetBlended("miningSpeedMul");
        }

        if (Item.MiningSpeed == null) return 0;

        if (!Item.MiningSpeed.ContainsKey(mat)) return 0;

        return Item.MiningSpeed[mat] * GlobalConstants.ToolMiningSpeedModifier * traitRate * GetIDGMultiplier(itemStack);
    }
    protected virtual bool IsSplittable(Block block) => block.HasBehavior<Splittable>();
    protected virtual float GetIDGMultiplier(IItemStack stack) => stack?.Collectible?.Attributes?["choppingprops"]?["fellingmultiplier"]?.AsFloat(1) ?? 1;

    protected static bool ActionRestricted(EntityPlayer player, bool mainHand = true)
    {
        if (mainHand)
        {
            return (player.LeftHandItemSlot.Itemstack?.Item as IRestrictAction)?.RestrictRightHandAction() ?? false;
        }
        else
        {
            return (player.RightHandItemSlot.Itemstack?.Item as IRestrictAction)?.RestrictLeftHandAction() ?? false;
        }
    }
}

public class AxeServer
{
    public AxeServer(ICoreServerAPI api, Axe item)
    {

    }
}

public class Axe : ItemAxe, IHasWeaponLogic, ISetsRenderingOffset, IHasIdleAnimations, IOnGameTick, IRestrictAction
{
    public AxeClient? Client { get; private set; }
    public AxeServer? Server { get; private set; }

    public IClientWeaponLogic? ClientLogic => Client;
    public bool RenderingOffset { get; private set; }
    public AnimationRequestByCode IdleAnimation { get; private set; }
    public AnimationRequestByCode ReadyAnimation { get; private set; }
    public bool RestrictRightHandAction() => Client?.RestrictRightHandAction() ?? false;
    public bool RestrictLeftHandAction() => Client?.RestrictLeftHandAction() ?? false;

    public float BlockBreakDamage { get; set; } = 0;

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);

        if (api is ICoreClientAPI clientAPI)
        {
            Client = new(clientAPI, this);
            AxeStats Stats = Attributes.AsObject<AxeStats>();
            RenderingOffset = Stats.RenderingOffset;

            IdleAnimation = new(Stats.IdleAnimation, 1, 1, "main", TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), false);
            ReadyAnimation = new(Stats.ReadyAnimation, 1, 1, "main", TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), false);
        }

        if (api is ICoreServerAPI serverAPI)
        {
            Server = new(serverAPI, this);
        }
    }
    public override void OnHeldRenderOpaque(ItemSlot inSlot, IClientPlayer byPlayer)
    {
        base.OnHeldRenderOpaque(inSlot, byPlayer);

        if (DebugWindowManager.RenderDebugColliders)
        {
            Client?.RenderDebugCollider(inSlot, byPlayer);
        }
    }

    public override void OnHeldAttackStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, ref EnumHandHandling handling)
    {
        handling = EnumHandHandling.PreventDefault;
    }
    public override bool OnHeldAttackStep(float secondsPassed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSelection, EntitySelection entitySel)
    {
        return false;
    }

    public override float OnBlockBreaking(IPlayer player, BlockSelection blockSel, ItemSlot itemslot, float remainingResistance, float dt, int counter)
    {
        float result = GameMath.Clamp(remainingResistance - BlockBreakDamage, 0, remainingResistance);
        BlockBreakDamage = 0;

        base.OnBlockBreaking(player, blockSel, itemslot, remainingResistance, dt, counter);

        return result;
    }

    public void OnGameTick(ItemSlot slot, EntityPlayer player, ref int state, bool mainHand) => Client?.OnGameTick(slot, player, ref state, mainHand);
}

public class Splittable : BlockBehavior
{
    public JsonItemStack DroppedItem { get; private set; } = new();

    public Splittable(Block block) : base(block)
    {
    }

    public override void Initialize(JsonObject properties)
    {
        base.Initialize(properties);

        DroppedItem = properties["DroppedItem"].AsObject<JsonItemStack>();
    }

    public ItemStack GetDrop(ICoreAPI api)
    {
        DroppedItem.Resolve(api.World, "CombatOverhaul");
        return DroppedItem.ResolvedItemstack;
    }
}

public sealed class BlockBreakingController
{
    public BlockBreakingController(ICoreClientAPI api)
    {
        _api = api;
        _game = api.World as ClientMain ?? throw new Exception();
        _damagedBlocks = (Dictionary<BlockPos, BlockDamage>?)_clientMain_damagedBlocks?.GetValue(_game) ?? throw new Exception();
    }

    public static float TreeDamageMultiplier { get; set; } = 4;

    public void DamageBlock(BlockSelection blockSelection, Block block, float blockDamage, EnumTool tool, int toolTier) => ContinueBreakSurvival(blockSelection, block, blockDamage, tool, toolTier);

    private readonly ICoreClientAPI _api;
    private BlockDamage? _curBlockDmg;
    private readonly ClientMain _game;
    private int _survivalBreakingCounter;

    private static readonly FieldInfo? _clientMain_damagedBlocks = typeof(ClientMain).GetField("damagedBlocks", BindingFlags.NonPublic | BindingFlags.Instance);

    private static readonly MethodInfo? _clientMain_OnPlayerTryDestroyBlock = typeof(ClientMain).GetMethod("OnPlayerTryDestroyBlock", BindingFlags.Public | BindingFlags.Instance);
    private static readonly MethodInfo? _clientMain_loadOrCreateBlockDamage = typeof(ClientMain).GetMethod("loadOrCreateBlockDamage", BindingFlags.Public | BindingFlags.Instance);
    private static readonly MethodInfo? _clientMain_UpdateCurrentSelection = typeof(ClientMain).GetMethod("UpdateCurrentSelection", BindingFlags.NonPublic | BindingFlags.Instance);

    private readonly Dictionary<BlockPos, BlockDamage> _damagedBlocks;

    private void OnPlayerTryDestroyBlock(BlockSelection blockSelection) => _clientMain_OnPlayerTryDestroyBlock?.Invoke(_game, new object[] { blockSelection });
    private BlockDamage loadOrCreateBlockDamage(BlockSelection blockSelection, Block block, EnumTool? tool, IPlayer byPlayer) => (BlockDamage?)_clientMain_loadOrCreateBlockDamage?.Invoke(_game, new object[] { blockSelection, block, tool, byPlayer }) ?? throw new Exception();
    private void UpdateCurrentSelection() => _clientMain_UpdateCurrentSelection?.Invoke(_game, Array.Empty<object>());

    private void InitBlockBreakSurvival(BlockSelection blockSelection)
    {
        Block block = blockSelection.Block ?? _game.BlockAccessor.GetBlock(blockSelection.Position);
        LoadOrCreateBlockDamage(blockSelection, block);
        _curBlockDmg.LastBreakEllapsedMs = _game.ElapsedMilliseconds;
        _curBlockDmg.BeginBreakEllapsedMs = _game.ElapsedMilliseconds;
    }
    private void ContinueBreakSurvival(BlockSelection blockSelection, Block block, float blockDamage, EnumTool tool, int ToolTier)
    {
        InitBlockBreakSurvival(blockSelection);

        LoadOrCreateBlockDamage(blockSelection, block);
        long elapsedMs = _game.ElapsedMilliseconds;
        int diff = (int)(elapsedMs - _curBlockDmg.LastBreakEllapsedMs);
        long decorBreakPoint = _curBlockDmg.BeginBreakEllapsedMs + 225;
        if (elapsedMs >= decorBreakPoint && _curBlockDmg.LastBreakEllapsedMs < decorBreakPoint && _game.BlockAccessor.GetChunkAtBlockPos(blockSelection.Position) is WorldChunk c)
        {
            BlockPos pos = blockSelection.Position;
            int chunksize = 32;
            c.BreakDecor(_game, pos, blockSelection.Face);
            _game.WorldMap.MarkChunkDirty(pos.X / chunksize, pos.Y / chunksize, pos.Z / chunksize, priority: true);
            _game.SendPacketClient(ClientPackets.BlockInteraction(blockSelection, 2, 0));
        }
        if (tool == EnumTool.Axe)
        {
            FindTree(_api.World, blockSelection.Position, out int resistance, out int woodTier);
            if (resistance > 0)
            {
                if (ToolTier < woodTier - 3)
                {
                    blockDamage *= 0;
                }
                else
                {
                    blockDamage *= _curBlockDmg.Block.Resistance / resistance * TreeDamageMultiplier;
                }
            }
        }

        _curBlockDmg.RemainingResistance -= blockDamage;


        _survivalBreakingCounter++;
        _curBlockDmg.Facing = blockSelection.Face;
        if (_curBlockDmg.Position != blockSelection.Position || _curBlockDmg.Block != block)
        {
            _curBlockDmg.RemainingResistance = block.GetResistance(_game.BlockAccessor, blockSelection.Position);
            _curBlockDmg.Block = block;
            _curBlockDmg.Position = blockSelection.Position;
        }
        if (_curBlockDmg.RemainingResistance <= 0f)
        {
            _game.eventManager.TriggerBlockBroken(_curBlockDmg);
            OnPlayerTryDestroyBlock(blockSelection);
            _damagedBlocks.Remove(blockSelection.Position);
            //UpdateCurrentSelection();
        }
        else
        {
            _game.eventManager.TriggerBlockBreaking(_curBlockDmg);
        }
        _curBlockDmg.LastBreakEllapsedMs = elapsedMs;
    }

    private void ContinueBreakSurvival(BlockSelection blockSelection, Block block)
    {
        LoadOrCreateBlockDamage(blockSelection, block);
        long elapsedMs = _game.ElapsedMilliseconds;
        int diff = (int)(elapsedMs - _curBlockDmg.LastBreakEllapsedMs);
        long decorBreakPoint = _curBlockDmg.BeginBreakEllapsedMs + 225;
        if (elapsedMs >= decorBreakPoint && _curBlockDmg.LastBreakEllapsedMs < decorBreakPoint && _game.BlockAccessor.GetChunkAtBlockPos(blockSelection.Position) is WorldChunk c)
        {
            BlockPos pos = blockSelection.Position;
            int chunksize = 32;
            c.BreakDecor(_game, pos, blockSelection.Face);
            _game.WorldMap.MarkChunkDirty(pos.X / chunksize, pos.Y / chunksize, pos.Z / chunksize, priority: true);
            _game.SendPacketClient(ClientPackets.BlockInteraction(blockSelection, 2, 0));
        }
        //_curBlockDmg.RemainingResistance = block.OnGettingBroken(_api.World.Player, blockSelection, _api.World.Player.Entity.ActiveHandItemSlot, _curBlockDmg.RemainingResistance, (float)diff / 1000f, _survivalBreakingCounter);
        _curBlockDmg.RemainingResistance = _api.World.Player.Entity.ActiveHandItemSlot.Itemstack.Collectible.OnBlockBreaking(_api.World.Player, blockSelection, _api.World.Player.Entity.ActiveHandItemSlot, _curBlockDmg.RemainingResistance, diff / 1000f, _survivalBreakingCounter);
        _survivalBreakingCounter++;
        _curBlockDmg.Facing = blockSelection.Face;
        if (_curBlockDmg.Position != blockSelection.Position || _curBlockDmg.Block != block)
        {
            _curBlockDmg.RemainingResistance = block.GetResistance(_game.BlockAccessor, blockSelection.Position);
            _curBlockDmg.Block = block;
            _curBlockDmg.Position = blockSelection.Position;
        }
        if (_curBlockDmg.RemainingResistance <= 0f)
        {
            _game.eventManager.TriggerBlockBroken(_curBlockDmg);
            OnPlayerTryDestroyBlock(blockSelection);
            _damagedBlocks.Remove(blockSelection.Position);
            //UpdateCurrentSelection();
        }
        else
        {
            _game.eventManager.TriggerBlockBreaking(_curBlockDmg);
        }
        _curBlockDmg.LastBreakEllapsedMs = elapsedMs;
    }

    private void LoadOrCreateBlockDamage(BlockSelection blockSelection, Block block)
    {
        BlockDamage prevDmg = _curBlockDmg;
        EnumTool? tool = _api.World.Player.Entity.ActiveHandItemSlot?.Itemstack?.Collectible?.Tool;
        _curBlockDmg = loadOrCreateBlockDamage(blockSelection, block, tool, _api.World.Player);
        if (prevDmg != null && !prevDmg.Position.Equals(blockSelection.Position))
        {
            _curBlockDmg.LastBreakEllapsedMs = _game.ElapsedMilliseconds;
        }
    }

    public static Stack<BlockPos> FindTree(IWorldAccessor world, BlockPos startPos, out int resistance, out int woodTier)
    {
        Queue<Vec4i> queue = new();
        Queue<Vec4i> queue2 = new();
        HashSet<BlockPos> hashSet = new();
        Stack<BlockPos> stack = new();
        resistance = 0;
        woodTier = 0;
        Block block = world.BlockAccessor.GetBlock(startPos);
        if (block.Code == null)
        {
            return stack;
        }

        string text = block.Attributes?["treeFellingGroupCode"].AsString();
        int num = block.Attributes?["treeFellingGroupSpreadIndex"].AsInt() ?? 0;
        JsonObject attributes = block.Attributes;
        if (attributes != null && !attributes["treeFellingCanChop"].AsBool(defaultValue: true))
        {
            return stack;
        }

        EnumTreeFellingBehavior enumTreeFellingBehavior = EnumTreeFellingBehavior.Chop;
        if (block is ICustomTreeFellingBehavior customTreeFellingBehavior)
        {
            enumTreeFellingBehavior = customTreeFellingBehavior.GetTreeFellingBehavior(startPos, null, num);
            if (enumTreeFellingBehavior == EnumTreeFellingBehavior.NoChop)
            {
                resistance = stack.Count;
                return stack;
            }
        }

        if (num < 2)
        {
            return stack;
        }

        if (text == null)
        {
            return stack;
        }

        queue.Enqueue(new Vec4i(startPos.X, startPos.Y, startPos.Z, num));
        hashSet.Add(startPos);
        int[] array = new int[7];
        while (queue.Count > 0)
        {
            Vec4i vec4i = queue.Dequeue();
            stack.Push(new BlockPos(vec4i.X, vec4i.Y, vec4i.Z, startPos.dimension));
            resistance += vec4i.W + 1;
            if (woodTier == 0)
            {
                woodTier = vec4i.W;
            }

            if (stack.Count > 2500)
            {
                break;
            }

            block = world.BlockAccessor.GetBlock(vec4i.X, vec4i.Y, vec4i.Z, 1);
            if (block is ICustomTreeFellingBehavior customTreeFellingBehavior2)
            {
                enumTreeFellingBehavior = customTreeFellingBehavior2.GetTreeFellingBehavior(startPos, null, num);
            }

            if (enumTreeFellingBehavior != 0)
            {
                onTreeBlock(vec4i, world.BlockAccessor, hashSet, startPos, enumTreeFellingBehavior == EnumTreeFellingBehavior.ChopSpreadVertical, text, queue, queue2, array);
            }
        }

        int num2 = 0;
        int num3 = -1;
        for (int i = 0; i < array.Length; i++)
        {
            if (array[i] > num2)
            {
                num2 = array[i];
                num3 = i;
            }
        }

        if (num3 >= 0)
        {
            text = num3 + 1 + text;
        }

        while (queue2.Count > 0)
        {
            Vec4i vec4i2 = queue2.Dequeue();
            stack.Push(new BlockPos(vec4i2.X, vec4i2.Y, vec4i2.Z, startPos.dimension));
            resistance += vec4i2.W + 1;
            if (stack.Count > 2500)
            {
                break;
            }

            onTreeBlock(vec4i2, world.BlockAccessor, hashSet, startPos, enumTreeFellingBehavior == EnumTreeFellingBehavior.ChopSpreadVertical, text, queue2, null, null);
        }

        return stack;
    }

    private static void onTreeBlock(Vec4i pos, IBlockAccessor blockAccessor, HashSet<BlockPos> checkedPositions, BlockPos startPos, bool chopSpreadVertical, string treeFellingGroupCode, Queue<Vec4i> queue, Queue<Vec4i> leafqueue, int[] adjacentLeaves)
    {
        for (int i = 0; i < Vec3i.DirectAndIndirectNeighbours.Length; i++)
        {
            Vec3i vec3i = Vec3i.DirectAndIndirectNeighbours[i];
            BlockPos blockPos = new(pos.X + vec3i.X, pos.Y + vec3i.Y, pos.Z + vec3i.Z);
            float num = GameMath.Sqrt(blockPos.HorDistanceSqTo(startPos.X, startPos.Z));
            float num2 = blockPos.Y - startPos.Y;
            float num3 = (chopSpreadVertical ? 0.5f : 2f);
            if (num - 1f >= num3 * num2 || checkedPositions.Contains(blockPos))
            {
                continue;
            }

            Block block = blockAccessor.GetBlock(blockPos, 1);
            if (block.Code == null || block.Id == 0)
            {
                continue;
            }

            string text = block.Attributes?["treeFellingGroupCode"].AsString();
            Queue<Vec4i> queue2;
            if (text != treeFellingGroupCode)
            {
                if (text == null || leafqueue == null || block.BlockMaterial != EnumBlockMaterial.Leaves || text.Length != treeFellingGroupCode.Length + 1 || !text.EndsWithOrdinal(treeFellingGroupCode))
                {
                    continue;
                }

                queue2 = leafqueue;
                int num4 = GameMath.Clamp(text[0] - 48, 1, 7);
                adjacentLeaves[num4 - 1]++;
            }
            else
            {
                queue2 = queue;
            }

            int num5 = block.Attributes?["treeFellingGroupSpreadIndex"].AsInt() ?? 0;
            if (pos.W >= num5)
            {
                checkedPositions.Add(blockPos);
                if (!chopSpreadVertical || vec3i.Equals(0, 1, 0) || num5 <= 0)
                {
                    queue2.Enqueue(new Vec4i(blockPos.X, blockPos.Y, blockPos.Z, num5));
                }
            }
        }
    }
}

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public class BlockSplitPacket
{
    public BlockSplitPacket() { }
}

public class BlockBreakingSystemClient
{
    public const string NetworkChannelId = "CombatOverhaul:blockBreaking";

    public BlockBreakingSystemClient(ICoreClientAPI api)
    {
        _api = api;
        _channel = api.Network.RegisterChannel(NetworkChannelId)
            .RegisterMessageType<BlockSplitPacket>();
    }

    public void SplitBlock()
    {
        _channel.SendPacket(new BlockSplitPacket());
    }

    private readonly ICoreClientAPI _api;
    private readonly IClientNetworkChannel _channel;
}

public class BlockBreakingSystemServer
{
    public const string NetworkChannelId = BlockBreakingSystemClient.NetworkChannelId;

    public BlockBreakingSystemServer(ICoreServerAPI api)
    {
        _api = api;
        api.Network.RegisterChannel(NetworkChannelId)
            .RegisterMessageType<BlockSplitPacket>()
            .SetMessageHandler<BlockSplitPacket>(PacketHandler);
    }

    private readonly ICoreServerAPI _api;

    private void PacketHandler(IServerPlayer player, BlockSplitPacket packet)
    {
        BlockSelection selection = player.Entity.BlockSelection;

        if (selection?.Position == null) return;

        Splittable? behavior = selection.Block.GetBehavior<Splittable>();
        if (behavior == null) return;

        _api.World.BlockAccessor.BreakBlock(selection.Position, player, 0f);
        _api.World.BlockAccessor.MarkBlockDirty(selection.Position, player);

        _api.World.SpawnItemEntity(behavior.GetDrop(_api), selection.Position.ToVec3d());

        player.Entity.ActiveHandItemSlot.Itemstack.Item.DamageItem(_api.World, player.Entity, player.Entity.ActiveHandItemSlot, 1);
        player.Entity.ActiveHandItemSlot.MarkDirty();
    }
}
