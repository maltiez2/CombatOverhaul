using CombatOverhaul.Armor;
using CombatOverhaul.Colliders;
using CombatOverhaul.DamageSystems;
using CombatOverhaul.Inputs;
using CombatOverhaul.Integration;
using CombatOverhaul.Animations;
using CombatOverhaul.RangedSystems;
using CombatOverhaul.RangedSystems.Aiming;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.Client.NoObf;
using CombatOverhaul.Implementations.Vanilla;
using OpenTK.Windowing.GraphicsLibraryFramework;
using Vintagestory.GameContent;

namespace CombatOverhaul;


public sealed class CombatOverhaulSystem : ModSystem
{
    public const string ArmorInventoryId = "CombatOverhaul:ArmorInventory";

    public override void StartPre(ICoreAPI api)
    {
        (api as ClientCoreAPI)?.ClassRegistryNative.RegisterInventoryClass(GlobalConstants.characterInvClassName, typeof(ArmorInventory));
    }

    public override void Start(ICoreAPI api)
    {
        api.RegisterEntityBehaviorClass("CombatOverhaul:FirstPersonAnimations", typeof(FirstPersonAnimationsBehavior));
        api.RegisterEntityBehaviorClass("CombatOverhaul:EntityColliders", typeof(CollidersEntityBehavior));
        api.RegisterEntityBehaviorClass("CombatOverhaul:EntityDamageModel", typeof(EntityDamageModelBehavior));
        api.RegisterEntityBehaviorClass("CombatOverhaul:PlayerDamageModel", typeof(PlayerDamageModelBehavior));
        api.RegisterEntityBehaviorClass("CombatOverhaul:ActionsManager", typeof(ActionsManagerPlayerBehavior));

        api.RegisterCollectibleBehaviorClass("CombatOverhaul:Animatable", typeof(Animatable));
        api.RegisterCollectibleBehaviorClass("CombatOverhaul:AnimatableAttachable", typeof(AnimatableAttachable));
        api.RegisterCollectibleBehaviorClass("CombatOverhaul:Projectile", typeof(ProjectileBehavior));

        api.RegisterItemClass("CombatOverhaul:Bow", typeof(BowItem));

        api.RegisterEntity("CombatOverhaul:Projectile", typeof(ProjectileEntity));

        new Harmony("CombatOverhaulAuto").PatchAll();
    }
    public override void StartServerSide(ICoreServerAPI api)
    {
        ServerProjectileSystem = new(api);
        ServerRangedWeaponSystem = new(api);
    }
    public override void StartClientSide(ICoreClientAPI api)
    {
        ClientProjectileSystem = new(api, api.ModLoader.GetModSystem<EntityPartitioning>());
        ActionListener = new(api);
        DirectionCursorRenderer = new(api);
        ReticleRenderer = new(api);
        DirectionController = new(api, DirectionCursorRenderer);
        ClientRangedWeaponSystem = new(api);

        api.Event.RegisterRenderer(ReticleRenderer, EnumRenderStage.Ortho);
        api.Event.RegisterRenderer(DirectionCursorRenderer, EnumRenderStage.Ortho);

        AimingPatches.Patch("CombatOverhaulAiming");
    }

    public override void Dispose()
    {
        new Harmony("CombatOverhaulAuto").UnpatchAll();

        AimingPatches.Unpatch("CombatOverhaulAiming");
    }

    internal ProjectileSystemClient? ClientProjectileSystem { get; private set; }
    internal ProjectileSystemServer? ServerProjectileSystem { get; private set; }
    internal ActionListener? ActionListener { get; private set; }
    internal DirectionCursorRenderer? DirectionCursorRenderer { get; private set; }
    internal ReticleRenderer? ReticleRenderer { get; private set; }
    internal DirectionController? DirectionController { get; private set; }
    internal RangedWeaponSystemClient? ClientRangedWeaponSystem { get; private set; }
    internal RangedWeaponSystemServer? ServerRangedWeaponSystem { get; private set; }
}


public sealed class CombatOverhaulAnimationsSystem : ModSystem
{
    public AnimationsManager? PlayerAnimationsManager { get; private set; }

    internal IShaderProgram? AnimatedItemShaderProgram => _shaderProgram;
    internal IShaderProgram? AnimatedItemShaderProgramFirstPerson => _shaderProgramFirstPerson;

    public override void Start(ICoreAPI api)
    {
        _api = api;

        AnimationPatch.Patch("CombatOverhaul");
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        api.Event.ReloadShader += LoadAnimatedItemShaders;
        LoadAnimatedItemShaders();
        PlayerAnimationsManager = new(api);
    }

    public override void StartServerSide(ICoreServerAPI api)
    {

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


