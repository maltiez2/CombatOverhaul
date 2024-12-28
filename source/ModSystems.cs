using Cairo;
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
using System.Numerics;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
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
    public bool Disposed { get; private set; } = false;

    public override void StartPre(ICoreAPI api)
    {
        (api as ServerCoreAPI)?.ClassRegistryNative.RegisterInventoryClass(GlobalConstants.characterInvClassName, typeof(ArmorInventory));
        (api as ClientCoreAPI)?.ClassRegistryNative.RegisterInventoryClass(GlobalConstants.characterInvClassName, typeof(ArmorInventory));

        (api as ServerCoreAPI)?.ClassRegistryNative.RegisterInventoryClass(GlobalConstants.backpackInvClassName, typeof(InventoryPlayerBackPacksCombatOverhaul));
        (api as ClientCoreAPI)?.ClassRegistryNative.RegisterInventoryClass(GlobalConstants.backpackInvClassName, typeof(InventoryPlayerBackPacksCombatOverhaul));
    }
    public override void Start(ICoreAPI api)
    {
        api.RegisterEntityBehaviorClass("CombatOverhaul:FirstPersonAnimations", typeof(FirstPersonAnimationsBehavior));
        api.RegisterEntityBehaviorClass("CombatOverhaul:EntityColliders", typeof(CollidersEntityBehavior));
        api.RegisterEntityBehaviorClass("CombatOverhaul:EntityDamageModel", typeof(EntityDamageModelBehavior));
        api.RegisterEntityBehaviorClass("CombatOverhaul:PlayerDamageModel", typeof(PlayerDamageModelBehavior));
        api.RegisterEntityBehaviorClass("CombatOverhaul:ActionsManager", typeof(ActionsManagerPlayerBehavior));
        api.RegisterEntityBehaviorClass("CombatOverhaul:AimingAccuracy", typeof(AimingAccuracyBehavior));
        api.RegisterEntityBehaviorClass("CombatOverhaul:WearableStats", typeof(WearableStatsBehavior));
        api.RegisterEntityBehaviorClass("CombatOverhaul:InInventory", typeof(InInventoryPlayerBehavior));

        api.RegisterCollectibleBehaviorClass("CombatOverhaul:Animatable", typeof(Animatable));
        api.RegisterCollectibleBehaviorClass("CombatOverhaul:AnimatableAttachable", typeof(AnimatableAttachable));
        api.RegisterCollectibleBehaviorClass("CombatOverhaul:Projectile", typeof(ProjectileBehavior));
        api.RegisterCollectibleBehaviorClass("CombatOverhaul:Armor", typeof(ArmorBehavior));
        api.RegisterCollectibleBehaviorClass("CombatOverhaul:WearableWithStats", typeof(WearableWithStatsBehavior));
        api.RegisterCollectibleBehaviorClass("CombatOverhaul:GearEquipableBag", typeof(GearEquipableBag));

        api.RegisterItemClass("CombatOverhaul:Bow", typeof(BowItem));
        api.RegisterItemClass("CombatOverhaul:MeleeWeapon", typeof(MeleeWeapon));
        api.RegisterItemClass("CombatOverhaul:VanillaShield", typeof(VanillaShield));
        api.RegisterItemClass("CombatOverhaul:Axe", typeof(Axe));
        api.RegisterItemClass("CombatOverhaul:Pickaxe", typeof(Pickaxe));
        api.RegisterItemClass("CombatOverhaul:WearableArmor", typeof(ItemWearableArmor));

        api.RegisterEntity("CombatOverhaul:Projectile", typeof(ProjectileEntity));

        api.RegisterBlockEntityClass("CombatOverhaul:GenericDisplayBlockEntity", typeof(GenericDisplayBlockEntity));
        api.RegisterBlockClass("CombatOverhaul:GenericDisplayBlock", typeof(GenericDisplayBlock));

        api.RegisterBlockBehaviorClass("CombatOverhaul:Splittable", typeof(Splittable));

        new Harmony("CombatOverhaulAuto").PatchAll();
    }
    public override void StartServerSide(ICoreServerAPI api)
    {
        ServerProjectileSystem = new(api);
        ServerRangedWeaponSystem = new(api);
        ServerSoundsSynchronizer = new(api);
        ServerMeleeSystem = new(api);
        ServerBlockSystem = new(api);
        ServerStatsSystem = new(api);
        ServerBlockBreakingSystem = new(api);
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
        ClientStatsSystem = new(api);
        ClientBlockBreakingSystem = new(api);

        api.Event.RegisterRenderer(ReticleRenderer, EnumRenderStage.Ortho);
        api.Event.RegisterRenderer(DirectionCursorRenderer, EnumRenderStage.Ortho);

        api.Gui.RegisterDialog(new GuiDialogArmorInventory(api));

        AimingPatches.Patch("CombatOverhaulAiming");
        MouseWheelPatch.Patch("CombatOverhaul", api);
    }
    public override void AssetsLoaded(ICoreAPI api)
    {
        if (api is not ICoreClientAPI clientApi) return;

        foreach (ArmorLayers layer in Enum.GetValues<ArmorLayers>())
        {
            foreach (DamageZone zone in Enum.GetValues<DamageZone>())
            {
                string iconPath = $"combatoverhaul:textures/gui/icons/armor-{layer}-{zone}.svg";
                string iconCode = $"combatoverhaul-armor-{layer}-{zone}";

                if (!clientApi.Assets.Exists(new AssetLocation(iconPath))) continue;

                RegisterCustomIcon(clientApi, iconCode, iconPath);
            }
        }
    }
    public override void AssetsFinalize(ICoreAPI api)
    {
        IAsset settingsAsset = api.Assets.Get("combatoverhaul:config/settings.json");
        JsonObject settings = JsonObject.FromJson(settingsAsset.ToText());
        Settings = settings.AsObject<Settings>();

        if (DirectionCursorRenderer != null)
        {
            DirectionCursorRenderer.Alpha = Settings.DirectionsCursorAlpha;
            DirectionCursorRenderer.CursorScale = Settings.DirectionsCursorScale;
        }

        if (DirectionController != null)
        {
            DirectionController.Sensitivity = Settings.DirectionsControllerSensitivity;
            DirectionController.Invert = Settings.DirectionsControllerInvert;
        }

        HarmonyPatches.YawSmoothing = Settings.HandsYawSmoothing;

        SettingsLoaded?.Invoke(Settings);

        if (api is ICoreServerAPI serverApi)
        {
            CheckStatusServerSide(serverApi);
        }

        if (api is ICoreClientAPI clientApi)
        {
            CheckStatusClientSide(clientApi);
        }
    }
    public override void Dispose()
    {
        if (Disposed) return;
        
        new Harmony("CombatOverhaulAuto").UnpatchAll();

        _clientApi?.Event.UnregisterRenderer(ReticleRenderer, EnumRenderStage.Ortho);
        _clientApi?.Event.UnregisterRenderer(DirectionCursorRenderer, EnumRenderStage.Ortho);

        AimingPatches.Unpatch("CombatOverhaulAiming");
        MouseWheelPatch.Unpatch("CombatOverhaul");

        OnDispose?.Invoke();

        Disposed = true;
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
    public StatsSystemClient? ClientStatsSystem { get; private set; }
    public StatsSystemServer? ServerStatsSystem { get; private set; }
    public BlockBreakingSystemClient? ClientBlockBreakingSystem { get; private set; }
    public BlockBreakingSystemServer? ServerBlockBreakingSystem { get; private set; }

    private ICoreClientAPI? _clientApi;
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
    private void CheckStatusServerSide(ICoreServerAPI api)
    {

    }
    private void CheckStatusClientSide(ICoreClientAPI api)
    {
        IInventory? backpackInventory = GetBackpackInventory(api);
        if (backpackInventory is not InventoryPlayerBackPacksCombatOverhaul)
        {
            string className = backpackInventory == null ? "null" : LoggerUtil.GetCallerTypeName(backpackInventory);
            LoggerUtil.Error(api, this, $"Backpack inventory class was replaced by some other mod, so quivers cant work properly. New class: {className}");
            PrintInChat(api, "(error message) Backpack inventory class was replaced by some other mod, so quivers cant work properly. Report this issue into Combat Overhaul thread with client-main logs attached.");
        }

        IInventory? gearInventory = GetGearInventory(api.World.Player.Entity);
        if (gearInventory is not InventoryPlayerBackPacksCombatOverhaul)
        {
            string className = gearInventory == null ? "null" : LoggerUtil.GetCallerTypeName(gearInventory);
            LoggerUtil.Error(api, this, $"Gear inventory inventory class was replaced by some other mod. New class: {className}");
            ThrowException(api, "(Combat Overhaul) Gear inventory class was replaced by some other mod, shutting down the client. Report this issue into Combat Overhaul thread with client-main logs attached.");
        }

        bool immersiveFirstPersonMode = api.Settings.Bool["immersiveFpMode"];
        if (immersiveFirstPersonMode)
        {
            LoggerUtil.Error(api, this, $"Immersive first person mode is enabled. It is not supported. Turn this setting off.");
            PrintInChat(api, "(Combat Overhaul) Immersive first person mode is enabled. It is not supported. Turn this setting off and reload the world to prevent this message.");
        }
    }

    private static IInventory? GetBackpackInventory(ICoreClientAPI api)
    {
        return api.World.Player.InventoryManager.GetOwnInventory(GlobalConstants.backpackInvClassName);
    }
    private static InventoryBase? GetGearInventory(Entity entity)
    {
        return entity.GetBehavior<EntityBehaviorPlayerInventory>().Inventory;
    }
    private static void ThrowException(ICoreAPI api, string message)
    {
        api.World.RegisterCallback(_ => throw new Exception(message), 1);
    }
    private void AnnoyPlayer(ICoreClientAPI api, string message)
    {
        api.World.RegisterCallback(_ =>
        {
            api.TriggerIngameError(this, "error", message);
            if (!Disposed) AnnoyPlayer(api, message);
        }, 5000);
    }
    private void PrintInChat(ICoreClientAPI api, string message)
    {
        api.World.RegisterCallback(_ =>
        {
            api.SendChatMessage(message);
            if (!Disposed) PrintInChat(api, message);
        }, 5000);
    }
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

        HarmonyPatches.Patch("CombatOverhaul");

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
        HarmonyPatches.Unpatch("CombatOverhaul");

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

public interface IFueledItem
{
    double GetFuelHours(ItemSlot slot);
    void AddFuelHours(ItemSlot slot, double hours);
}


public sealed class NightVisionSystem : ModSystem, IRenderer
{
    public double RenderOrder => 0;
    public int RenderRange => 1;

    public override void StartClientSide(ICoreClientAPI api)
    {
        _clientApi = api;
        api.Event.RegisterRenderer(this, EnumRenderStage.Before, "nightvisionCO");
        api.Event.LevelFinalize += OnLevelFinalize;
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        base.StartServerSide(api);
        _serverApi = api;
        api.Event.RegisterGameTickListener(OnServerTick, 1000, 200);
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (_playerInventoryBehavior?.Inventory == null || _clientApi == null) return;

        ItemSlot? slot = _playerInventoryBehavior.Inventory.FirstOrDefault(slot => slot.Itemstack?.Collectible is ItemNightvisiondevice);

        double fuelLeft = (slot?.Itemstack?.Collectible as ItemNightvisiondevice)?.GetFuelHours(slot.Itemstack) ?? 0;

        if (fuelLeft > 0)
        {
            _clientApi.Render.ShaderUniforms.NightVisionStrength = (float)GameMath.Clamp(fuelLeft * 20, 0, 0.8);
        }
        else
        {
            _clientApi.Render.ShaderUniforms.NightVisionStrength = 0;
        }
    }

    private double _lastCheckTotalHours;
    private ICoreClientAPI? _clientApi;
    private ICoreServerAPI? _serverApi;
    private EntityBehaviorPlayerInventory? _playerInventoryBehavior;

    private void OnServerTick(float dt)
    {
        if (_serverApi == null) return;

        double totalHours = _serverApi.World.Calendar.TotalHours;
        double hoursPassed = totalHours - _lastCheckTotalHours;

        if (hoursPassed > 0.05)
        {
            foreach (IPlayer? player in _serverApi.World.AllOnlinePlayers)
            {
                IInventory? inventory = player.InventoryManager.GetOwnInventory(GlobalConstants.characterInvClassName);
                if (inventory == null) continue;

                ItemSlot? slot = inventory.FirstOrDefault(slot => slot.Itemstack?.Collectible is ItemNightvisiondevice);

                if (slot?.Itemstack?.Collectible is ItemNightvisiondevice device)
                {
                    device.AddFuelHours(slot.Itemstack, -hoursPassed);
                    slot.MarkDirty();
                }
            }

            foreach (IPlayer? player in _serverApi.World.AllOnlinePlayers)
            {
                IInventory? inventory = player.InventoryManager.GetOwnInventory(GlobalConstants.characterInvClassName);
                if (inventory == null) continue;

                foreach (ItemSlot slot in inventory)
                {
                    IFueledItem? item = slot.Itemstack?.Collectible?.GetCollectibleInterface<IFueledItem>();
                    if (item == null) continue;

                    item.AddFuelHours(slot, -hoursPassed);
                    slot.MarkDirty();
                }
            }

            _lastCheckTotalHours = totalHours;
        }
    }

    private void OnLevelFinalize()
    {
        _playerInventoryBehavior = _clientApi?.World?.Player?.Entity?.GetBehavior<EntityBehaviorPlayerInventory>();
    }
}