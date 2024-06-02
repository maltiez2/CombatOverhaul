using CombatOverhaul.Integration;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace CombatOverhaul.PlayerAnimations;

public sealed class FirstPersonAnimationsBehavior : EntityBehavior
{
    public FirstPersonAnimationsBehavior(Entity entity) : base(entity)
    {
        if (entity is not EntityPlayer player) throw new ArgumentException("Only for players");
        _player = player;

        AnimatorPatch.OnBeforeFrame += OnBeforeFrame;
        AnimatorPatch.OnFrame += OnFrame;
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

        if (FrameOverride != null)
        {
            FrameOverride.Value.Apply(pose);
        }
        else
        {
            _lastFrame.Apply(pose);
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
}
