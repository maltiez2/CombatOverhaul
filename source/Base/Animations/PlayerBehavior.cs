using CombatOverhaul.Integration;
using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;
using Vintagestory.GameContent;

namespace CombatOverhaul.Animations;

public interface IHasIdleAnimations
{
    AnimationRequestByCode IdleAnimation { get; }
    AnimationRequestByCode ReadyAnimation { get; }
}

public sealed class FirstPersonAnimationsBehavior : EntityBehavior
{
    public FirstPersonAnimationsBehavior(Entity entity) : base(entity)
    {
        if (entity is not EntityPlayer player) throw new ArgumentException("Only for players");
        _player = player;
        _api = player.Api as ICoreClientAPI;
        _animationsManager = player.Api.ModLoader.GetModSystem<CombatOverhaulAnimationsSystem>().PlayerAnimationsManager;

        SoundsSynchronizerClient soundsManager = player.Api.ModLoader.GetModSystem<CombatOverhaulSystem>().ClientSoundsSynchronizer ?? throw new Exception();
        _composer = new(soundsManager);

        AnimationPatch.OnBeforeFrame += OnBeforeFrame;
        AnimationPatch.OnFrame += OnFrame;
    }

    public override string PropertyName() => "FirstPersonAnimations";

    public override void OnGameTick(float deltaTime) // @TODO refactor this brunching hell
    {
        int mainHandItemId = _player.RightHandItemSlot.Itemstack?.Item?.Id ?? 0;
        int offhandItemId = _player.LeftHandItemSlot.Itemstack?.Item?.Id ?? 0;

        if (_mainHandItemId != mainHandItemId)
        {
            _mainHandItemId = mainHandItemId;
            StopIdleTimer(true);

            if (_player.RightHandItemSlot.Itemstack?.Item is IHasIdleAnimations item)
            {
                string readyCategory = item.ReadyAnimation.Category;

                foreach (string category in _mainHandCategories.Where(element => element != readyCategory))
                {
                    _composer.Stop(category);
                }
                _mainHandCategories.Clear();

                Play(item.ReadyAnimation, true);
                StartIdleTimer(item.IdleAnimation, true);
            }
            else
            {
                foreach (string category in _mainHandCategories)
                {
                    _composer.Stop(category);
                }
                _mainHandCategories.Clear();
            }
        }

        if (_offHandItemId != offhandItemId)
        {
            _offHandItemId = offhandItemId;
            StopIdleTimer(false);

            if (_player.LeftHandItemSlot.Itemstack?.Item is IHasIdleAnimations item)
            {
                string readyCategory = item.ReadyAnimation.Category;

                foreach (string category in _offhandCategories.Where(element => element != readyCategory))
                {
                    _composer.Stop(category);
                }
                _offhandCategories.Clear();

                Play(item.ReadyAnimation, false);
                StartIdleTimer(item.IdleAnimation, false);
            }
            else
            {
                foreach (string category in _offhandCategories)
                {
                    _composer.Stop(category);
                }
                _offhandCategories.Clear();
            }
        }
    }

    public PlayerItemFrame? FrameOverride { get; set; } = null;
    public static float CurrentFov { get; set; } = ClientSettings.FieldOfView;

    public void Play(AnimationRequest request, bool mainHand = true)
    {
        _composer.Play(request);
        StopIdleTimer(mainHand, request.Category);
        if (mainHand)
        {
            _mainHandCategories.Add(request.Category);
        }
        else
        {
            _offhandCategories.Add(request.Category);
        }
    }
    public void Play(AnimationRequestByCode requestByCode, bool mainHand = true)
    {
        if (_animationsManager == null) return;
        if (!_animationsManager.Animations.TryGetValue(requestByCode.Animation, out Animation? animation)) return;

        AnimationRequest request = new(animation, requestByCode);

        Play(request, mainHand);
    }
    public void PlayReadyAnimation(bool mainHand = true)
    {
        if (mainHand)
        {
            if (_player.RightHandItemSlot.Itemstack?.Item is IHasIdleAnimations item)
            {
                Play(item.ReadyAnimation, mainHand);
                StartIdleTimer(item.IdleAnimation, mainHand);
            }
        }
        else
        {
            if (_player.LeftHandItemSlot.Itemstack?.Item is IHasIdleAnimations item)
            {
                Play(item.ReadyAnimation, mainHand);
                StartIdleTimer(item.IdleAnimation, mainHand);
            }
        }
    }
    public void Stop(string category)
    {
        _composer.Stop(category);
    }

