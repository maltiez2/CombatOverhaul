using Cairo;
using CombatOverhaul.Animations;
using CombatOverhaul.Implementations;
using CombatOverhaul.Inputs;
using CombatOverhaul.Integration;
using CombatOverhaul.RangedSystems;
using CombatOverhaul.RangedSystems.Aiming;
using CombatOverhaul.Utils;
using HarmonyLib;
using System.Numerics;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.Client.NoObf;
using Vintagestory.GameContent;
using Vintagestory.Server;

namespace CombatOverhaul;

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

public sealed class CombatOverhaulSystem : ModSystem
{
    public event Action? OnDispose;
    public event Action<Settings>? SettingsLoaded;
    public Settings Settings { get; set; } = new();

    public override void Start(ICoreAPI api)
    {
        api.RegisterEntityBehaviorClass("CombatOverhaul:FirstPersonAnimations", typeof(FirstPersonAnimationsBehavior));
        api.RegisterEntityBehaviorClass("CombatOverhaul:ActionsManager", typeof(ActionsManagerPlayerBehavior));
        api.RegisterEntityBehaviorClass("CombatOverhaul:AimingAccuracy", typeof(AimingAccuracyBehavior));

        api.RegisterCollectibleBehaviorClass("CombatOverhaul:Animatable", typeof(Animatable));
        api.RegisterCollectibleBehaviorClass("CombatOverhaul:AnimatableAttachable", typeof(AnimatableAttachable));
        api.RegisterCollectibleBehaviorClass("CombatOverhaul:Projectile", typeof(ProjectileBehavior));

        api.RegisterItemClass("CombatOverhaul:Bow", typeof(BowItem));
        api.RegisterItemClass("CombatOverhaul:MeleeWeapon", typeof(MeleeWeapon));

        api.RegisterEntity("CombatOverhaul:Projectile", typeof(ProjectileEntity));

        api.RegisterBlockEntityClass("CombatOverhaul:GenericDisplayBlockEntity", typeof(GenericDisplayBlockEntity));
        api.RegisterBlockClass("CombatOverhaul:GenericDisplayBlock", typeof(GenericDisplayBlock));

        new Harmony("CombatOverhaulAuto").PatchAll();
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

        ClientProjectileSystem = new(api, api.ModLoader.GetModSystem<EntityPartitioning>());
        ActionListener = new(api);
        ReticleRenderer = new(api);
        ClientRangedWeaponSystem = new(api);
        ClientSoundsSynchronizer = new(api);
        AimingSystem = new(api, ReticleRenderer);
        ClientStatsSystem = new(api);

        api.Event.RegisterRenderer(ReticleRenderer, EnumRenderStage.Ortho);

        AimingPatches.Patch("CombatOverhaulAiming");
    }

    public override void AssetsFinalize(ICoreAPI api)
    {
        IAsset settingsAsset = api.Assets.Get("combatoverhaul:config/settings.json");
        JsonObject settings = JsonObject.FromJson(settingsAsset.ToText());
        Settings = settings.AsObject<Settings>();

        AnimationPatch.YawSmoothing = Settings.HandsYawSmoothing;

        SettingsLoaded?.Invoke(Settings);
    }

    private readonly Vector4 _iconScale = new(-0.1f, -0.1f, 1.2f, 1.2f);

    private void RegisterCustomIcon(ICoreClientAPI api, string key, string path)
    {
        api.Gui.Icons.CustomIcons[key] = delegate (Context ctx, int x, int y, float w, float h, double[] rgba)
        {
            AssetLocation location = new(path);
            IAsset svgAsset = api.Assets.TryGet(location);
            int value = ColorUtil.ColorFromRgba(75, 75, 75, 125);
            Surface target = ctx.GetTarget();

            int xNew = x + (int)(w * _iconScale.X);
            int yNew = y + (int)(h * _iconScale.Y);
            int wNew = (int)(w * _iconScale.W);
            int hNew = (int)(h * _iconScale.Z);

            api.Gui.DrawSvg(svgAsset, (ImageSurface)(object)((target is ImageSurface) ? target : null), xNew, yNew, wNew, hNew, value);
        };
    }

    public override void Dispose()
    {
        new Harmony("CombatOverhaulAuto").UnpatchAll();

        _clientApi?.Event.UnregisterRenderer(ReticleRenderer, EnumRenderStage.Ortho);

        AimingPatches.Unpatch("CombatOverhaulAiming");

        OnDispose?.Invoke();
    }

    public ProjectileSystemClient? ClientProjectileSystem { get; private set; }
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


public sealed class CombatOverhaulAnimationsSystem : ModSystem
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

        AnimationPatch.Patch("CombatOverhaul");

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
        AnimationPatch.Unpatch("CombatOverhaul");

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