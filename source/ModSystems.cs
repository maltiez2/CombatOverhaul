using Bullseye.Animations;
using Bullseye.Implementations;
using Bullseye.Inputs;
using Bullseye.Integration;
using Bullseye.RangedSystems;
using Bullseye.RangedSystems.Aiming;
using Bullseye.Utils;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.Client.NoObf;

namespace Bullseye;

public sealed class Settings
{
    public float DirectionsCursorAlpha { get; set; } = 1.0f;
    public float DirectionsCursorScale { get; set; } = 1.0f;

    public string BowsAimingCursorType { get; set; } = "Moving";
    public float BowsAimingHorizontalLimit { get; set; } = 0.125f;
    public float BowsAimingVerticalLimit { get; set; } = 0.35f;

    public bool PrintProjectilesHits { get; set; } = false;
    public bool PrintMeleeHits { get; set; } = false;
    public bool PrintPlayerBeingHit { get; set; } = false;

    public float DirectionsControllerSensitivity { get; set; } = 1f;
    public bool DirectionsControllerInvert { get; set; } = false;

    public bool HandsYawSmoothing { get; set; } = false;
}

public sealed class BullseyeSystem : ModSystem
{
    public event Action? OnDispose;
    public event Action<Settings>? SettingsLoaded;
    public Settings Settings { get; set; } = new();

    public override void Start(ICoreAPI api)
    {
        api.RegisterEntityBehaviorClass("Bullseye:FirstPersonAnimations", typeof(FirstPersonAnimationsBehavior));
        api.RegisterEntityBehaviorClass("Bullseye:ActionsManager", typeof(ActionsManagerPlayerBehavior));
        api.RegisterEntityBehaviorClass("Bullseye:AimingAccuracy", typeof(AimingAccuracyBehavior));

        api.RegisterCollectibleBehaviorClass("Bullseye:Animatable", typeof(Animatable));
        api.RegisterCollectibleBehaviorClass("Bullseye:AnimatableAttachable", typeof(AnimatableAttachable));
        api.RegisterCollectibleBehaviorClass("Bullseye:Projectile", typeof(ProjectileBehavior));

        api.RegisterItemClass("Bullseye:Bow", typeof(BowItem));
        api.RegisterItemClass("Bullseye:MeleeWeapon", typeof(MeleeWeapon));

        api.RegisterBlockEntityClass("Bullseye:GenericDisplayBlockEntity", typeof(GenericDisplayBlockEntity));
        api.RegisterBlockClass("Bullseye:GenericDisplayBlock", typeof(GenericDisplayBlock));

        new Harmony("BullseyeAuto").PatchAll();
    }
    public override void StartServerSide(ICoreServerAPI api)
    {
        ServerProjectileSystem = new(api);
        ServerRangedWeaponSystem = new(api);
        ServerSoundsSynchronizer = new(api);
        ServerStatsSystem = new(api);
    }
    public override void StartClientSide(ICoreClientAPI api)
    {
        _clientApi = api;

        ActionListener = new(api);
        ReticleRenderer = new(api);
        ClientRangedWeaponSystem = new(api);
        ClientSoundsSynchronizer = new(api);
        AimingSystem = new(api, ReticleRenderer);
        ClientStatsSystem = new(api);

        api.Event.RegisterRenderer(ReticleRenderer, EnumRenderStage.Ortho);

        AimingPatches.Patch("BullseyeAiming");
    }

    public override void AssetsFinalize(ICoreAPI api)
    {
        IAsset settingsAsset = api.Assets.Get("combatoverhaul:config/settings.json");
        JsonObject settings = JsonObject.FromJson(settingsAsset.ToText());
        Settings = settings.AsObject<Settings>();

        AnimationPatch.YawSmoothing = Settings.HandsYawSmoothing;

        SettingsLoaded?.Invoke(Settings);
    }

    public override void Dispose()
    {
        new Harmony("BullseyeAuto").UnpatchAll();

        _clientApi?.Event.UnregisterRenderer(ReticleRenderer, EnumRenderStage.Ortho);

        AimingPatches.Unpatch("BullseyeAiming");

        OnDispose?.Invoke();
    }

    public ProjectileSystemServer? ServerProjectileSystem { get; private set; }
    public ActionListener? ActionListener { get; private set; }
    public ReticleRenderer? ReticleRenderer { get; private set; }
    public ClientAimingSystem? AimingSystem { get; private set; }
    public RangedWeaponSystemClient? ClientRangedWeaponSystem { get; private set; }
    public RangedWeaponSystemServer? ServerRangedWeaponSystem { get; private set; }
    public SoundsSynchronizerClient? ClientSoundsSynchronizer { get; private set; }
    public SoundsSynchronizerServer? ServerSoundsSynchronizer { get; private set; }
    public StatsSystemClient? ClientStatsSystem { get; private set; }
    public StatsSystemServer? ServerStatsSystem { get; private set; }

    private ICoreClientAPI? _clientApi;
}


public sealed class BullseyeAnimationsSystem : ModSystem
{
    public AnimationsManager? PlayerAnimationsManager { get; private set; }
    public ParticleEffectsManager? ParticleEffectsManager { get; private set; }
    public VanillaAnimationsSystemClient? ClientVanillaAnimations { get; private set; }
    public VanillaAnimationsSystemServer? ServerVanillaAnimations { get; private set; }

    public IShaderProgram? AnimatedItemShaderProgram => _shaderProgram;
    public IShaderProgram? AnimatedItemShaderProgramFirstPerson => _shaderProgramFirstPerson;

    public override void Start(ICoreAPI api)
    {
        _api = api;

        AnimationPatch.Patch("Bullseye");

    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        api.Event.ReloadShader += LoadAnimatedItemShaders;
        LoadAnimatedItemShaders();
        ParticleEffectsManager = new(api);
        PlayerAnimationsManager = new(api, ParticleEffectsManager);
        ClientVanillaAnimations = new(api);
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        ParticleEffectsManager = new(api);
        ServerVanillaAnimations = new(api);
    }

    public override void AssetsFinalize(ICoreAPI api)
    {
        PlayerAnimationsManager?.Load();
    }

    public override void Dispose()
    {
        AnimationPatch.Unpatch("Bullseye");

        if (_api is ICoreClientAPI clientApi)
        {
            clientApi.Event.ReloadShader -= LoadAnimatedItemShaders;
        }
    }


    private ShaderProgram? _shaderProgram;
    private ShaderProgram? _shaderProgramFirstPerson;
    private ICoreAPI? _api;

    private bool LoadAnimatedItemShaders()
    {
        if (_api is not ICoreClientAPI clientApi) return false;

        _shaderProgram = clientApi.Shader.NewShaderProgram() as ShaderProgram;
        _shaderProgramFirstPerson = clientApi.Shader.NewShaderProgram() as ShaderProgram;

        if (_shaderProgram == null || _shaderProgramFirstPerson == null) return false;

        _shaderProgram.AssetDomain = Mod.Info.ModID;
        clientApi.Shader.RegisterFileShaderProgram("customstandard", AnimatedItemShaderProgram);
        _shaderProgram.Compile();

        _shaderProgramFirstPerson.AssetDomain = Mod.Info.ModID;
        clientApi.Shader.RegisterFileShaderProgram("customstandardfirstperson", AnimatedItemShaderProgramFirstPerson);
        _shaderProgramFirstPerson.Compile();

        return true;
    }
}