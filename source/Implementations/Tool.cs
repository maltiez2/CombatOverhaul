using CombatOverhaul.Animations;
using CombatOverhaul.Colliders;
using CombatOverhaul.Inputs;
using System.Numerics;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

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
    protected Axe Item;

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
                        callback: () => SwingForwardAnimationCallback(mainHand));
                    AnimationBehavior?.PlayVanillaAnimation(Stats.SwingTpAnimation);

                    state = (int)AxeState.SwingForward;

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
    protected virtual bool SwingForwardAnimationCallback(bool mainHand)
    {
        AnimationBehavior?.Play(
            mainHand,
            Stats.SwingBackAnimation,
            animationSpeed: PlayerBehavior?.ManipulationSpeed ?? 1,
            category: AnimationCategory(mainHand),
            callback: () => SwingBackAnimationCallback(mainHand));
        PlayerBehavior?.SetState((int)AxeState.SwingBack, mainHand);
        AnimationBehavior?.StopVanillaAnimation(Stats.SwingTpAnimation);
        return true;
    }
    protected virtual bool SwingBackAnimationCallback(bool mainHand)
    {
        AnimationBehavior?.PlayReadyAnimation(mainHand);
        PlayerBehavior?.SetState((int)AxeState.Idle, mainHand);
        AnimationBehavior?.StopVanillaAnimation(Stats.SwingTpAnimation);
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

        SwingForwardAnimationCallback(mainHand);
        SoundsSystem.Play(collision.Value.block.Sounds.GetHitSound(Item.Tool ?? EnumTool.Pickaxe).ToString(), randomizedPitch: true);

        AnimationBehavior?.SetSpeedModifier(HitImpactFunction);

        return true;
    }
    protected virtual bool HitImpactFunction(TimeSpan duration, ref TimeSpan delta)
    {
        TimeSpan totalDuration = TimeSpan.FromMilliseconds(Stats.HitStaggerDurationMs);

        delta = TimeSpan.Zero;

        return duration < totalDuration;
    }

    protected static string AnimationCategory(bool mainHand = true) => mainHand ? "main" : "mainOffhand";
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
}

public class ToolSystemClient
{

}

public class ToolSystemServer
{

}
