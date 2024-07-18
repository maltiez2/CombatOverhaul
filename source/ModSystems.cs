using CombatOverhaul.Animations;
using CombatOverhaul.Armor;
using CombatOverhaul.Colliders;
using CombatOverhaul.DamageSystems;
using CombatOverhaul.Implementations;
using CombatOverhaul.Inputs;
using CombatOverhaul.Integration;
using CombatOverhaul.MeleeSystems;
using CombatOverhaul.RangedSystems;
using CombatOverhaul.RangedSystems.Aiming;
using CombatOverhaul.Utils;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.Client.NoObf;
using Vintagestory.GameContent;
using Vintagestory.Server;

namespace CombatOverhaul;


public sealed class CombatOverhaulSystem : ModSystem
{
    public override void StartPre(ICoreAPI api)
    {
        (api as ServerCoreAPI)?.ClassRegistryNative.RegisterInventoryClass(GlobalConstants.characterInvClassName, typeof(ArmorInventory));
        (api as ClientCoreAPI)?.ClassRegistryNative.RegisterInventoryClass(GlobalConstants.characterInvClassName, typeof(ArmorInventory));
    }

    public override void Start(ICoreAPI api)
    {
        api.RegisterEntityBehaviorClass("CombatOverhaul:FirstPersonAnimations", typeof(FirstPersonAnimationsBehavior));
        api.RegisterEntityBehaviorClass("CombatOverhaul:EntityColliders", typeof(CollidersEntityBehavior));
        api.RegisterEntityBehaviorClass("CombatOverhaul:EntityDamageModel", typeof(EntityDamageModelBehavior));
        api.RegisterEntityBehaviorClass("CombatOverhaul:PlayerDamageModel", typeof(PlayerDamageModelBehavior));
        api.RegisterEntityBehaviorClass("CombatOverhaul:ActionsManager", typeof(ActionsManagerPlayerBehavior));
        api.RegisterEntityBehaviorClass("CombatOverhaul:AimingAccuracy", typeof(AimingAccuracyBehavior));
        api.RegisterEntityBehaviorClass("CombatOverhaul:ArmorStats", typeof(ArmorStatsBehavior));

        api.RegisterCollectibleBehaviorClass("CombatOverhaul:Animatable", typeof(Animatable));
        api.RegisterCollectibleBehaviorClass("CombatOverhaul:AnimatableAttachable", typeof(AnimatableAttachable));
        api.RegisterCollectibleBehaviorClass("CombatOverhaul:Projectile", typeof(ProjectileBehavior));
        api.RegisterCollectibleBehaviorClass("CombatOverhaul:ExplosiveProjectile", typeof(ProjectileExplosiveBehavior));
        api.RegisterCollectibleBehaviorClass("CombatOverhaul:FragmentationProjectile", typeof(ProjectileFragmentationBehavior)); 
        api.RegisterCollectibleBehaviorClass("CombatOverhaul:Armor", typeof(ArmorBehavior));

        api.RegisterItemClass("CombatOverhaul:Bow", typeof(BowItem));
        api.RegisterItemClass("CombatOverhaul:Crossbow", typeof(CrossbowItem));
        api.RegisterItemClass("CombatOverhaul:MagazineCrossbow", typeof(MagazineCrossbowItem));
        api.RegisterItemClass("CombatOverhaul:Muzzleloader", typeof(MuzzleloaderItem));
        api.RegisterItemClass("CombatOverhaul:MeleeWeapon", typeof(MeleeWeapon));
        api.RegisterItemClass("CombatOverhaul:Armor", typeof(ArmorItem));
        api.RegisterItemClass("CombatOverhaul:Javelin", typeof(JavelinItem));

        api.RegisterEntity("CombatOverhaul:Projectile", typeof(ProjectileEntity));
        api.RegisterEntity("CombatOverhaul:ProjectileExplosive", typeof(ProjectileExplosive));
        api.RegisterEntity("CombatOverhaul:ProjectileFragmentation", typeof(ProjectileFragmentation));
        api.RegisterEntity("CombatOverhaul:ProjectileOreBomb", typeof(ProjectileOreBomb));
        api.RegisterEntity("CombatOverhaul:ProjectileFirework", typeof(ProjectileFirework));

        new Harmony("CombatOverhaulAuto").PatchAll();
    }
    public override void StartServerSide(ICoreServerAPI api)
    {
        ServerProjectileSystem = new(api);
        ServerRangedWeaponSystem = new(api);
        ServerSoundsSynchronizer = new(api);
        ServerMeleeSystem = new(api);
        ServerBlockSystem = new(api);
    }
    public override void StartClientSide(ICoreClientAPI api)
    {
        _clientApi = api;

        ClientProjectileSystem = new(api, api.ModLoader.GetModSystem<EntityPartitioning>());
        ActionListener = new(api);
        DirectionCursorRenderer = new(api);
        ReticleRenderer = new(api);
        DirectionController = new(api, DirectionCursorRenderer);
        ClientRangedWeaponSystem = new(api);
        ClientSoundsSynchronizer = new(api);
        AimingSystem = new(api, ReticleRenderer);
        ClientMeleeSystem = new(api);
        ClientBlockSystem = new(api);

        api.Event.RegisterRenderer(ReticleRenderer, EnumRenderStage.Ortho);
        api.Event.RegisterRenderer(DirectionCursorRenderer, EnumRenderStage.Ortho);

        api.Gui.RegisterDialog(new GuiDialogArmorInventory(api));

        AimingPatches.Patch("CombatOverhaulAiming");
        MouseWheelPatch.Patch("CombatOverhaul", api);
    }

    public override void Dispose()
    {
        new Harmony("CombatOverhaulAuto").UnpatchAll();

        _clientApi?.Event.UnregisterRenderer(ReticleRenderer, EnumRenderStage.Ortho);
        _clientApi?.Event.UnregisterRenderer(DirectionCursorRenderer, EnumRenderStage.Ortho);

        AimingPatches.Unpatch("CombatOverhaulAiming");
        MouseWheelPatch.Unpatch("CombatOverhaul");
    }

    public ProjectileSystemClient? ClientProjectileSystem { get; private set; }
    public ProjectileSystemServer? ServerProjectileSystem { get; private set; }
    public ActionListener? ActionListener { get; private set; }
    public DirectionCursorRenderer? DirectionCursorRenderer { get; private set; }
    public ReticleRenderer? ReticleRenderer { get; private set; }
    public ClientAimingSystem? AimingSystem { get; private set; }
    public DirectionController? DirectionController { get; private set; }
    public RangedWeaponSystemClient? ClientRangedWeaponSystem { get; private set; }
    public RangedWeaponSystemServer? ServerRangedWeaponSystem { get; private set; }
    public SoundsSynchronizerClient? ClientSoundsSynchronizer { get; private set; }
    public SoundsSynchronizerServer? ServerSoundsSynchronizer { get; private set; }
    public MeleeSystemClient? ClientMeleeSystem { get; private set; }
    public MeleeSystemServer? ServerMeleeSystem { get; private set; }
    public MeleeBlockSystemClient? ClientBlockSystem { get; private set; }
    public MeleeBlockSystemServer? ServerBlockSystem { get; private set; }


    private ICoreClientAPI? _clientApi;
}


public sealed class CombatOverhaulAnimationsSystem : ModSystem
{
    public AnimationsManager? PlayerAnimationsManager { get; private set; }
    public ParticleEffectsManager? ParticleEffectsManager { get; private set; }

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
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        ParticleEffectsManager = new(api);
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