    private readonly Composer _composer;
    private readonly EntityPlayer _player;
    private readonly AnimationsManager? _animationsManager;
    private PlayerItemFrame _lastFrame = PlayerItemFrame.Zero;
    private readonly List<string> _offhandCategories = new();
    private readonly List<string> _mainHandCategories = new();
    private int _offHandItemId = 0;
    private int _mainHandItemId = 0;
    private long _mainHandIdleTimer = -1;
    private long _offHandIdleTimer = -1;
    private string _mainHandIdleCategory = "";
    private string _offHandIdleCategory = "";
    private bool _resetFov = false;
    private ICoreClientAPI? _api;

    private static readonly TimeSpan _readyTimeout = TimeSpan.FromSeconds(5);

    private readonly FieldInfo _mainCameraInfo = typeof(ClientMain).GetField("MainCamera", BindingFlags.NonPublic | BindingFlags.Instance);
    private readonly FieldInfo _cameraFov = typeof(Camera).GetField("Fov", BindingFlags.NonPublic | BindingFlags.Instance);

    private void OnBeforeFrame(Entity entity, float dt)
    {
        if (!IsOwner(entity)) return;

        _lastFrame = _composer.Compose(TimeSpan.FromSeconds(dt / 2));
    }
    private void OnFrame(Entity entity, ElementPose pose)
    {
        if (!IsFirstPerson(entity)) return;
        if (!_composer.AnyActiveAnimations() && FrameOverride == null)
        {
            if (_resetFov)
            {
                SetFov(1);
                EyeHightController.Amplitude = 1.0f;
                _resetFov = false;
            }
            return;
        }

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
        TorsoAnimationType torsoAnimation = (entity as EntityPlayer)?.Controls.Sneak ?? false ? TorsoAnimationType.Sneaking : TorsoAnimationType.Standing;
        torsoAnimation = IsFirstPerson(entity) ? torsoAnimation : TorsoAnimationType.None;

        frame.Apply(pose, torsoAnimation);

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

        SetFov(frame.Player.FovMultiplier);
        EyeHightController.Amplitude = frame.Player.BobbingAmplitude;
        _resetFov = true;
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
    private void SetFov(float multiplier)
    {
        ClientMain? client = _api?.World as ClientMain;
        if (client == null) return;

        PlayerCamera? camera = (PlayerCamera?)_mainCameraInfo.GetValue(client);
        if (camera == null) return;

        float? fovField = (float?)_cameraFov.GetValue(camera);
        if (fovField == null) return;

        PlayerRenderingPatches.HandsFovMultiplier = multiplier;
        _cameraFov.SetValue(camera, ClientSettings.FieldOfView * GameMath.DEG2RAD * multiplier);

        CurrentFov = ClientSettings.FieldOfView * multiplier;
    }
    
    private void StartIdleTimer(AnimationRequestByCode request, bool mainHand)
    {
        long timer = _api?.World.RegisterCallback(_ => PlayIdleAnimation(request, mainHand), (int)_readyTimeout.TotalMilliseconds) ?? -1;
        if (mainHand)
        {
            _mainHandIdleTimer = timer;
        }
        else
        {
            _offHandIdleTimer = timer;
        }
    }
    private void StopIdleTimer(bool mainHand)
    {
        if (mainHand)
        {
            if (_mainHandIdleTimer != -1)
            {
                _api?.World.UnregisterCallback(_mainHandIdleTimer);
                _mainHandIdleTimer = -1;
            }
        }
        else
        {
            if (_offHandIdleTimer != -1)
            {
                _api?.World.UnregisterCallback(_offHandIdleTimer);
                _offHandIdleTimer = -1;
            }
        }
    }
    private void StopIdleTimer(bool mainHand, string category)
    {
        if (mainHand)
        {
            if (_mainHandIdleTimer != -1 && _mainHandIdleCategory == category)
            {
                _api?.World.UnregisterCallback(_mainHandIdleTimer);
                _mainHandIdleTimer = -1;
            }
        }
        else
        {
            if (_offHandIdleTimer != -1 && _offHandIdleCategory == category)
            {
                _api?.World.UnregisterCallback(_offHandIdleTimer);
                _offHandIdleTimer = -1;
            }
        }
    }
    private void PlayIdleAnimation(AnimationRequestByCode request, bool mainHand)
    {
        if (mainHand)
        {
            _mainHandIdleTimer = -1;
            _mainHandIdleCategory = request.Category;
        }
        else
        {
            _offHandIdleTimer = -1;
            _offHandIdleCategory = request.Category;
        }

        Play(request, mainHand);
    }
}
