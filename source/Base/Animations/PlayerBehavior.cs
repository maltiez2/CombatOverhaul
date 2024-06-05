using CombatOverhaul.Integration;
using CombatOverhaul.ItemsAnimations;
using System.Numerics;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace CombatOverhaul.PlayerAnimations;

public sealed class FirstPersonAnimationsBehavior : EntityBehavior
{
    public FirstPersonAnimationsBehavior(Entity entity) : base(entity)
    {
        if (entity is not EntityPlayer player) throw new ArgumentException("Only for players");
        _player = player;

        AnimationPatch.OnBeforeFrame += OnBeforeFrame;
        AnimationPatch.OnFrame += OnFrame;
    }

    public override string PropertyName() => "FirstPersonAnimations";

    public override void OnGameTick(float deltaTime)
    {
        int mainHandItemId = _player.RightHandItemSlot.Itemstack?.Item?.Id ?? 0;
        int offhandItemId = _player.LeftHandItemSlot.Itemstack?.Item?.Id ?? 0;

        if (_mainHandItemId != mainHandItemId)
        {
            _mainHandItemId = mainHandItemId;
            foreach (string category in _mainHandCategories)
            {
                _composer.Stop(category);
            }
            _mainHandCategories.Clear();
        }

        if (_offHandItemId != offhandItemId)
        {
            _offHandItemId = offhandItemId;
            foreach (string category in _offhandCategories)
            {
                _composer.Stop(category);
            }
            _offhandCategories.Clear();
        }
    }

    public PlayerItemFrame? FrameOverride { get; set; } = null;

    public void Play(AnimationRequest request, bool mainHand = true)
    {
        _composer.Play(request);
        if (mainHand)
        {
            _mainHandCategories.Add(request.Category);
        }
        else
        {
            _offhandCategories.Add(request.Category);
        }
    }

    private readonly Composer _composer = new();
    private readonly EntityPlayer _player;
    private PlayerItemFrame _lastFrame = PlayerItemFrame.Zero;
    private readonly List<string> _offhandCategories = new();
    private readonly List<string> _mainHandCategories = new();
    private int _offHandItemId = 0;
    private int _mainHandItemId = 0;

    private void OnBeforeFrame(Entity entity, float dt)
    {
        if (!IsOwner(entity)) return;
        
        _lastFrame = _composer.Compose(TimeSpan.FromSeconds(dt / 2));
    }
    private void OnFrame(Entity entity, ElementPose pose)
    {
        if (!IsFirstPerson(entity)) return;

        Animatable? animatable = (entity as EntityAgent)?.RightHandItemSlot?.Itemstack?.Item?.GetCollectibleBehavior(typeof(Animatable), true) as Animatable;

        if (FrameOverride != null)
        {
            ApplyFrame(FrameOverride.Value, entity, pose, animatable);
        }
        else
        {
            ApplyFrame(_lastFrame, entity, pose, animatable);
        }
    }
    private void ApplyFrame(PlayerItemFrame frame, Entity entity, ElementPose pose, Animatable? animatable)
    {
        frame.Apply(pose);

        if (animatable != null && frame.DetachedAnchor)
        {
            animatable.DetachedAnchor = true;
        }

        if (animatable != null && frame.SwitchArms)
        {
            animatable.SwitchArms = true;
        }

        if ((frame.Player.PitchFollow - PlayerFrame.DefaultPitchFollow) >= PlayerFrame.Epsilon)
        {
            if (entity.Properties.Client.Renderer is EntityPlayerShapeRenderer renderer)
            {
                renderer.HeldItemPitchFollowOverride = frame.Player.PitchFollow;
            }
        }
        else
        {
            if (entity.Properties.Client.Renderer is EntityPlayerShapeRenderer renderer)
            {
                renderer.HeldItemPitchFollowOverride = null;
            }
        }
    }
    private static bool IsOwner(Entity entity) => (entity.Api as ICoreClientAPI)?.World.Player.Entity.EntityId == entity.EntityId;
    private static bool IsFirstPerson(Entity entity)
    {
        bool owner = (entity.Api as ICoreClientAPI)?.World.Player.Entity.EntityId == entity.EntityId;
        if (!owner) return false;

        bool firstPerson = entity.Api is ICoreClientAPI { World.Player.CameraMode: EnumCameraMode.FirstPerson };

        return firstPerson;
    }
    public static void SetFirstPersonHandsPitch(IClientPlayer player, float value)
    {
        if (player.Entity.Properties.Client.Renderer is not EntityPlayerShapeRenderer renderer) return;

        renderer.HeldItemPitchFollowOverride = 0.8f * value;
    }
}
