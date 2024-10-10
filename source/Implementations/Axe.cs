using CombatOverhaul.Animations;
using CombatOverhaul.Colliders;
using CombatOverhaul.Inputs;
using ProtoBuf;
using System.Numerics;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.Client;

namespace CombatOverhaul.Implementations;

public class AxeStats
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

public enum AxeState
{
    Idle,
    SwingForward,
    SwingBack
}

public class AxeClient : IClientWeaponLogic
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
        BlockBreakingSystem = system.ClientBlockBreakingSystem ?? throw new Exception();

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
        AnimationBehavior?.StopVanillaAnimation(Stats.SwingTpAnimation);
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

    protected AxeStats Stats;
    protected LineSegmentCollider Collider;
    protected ICoreClientAPI Api;
    protected FirstPersonAnimationsBehavior? AnimationBehavior;
    protected ActionsManagerPlayerBehavior? PlayerBehavior;
    protected SoundsSynchronizerClient SoundsSystem;
    protected BlockBreakingSystemClient BlockBreakingSystem;
    protected Axe Item;
    protected TimeSpan SwingStart;
    protected TimeSpan SwingEnd;
    protected TimeSpan ExtraSwingTime;
    protected readonly TimeSpan MaxDelta = TimeSpan.FromSeconds(0.1);

    [ActionEventHandler(EnumEntityAction.LeftMouseDown, ActionState.Active)]
    protected virtual bool Swing(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
    {
        if (eventData.AltPressed && !mainHand) return false;

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
                    AnimationBehavior?.PlayVanillaAnimation(Stats.SwingTpAnimation);

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

        return true;
    }
    protected virtual bool SwingForwardAnimationCallback(ItemSlot slot, EntityPlayer player, bool mainHand)
    {
        AnimationBehavior?.Play(
            mainHand,
            Stats.SwingBackAnimation,
            animationSpeed: PlayerBehavior?.ManipulationSpeed ?? 1,
            category: AnimationCategory(mainHand),
            callback: () => SwingBackAnimationCallback(mainHand));
        PlayerBehavior?.SetState((int)AxeState.SwingBack, mainHand);
        AnimationBehavior?.StopVanillaAnimation(Stats.SwingTpAnimation);

        BlockSelection selection = player.BlockSelection;

        if (selection?.Position == null) return true;

        SoundsSystem.Play(selection.Block.Sounds.GetHitSound(Item.Tool ?? EnumTool.Pickaxe).ToString(), randomizedPitch: true);
        TimeSpan currentTime = TimeSpan.FromMilliseconds(Api.ElapsedMilliseconds);
        TimeSpan delta = SwingStart - currentTime + ExtraSwingTime;

        float miningSpeed = GetMiningSpeed(slot.Itemstack, selection, selection.Block, player);

        BlockBreakingSystem.DamageBlock(selection.Position, selection.Face, miningSpeed * (float)delta.TotalSeconds);

        Api.Network.SendPacketClient(ClientPackets.BlockInteraction(selection, 0, 0));

        AnimationBehavior?.SetSpeedModifier(HitImpactFunction);

        SwingStart = currentTime;

        return true;
    }
    protected virtual bool SwingBackAnimationCallback(bool mainHand)
    {
        AnimationBehavior?.PlayReadyAnimation(mainHand);
        PlayerBehavior?.SetState((int)AxeState.Idle, mainHand);
        AnimationBehavior?.StopVanillaAnimation(Stats.SwingTpAnimation);

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

public class AxeServer
{
    public AxeServer(ICoreServerAPI api, Axe item)
    {

    }
}

public class Axe : Item, IHasWeaponLogic, ISetsRenderingOffset, IHasIdleAnimations
{
    public AxeClient? Client { get; private set; }
    public AxeServer? Server { get; private set; }

    public IClientWeaponLogic? ClientLogic => Client;
    public bool RenderingOffset { get; private set; }
    public AnimationRequestByCode IdleAnimation { get; private set; }
    public AnimationRequestByCode ReadyAnimation { get; private set; }

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

        if (AnimationsManager.RenderDebugColliders)
        {
            Client?.RenderDebugCollider(inSlot, byPlayer);
        }
    }

    public override float OnBlockBreaking(IPlayer player, BlockSelection blockSel, ItemSlot itemslot, float remainingResistance, float dt, int counter)
    {
        return remainingResistance;

        bool preventDefault = false;

        foreach (CollectibleBehavior behavior in CollectibleBehaviors)
        {
            EnumHandling handled = EnumHandling.PassThrough;
            float remainingResistanceBh = behavior.OnBlockBreaking(player, blockSel, itemslot, remainingResistance, dt, counter, ref handled);
            if (handled != EnumHandling.PassThrough)
            {
                remainingResistance = remainingResistanceBh;
                preventDefault = true;
            }

            if (handled == EnumHandling.PreventSubsequent) return remainingResistance;
        }

        if (preventDefault) return remainingResistance;


        Block block = player.Entity.World.BlockAccessor.GetBlock(blockSel.Position);
        EnumBlockMaterial mat = block.GetBlockMaterial(api.World.BlockAccessor, blockSel.Position);

        Vec3f faceVec = blockSel.Face.Normalf;
        Random rnd = player.Entity.World.Rand;

        bool cantMine = block.RequiredMiningTier > 0 && itemslot.Itemstack?.Collectible != null && (itemslot.Itemstack.Collectible.ToolTier < block.RequiredMiningTier || (MiningSpeed == null || !MiningSpeed.ContainsKey(mat)));

        double chance = mat == EnumBlockMaterial.Ore ? 0.72 : 0.12;

        if ((counter % 5 == 0) && (rnd.NextDouble() < chance || cantMine) && (mat == EnumBlockMaterial.Stone || mat == EnumBlockMaterial.Ore) && (Tool == EnumTool.Pickaxe || Tool == EnumTool.Hammer))
        {
            double posx = blockSel.Position.X + blockSel.HitPosition.X;
            double posy = blockSel.Position.Y + blockSel.HitPosition.Y;
            double posz = blockSel.Position.Z + blockSel.HitPosition.Z;

            player.Entity.World.SpawnParticles(new SimpleParticleProperties()
            {
                MinQuantity = 0,
                AddQuantity = 8,
                Color = ColorUtil.ToRgba(255, 255, 255, 128),
                MinPos = new Vec3d(posx + faceVec.X * 0.01f, posy + faceVec.Y * 0.01f, posz + faceVec.Z * 0.01f),
                AddPos = new Vec3d(0, 0, 0),
                MinVelocity = new Vec3f(
                    4 * faceVec.X,
                    4 * faceVec.Y,
                    4 * faceVec.Z
                ),
                AddVelocity = new Vec3f(
                    8 * ((float)rnd.NextDouble() - 0.5f),
                    8 * ((float)rnd.NextDouble() - 0.5f),
                    8 * ((float)rnd.NextDouble() - 0.5f)
                ),
                LifeLength = 0.025f,
                GravityEffect = 0f,
                MinSize = 0.03f,
                MaxSize = 0.4f,
                ParticleModel = EnumParticleModel.Cube,
                VertexFlags = 200,
                SizeEvolve = new EvolvingNatFloat(EnumTransformFunction.LINEAR, -0.15f)
            }, player);
        }


        if (cantMine)
        {
            return remainingResistance;
        }

        return remainingResistance - GetMiningSpeed(itemslot.Itemstack, blockSel, block, player) * dt;
    }
}

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public class BlockDamagePacket
{
    public int[] BlockPos { get; set; } = Array.Empty<int>();
    public string Face { get; set; } = "";
    public float Damage { get; set; } = 0;

    public BlockDamagePacket() { }
    public BlockDamagePacket(BlockPos position, BlockFacing facing, float damage)
    {
        BlockPos = new int[4] { position.X, position.Y, position.Z, position.dimension };
        Face = facing.ToString();
        Damage = damage;
    }
    public (BlockPos position, BlockFacing facing, float damage) ToData()
    {
        return (
            new BlockPos(BlockPos[0], BlockPos[1], BlockPos[2], BlockPos[3]),
            BlockFacing.FromCode(Face),
            Damage
            );
    }
}

public class BlockBreakingSystemClient
{
    public const string NetworkChannelId = "CombatOverhaul:blockBreaking";

    public BlockBreakingSystemClient(ICoreClientAPI api)
    {
        _api = api;
        _channel = api.Network.RegisterChannel(NetworkChannelId)
            .RegisterMessageType<BlockDamagePacket>();
    }

    public void DamageBlock(BlockPos position, BlockFacing facing, float damage)
    {
        _api.World.BlockAccessor.DamageBlock(position, facing, damage);
        _channel.SendPacket(new BlockDamagePacket(position, facing, damage));
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
            .RegisterMessageType<BlockDamagePacket>()
            .SetMessageHandler<BlockDamagePacket>(PacketHandler);
    }

    private readonly ICoreServerAPI _api;

    private void PacketHandler(IServerPlayer player, BlockDamagePacket packet)
    {
        (BlockPos position, BlockFacing facing, float damage) = packet.ToData();
        _api.World.BlockAccessor.DamageBlock(position, facing, damage);
    }
}
