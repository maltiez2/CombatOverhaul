using CombatOverhaul.Animations;
using CombatOverhaul.Colliders;
using CombatOverhaul.Inputs;
using ProtoBuf;
using System.Numerics;
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

public class PickaxeStats
{
    public string ReadyAnimation { get; set; } = "";
    public string IdleAnimation { get; set; } = "";
    public string SwingForwardAnimation { get; set; } = "";
    public string SwingBackAnimation { get; set; } = "";
    public string SwingTpAnimation { get; set; } = "";
    public bool RenderingOffset { get; set; } = false;
    public float[] Collider { get; set; } = Array.Empty<float>();
    public Dictionary<string, string> HitParticleEffects { get; set; } = new();
    public float HitStaggerDurationMs { get; set; } = 100;
}

public enum PickaxeState
{
    Idle,
    SwingForward,
    SwingBack
}

public class PickaxeClient : IClientWeaponLogic
{
    public PickaxeClient(ICoreClientAPI api, Pickaxe item)
    {
        Item = item;
        ItemId = item.Id;
        Stats = item.Attributes.AsObject<PickaxeStats>();
        Api = api;

        Collider = new(Stats.Collider);

        CombatOverhaulSystem system = api.ModLoader.GetModSystem<CombatOverhaulSystem>();
        SoundsSystem = system.ClientSoundsSynchronizer ?? throw new Exception();
        BlockBreakingNetworking = system.ClientBlockBreakingSystem ?? throw new Exception();
        BlockBreakingSystem = new(api);

#if DEBUG
        AnimationsManager.RegisterCollider(item.Code.ToString(), "tool head", value => Collider = value, () => Collider);
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
    }
    public virtual void RenderDebugCollider(ItemSlot inSlot, IClientPlayer byPlayer)
    {
        if (AnimationsManager._currentCollider != null)
        {
            AnimationsManager._currentCollider.Value.Transform(byPlayer.Entity.Pos, byPlayer.Entity, inSlot, Api, right: true)?.Render(Api, byPlayer.Entity, ColorUtil.ColorFromRgba(255, 125, 125, 255));
            return;
        }

        Collider.Transform(byPlayer.Entity.Pos, byPlayer.Entity, inSlot, Api, right: true)?.Render(Api, byPlayer.Entity);
    }

    protected PickaxeStats Stats;
    protected LineSegmentCollider Collider;
    protected ICoreClientAPI Api;
    protected FirstPersonAnimationsBehavior? AnimationBehavior;
    protected ActionsManagerPlayerBehavior? PlayerBehavior;
    protected SoundsSynchronizerClient SoundsSystem;
    protected BlockBreakingSystemClient BlockBreakingNetworking;
    protected BlockBreakingController BlockBreakingSystem;
    protected Pickaxe Item;
    protected TimeSpan SwingStart;
    protected TimeSpan SwingEnd;
    protected TimeSpan ExtraSwingTime;
    protected readonly TimeSpan MaxDelta = TimeSpan.FromSeconds(0.1);

    [ActionEventHandler(EnumEntityAction.LeftMouseDown, ActionState.Active)]
    protected virtual bool Swing(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
    {
        if (eventData.AltPressed && !mainHand) return false;
        if (player.BlockSelection?.Block == null) return false;

        switch ((PickaxeState)state)
        {
            case PickaxeState.Idle:
                {
                    AnimationBehavior?.Play(
                        mainHand,
                        Stats.SwingForwardAnimation,
                        animationSpeed: PlayerBehavior?.ManipulationSpeed ?? 1,
                        category: AnimationCategory(mainHand),
                        callback: () => SwingForwardAnimationCallback(slot, player, mainHand));
                    AnimationBehavior?.PlayVanillaAnimation(Stats.SwingTpAnimation, mainHand);

                    state = (int)PickaxeState.SwingForward;

                    ExtraSwingTime = SwingEnd - SwingStart;
                    SwingStart = TimeSpan.FromMilliseconds(Api.ElapsedMilliseconds);
                    if (SwingStart - SwingEnd > MaxDelta) ExtraSwingTime = TimeSpan.Zero;

                    break;
                }
            case PickaxeState.SwingForward:
                {
                    SwingForward(slot, player, ref state, eventData, mainHand, direction);
                    break;
                }
            case PickaxeState.SwingBack:
                {
                    break;
                }
        }

        return false;
    }
    protected virtual bool SwingForwardAnimationCallback(ItemSlot slot, EntityPlayer player, bool mainHand)
    {
        AnimationBehavior?.Play(
            mainHand,
            Stats.SwingBackAnimation,
            animationSpeed: PlayerBehavior?.ManipulationSpeed ?? 1,
            category: AnimationCategory(mainHand),
            callback: () => SwingBackAnimationCallback(mainHand));
        PlayerBehavior?.SetState((int)PickaxeState.SwingBack, mainHand);
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
        PlayerBehavior?.SetState((int)PickaxeState.Idle, mainHand);
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

        (Block block, Vector3 position, float parameter)? collision = collider.Value.IntersectBlock(Api, selection.Position);

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

    protected static string AnimationCategory(bool mainHand = true) => mainHand ? "main" : "mainOffhand";
    protected virtual float GetMiningSpeed(IItemStack itemStack, BlockSelection blockSel, Block block, EntityPlayer forPlayer)
    {
        float traitRate = 1f;

        EnumBlockMaterial mat = block.GetBlockMaterial(Api.World.BlockAccessor, blockSel.Position);

        if (mat == EnumBlockMaterial.Ore || mat == EnumBlockMaterial.Stone)
        {
            traitRate = forPlayer.Stats.GetBlended("miningSpeedMul");
        }

        if (Item.MiningSpeed == null || !Item.MiningSpeed.ContainsKey(mat)) return traitRate;

        return Item.MiningSpeed[mat] * GlobalConstants.ToolMiningSpeedModifier * traitRate;
    }
}

public class Pickaxe : Item, IHasWeaponLogic, ISetsRenderingOffset, IHasIdleAnimations
{
    public PickaxeClient? Client { get; private set; }

    public IClientWeaponLogic? ClientLogic => Client;
    public bool RenderingOffset { get; private set; }
    public AnimationRequestByCode IdleAnimation { get; private set; }
    public AnimationRequestByCode ReadyAnimation { get; private set; }

    public float BlockBreakDamage { get; set; } = 0;

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);

        if (api is ICoreClientAPI clientAPI)
        {
            Client = new(clientAPI, this);
            PickaxeStats Stats = Attributes.AsObject<PickaxeStats>();
            RenderingOffset = Stats.RenderingOffset;

            IdleAnimation = new(Stats.IdleAnimation, 1, 1, "main", TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), false);
            ReadyAnimation = new(Stats.ReadyAnimation, 1, 1, "main", TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), false);
        }
    }
    public override void OnHeldRenderOpaque(ItemSlot inSlot, IClientPlayer byPlayer)
    {
        base.OnHeldRenderOpaque(inSlot, byPlayer);

        if (AnimationsManager.RenderDebugColliders)
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
}