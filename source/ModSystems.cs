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
using OpenTK.Mathematics;
using ProtoBuf;
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

    public bool DoVanillaActionsWhileBlocking { get; set; } = true;
}

public sealed class ArmorConfig
{
    public int MaxAttackTier { get; set; } = 9;
    public int MaxArmorTier { get; set; } = 24;
    public float[][] DamageReduction { get; set; } = new float[][]
    {
        new float[] { 0.75f, 1.00f, 1.00f, 1.00f, 1.00f, 1.00f, 1.00f, 1.00f, 1.00f },
        new float[] { 0.50f, 0.75f, 1.00f, 1.00f, 1.00f, 1.00f, 1.00f, 1.00f, 1.00f },
        new float[] { 0.25f, 0.50f, 0.75f, 1.00f, 1.00f, 1.00f, 1.00f, 1.00f, 1.00f },
        new float[] { 0.10f, 0.25f, 0.50f, 0.75f, 1.00f, 1.00f, 1.00f, 1.00f, 1.00f },
        new float[] { 0.05f, 0.15f, 0.33f, 0.50f, 0.75f, 1.00f, 1.00f, 1.00f, 1.00f },
        new float[] { 0.03f, 0.10f, 0.25f, 0.40f, 0.50f, 0.75f, 1.00f, 1.00f, 1.00f },
        new float[] { 0.02f, 0.05f, 0.20f, 0.33f, 0.40f, 0.50f, 0.75f, 1.00f, 1.00f },
        new float[] { 0.01f, 0.03f, 0.15f, 0.25f, 0.35f, 0.45f, 0.50f, 0.75f, 1.00f },
        new float[] { 0.01f, 0.02f, 0.10f, 0.20f, 0.30f, 0.40f, 0.45f, 0.50f, 0.75f },
        new float[] { 0.01f, 0.01f, 0.05f, 0.15f, 0.25f, 0.35f, 0.40f, 0.45f, 0.50f },
        new float[] { 0.01f, 0.01f, 0.03f, 0.10f, 0.20f, 0.30f, 0.35f, 0.41f, 0.46f },
        new float[] { 0.01f, 0.01f, 0.02f, 0.07f, 0.15f, 0.25f, 0.30f, 0.37f, 0.42f },
        new float[] { 0.01f, 0.01f, 0.01f, 0.05f, 0.10f, 0.20f, 0.25f, 0.33f, 0.39f },
        new float[] { 0.01f, 0.01f, 0.01f, 0.03f, 0.07f, 0.15f, 0.20f, 0.29f, 0.36f },
        new float[] { 0.01f, 0.01f, 0.01f, 0.02f, 0.06f, 0.10f, 0.17f, 0.25f, 0.33f },
        new float[] { 0.01f, 0.01f, 0.01f, 0.01f, 0.05f, 0.08f, 0.15f, 0.21f, 0.30f },
        new float[] { 0.01f, 0.01f, 0.01f, 0.01f, 0.03f, 0.07f, 0.12f, 0.18f, 0.27f },
        new float[] { 0.01f, 0.01f, 0.01f, 0.01f, 0.02f, 0.06f, 0.10f, 0.15f, 0.24f },
        new float[] { 0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.05f, 0.08f, 0.12f, 0.21f },
        new float[] { 0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.03f, 0.07f, 0.10f, 0.18f },
        new float[] { 0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.02f, 0.06f, 0.08f, 0.15f },
        new float[] { 0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.05f, 0.07f, 0.12f },
        new float[] { 0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.03f, 0.06f, 0.10f },
        new float[] { 0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.02f, 0.05f, 0.08f }
    };
}

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public sealed class TogglePacket
{
    public string HotKeyCode { get; set; } = "";
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
    }
    public override void Start(ICoreAPI api)
    {
        api.RegisterEntityBehaviorClass("CombatOverhaul:FirstPersonAnimations", typeof(FirstPersonAnimationsBehavior));
        api.RegisterEntityBehaviorClass("CombatOverhaul:ThirdPersonAnimations", typeof(ThirdPersonAnimationsBehavior));
        api.RegisterEntityBehaviorClass("CombatOverhaul:EntityColliders", typeof(CollidersEntityBehavior));
        api.RegisterEntityBehaviorClass("CombatOverhaul:EntityDamageModel", typeof(EntityDamageModelBehavior));
        api.RegisterEntityBehaviorClass("CombatOverhaul:PlayerDamageModel", typeof(PlayerDamageModelBehavior));
        api.RegisterEntityBehaviorClass("CombatOverhaul:ActionsManager", typeof(ActionsManagerPlayerBehavior));
        api.RegisterEntityBehaviorClass("CombatOverhaul:AimingAccuracy", typeof(AimingAccuracyBehavior));
        api.RegisterEntityBehaviorClass("CombatOverhaul:WearableStats", typeof(WearableStatsBehavior));
        api.RegisterEntityBehaviorClass("CombatOverhaul:InInventory", typeof(InInventoryPlayerBehavior));
        api.RegisterEntityBehaviorClass("CombatOverhaul:ArmorStandInventory", typeof(EntityBehaviorCOArmorStandInventory));

        api.RegisterCollectibleBehaviorClass("CombatOverhaul:Animatable", typeof(Animatable));
        api.RegisterCollectibleBehaviorClass("CombatOverhaul:AnimatableAttachable", typeof(AnimatableAttachable));
        api.RegisterCollectibleBehaviorClass("CombatOverhaul:Projectile", typeof(ProjectileBehavior));
        api.RegisterCollectibleBehaviorClass("CombatOverhaul:Armor", typeof(ArmorBehavior));
        api.RegisterCollectibleBehaviorClass("CombatOverhaul:WearableWithStats", typeof(WearableWithStatsBehavior));
        api.RegisterCollectibleBehaviorClass("CombatOverhaul:GearEquipableBag", typeof(GearEquipableBag));
        api.RegisterCollectibleBehaviorClass("CombatOverhaul:TextureFromAttributes", typeof(TextureFromAttributes));
        api.RegisterCollectibleBehaviorClass("CombatOverhaul:TexturesFromAttributes", typeof(TexturesFromAttributes));

        api.RegisterItemClass("CombatOverhaul:Bow", typeof(BowItem));
        api.RegisterItemClass("CombatOverhaul:MeleeWeapon", typeof(MeleeWeapon));
        api.RegisterItemClass("CombatOverhaul:VanillaShield", typeof(VanillaShield));
        api.RegisterItemClass("CombatOverhaul:Axe", typeof(Axe));
        api.RegisterItemClass("CombatOverhaul:Pickaxe", typeof(Pickaxe));
        api.RegisterItemClass("CombatOverhaul:WearableArmor", typeof(ItemWearableArmor));
        api.RegisterItemClass("CombatOverhaul:WearableFueledLightSource", typeof(WearableFueledLightSource));

        api.RegisterEntity("CombatOverhaul:Projectile", typeof(ProjectileEntity));

        api.RegisterBlockEntityClass("CombatOverhaul:GenericDisplayBlockEntity", typeof(GenericDisplayBlockEntity));
        api.RegisterBlockClass("CombatOverhaul:GenericDisplayBlock", typeof(GenericDisplayBlock));

        api.RegisterBlockBehaviorClass("CombatOverhaul:Splittable", typeof(Splittable));

        new Harmony("CombatOverhaulAuto").PatchAll();

        InInventoryPlayerBehavior._reportedEntities.Clear();
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
        ServerAttachmentSystem = new(api);

        _serverTOggleChannel = api.Network.RegisterChannel("combatOverhaulToggleItem")
            .RegisterMessageType<TogglePacket>()
            .SetMessageHandler<TogglePacket>(ToggleWearableItem);

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
        ClientAttachmentSystem = new(api);

        api.Event.RegisterRenderer(ReticleRenderer, EnumRenderStage.Ortho);
        api.Event.RegisterRenderer(DirectionCursorRenderer, EnumRenderStage.Ortho);

        api.Gui.RegisterDialog(new GuiDialogArmorInventory(api));

        AimingPatches.Patch("CombatOverhaulAiming");
        MouseWheelPatch.Patch("CombatOverhaul", api);

        _clientToggleChannel = api.Network.RegisterChannel("combatOverhaulToggleItem")
            .RegisterMessageType<TogglePacket>();

        api.Input.RegisterHotKey("toggleWearableLight", "Toggle wearable light source", GlKeys.L);
        api.Input.SetHotKeyHandler("toggleWearableLight", _ => ToggleWearableItem(api.World.Player, "toggleWearableLight"));
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


        IAsset armorConfigAsset = api.Assets.Get("combatoverhaul:config/armor-config.json");
        JsonObject armorConfig = JsonObject.FromJson(armorConfigAsset.ToText());
        ArmorConfig armorConfigObj = armorConfig.AsObject<ArmorConfig>();

        DamageResistData.MaxAttackTier = armorConfigObj.MaxAttackTier;
        DamageResistData.MaxArmorTier = armorConfigObj.MaxArmorTier;
        DamageResistData.DamageReduction = armorConfigObj.DamageReduction;

        ArmorAutoPatcher.Patch(api);

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

    public bool ToggleWearableItem(IPlayer player, string hotkeyCode)
    {
        IInventory? gearInventory = player.Entity.GetBehavior<EntityBehaviorPlayerInventory>().Inventory;
        bool toggled = false;
        foreach (ItemSlot slot in gearInventory)
        {
            if (slot?.Itemstack?.Collectible?.GetCollectibleInterface<ITogglableItem>() is ITogglableItem togglableItem && togglableItem.HotKeyCode == hotkeyCode)
            {
                togglableItem.Toggle(player, slot);
                toggled = true;
            }
        }

        if (player is IClientPlayer)
        {
            _clientToggleChannel?.SendPacket(new TogglePacket() { HotKeyCode = hotkeyCode });
        }

        return toggled;
    }
    public void ToggleWearableItem(IServerPlayer player, TogglePacket packet) => ToggleWearableItem(player, packet.HotKeyCode);

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
    public AttachableSystemClient? ClientAttachmentSystem { get; private set; }
    public AttachableSystemServer? ServerAttachmentSystem { get; private set; }

    private ICoreClientAPI? _clientApi;
    private readonly Vector4 _iconScale = new(-0.1f, -0.1f, 1.2f, 1.2f);
    private IClientNetworkChannel? _clientToggleChannel;
    private IServerNetworkChannel? _serverTOggleChannel;

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
        IInventory? gearInventory = GetGearInventory(api.World.Player.Entity);
        if (gearInventory is not ArmorInventory)
        {
            string className = gearInventory == null ? "null" : LoggerUtil.GetCallerTypeName(gearInventory);
            LoggerUtil.Error(api, this, $"Gear inventory class was replaced by some other mod, with {className}");
            ThrowException(api, $"(Combat Overhaul) Gear inventory class was replaced with '{className}' by some other mod, shutting down the client. Report this issue into Combat Overhaul thread with client-main logs attached.");
        }

        bool immersiveFirstPersonMode = api.Settings.Bool["immersiveFpMode"];
        if (immersiveFirstPersonMode)
        {
            LoggerUtil.Error(api, this, $"Immersive first person mode is enabled. It is not supported. Turn this setting off.");
            AnnoyPlayer(api, "(Combat Overhaul) Immersive first person mode is enabled. It is not supported. Turn this setting off to prevent this message.", () => api.Settings.Bool["immersiveFpMode"]);
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
    private void AnnoyPlayer(ICoreClientAPI api, string message, System.Func<bool> continueDelegate)
    {
        api.World.RegisterCallback(_ =>
        {
            api.TriggerIngameError(this, "error", message);
            if (!Disposed && continueDelegate()) AnnoyPlayer(api, message, continueDelegate);
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
    public AnimationSystemClient? ClientTpAnimationSystem { get; private set; }
    public AnimationSystemServer? ServerTpAnimationSystem { get; private set; }

    public IShaderProgram? AnimatedItemShaderProgram => _shaderProgram;
    public IShaderProgram? AnimatedItemShaderProgramFirstPerson => _shaderProgramFirstPerson;

    public override void Start(ICoreAPI api)
    {
        _api = api;

        HarmonyPatches.Patch("SomeUnknownMod", api);
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        api.Event.ReloadShader += LoadAnimatedItemShaders;
        LoadAnimatedItemShaders();
        ParticleEffectsManager = new(api);
        PlayerAnimationsManager = new(api, ParticleEffectsManager);
        ClientVanillaAnimations = new(api);
        ClientTpAnimationSystem = new(api);
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        ParticleEffectsManager = new(api);
        ServerVanillaAnimations = new(api);
        ServerTpAnimationSystem = new(api);
    }

    public override void AssetsFinalize(ICoreAPI api)
    {
        PlayerAnimationsManager?.Load();
    }

    public override void Dispose()
    {
        HarmonyPatches.Unpatch("SomeUnknownMod", _api);

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
    double GetFuelHours(IPlayer player, ItemSlot slot);
    void AddFuelHours(IPlayer player, ItemSlot slot, double hours);
    bool ConsumeFuelWhenSleeping(IPlayer player, ItemSlot slot);
}

public interface ITogglableItem
{
    string HotKeyCode { get; }

    bool TurnedOn(IPlayer player, ItemSlot slot);
    void TurnOn(IPlayer player, ItemSlot slot);
    void TurnOff(IPlayer player, ItemSlot slot);
    void Toggle(IPlayer player, ItemSlot slot);
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

                    if (IsSleeping(player.Entity) && !item.ConsumeFuelWhenSleeping(player, slot)) continue;

                    item.AddFuelHours(player, slot, -hoursPassed);
                    slot.MarkDirty();
                }
            }

            _lastCheckTotalHours = totalHours;
        }
    }

    private bool IsSleeping(EntityPlayer ep) => ep.GetBehavior<EntityBehaviorTiredness>()?.IsSleeping == true;

    private void OnLevelFinalize()
    {
        _playerInventoryBehavior = _clientApi?.World?.Player?.Entity?.GetBehavior<EntityBehaviorPlayerInventory>();
    }
}