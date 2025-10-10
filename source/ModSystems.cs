using CombatOverhaul.Animations;
using CombatOverhaul.Armor;
using CombatOverhaul.Colliders;
using CombatOverhaul.DamageSystems;
using CombatOverhaul.Integration;
using CombatOverhaul.source;
using CombatOverhaul.Utils;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.Client.NoObf;
using Vintagestory.GameContent;
using Vintagestory.Server;

namespace CombatOverhaul;

public sealed class CombatOverhaulAdditionalSystem : ModSystem
{
    public override void StartPre(ICoreAPI api)
    {
        (api as ServerCoreAPI)?.ClassRegistryNative.RegisterInventoryClass(GlobalConstants.characterInvClassName, typeof(ArmorInventory));
        (api as ClientCoreAPI)?.ClassRegistryNative.RegisterInventoryClass(GlobalConstants.characterInvClassName, typeof(ArmorInventory));
    }
    public override void StartClientSide(ICoreClientAPI api)
    {
        api.Gui.RegisterDialog(new GuiDialogArmorInventory(api));
    }
    public override void Start(ICoreAPI api)
    {
        api.RegisterEntityBehaviorClass("CombatOverhaul:ShowStatsBehavior", typeof(ShowStatsBehavior));
    }

    public override void AssetsFinalize(ICoreAPI api)
    {
        if (api.Side == EnumAppSide.Server)
        {
            ArmorAutoPatcher.Patch(api);
        }

        if (api is ICoreClientAPI clientApi)
        {
            CheckStatusClientSide(clientApi);
        }

        if (api is ICoreServerAPI serverApi)
        {
            CheckStatusServerSide(serverApi);
        }
    }

    public override void Dispose()
    {
        base.Dispose();

        Disposed = true;
    }

    private bool Disposed = false;

    private void CheckStatusClientSide(ICoreClientAPI api)
    {
        IInventory? gearInventory = GetGearInventory(api.World.Player.Entity);
        if (gearInventory is not ArmorInventory)
        {
            string className = gearInventory == null ? "null" : LoggerUtil.GetCallerTypeName(gearInventory);
            LoggerUtil.Error(api, this, $"Gear inventory class was replaced by some other mod, with {className}");
        }

        bool immersiveFirstPersonMode = api.Settings.Bool["immersiveFpMode"];
        if (immersiveFirstPersonMode)
        {
            LoggerUtil.Error(api, this, $"Immersive first person mode is enabled. It is not supported. Turn this setting off.");
            AnnoyPlayer(api, "(Combat Overhaul) Immersive first person mode is enabled. It is not supported. Turn this setting off to prevent this message.", () => false);// api.Settings.Bool["immersiveFpMode"]);
        }

        api.World.RegisterCallback(_ =>
        {
            EntityPlayer? player = api.World.Player.Entity;

            bool hasFirstPersonAnimationsBehavior = player?.GetBehavior<FirstPersonAnimationsBehavior>() != null;
            if (!hasFirstPersonAnimationsBehavior && player != null)
            {
                string behaviorsList = player.Properties.Client.Behaviors.Select(behavior => behavior.GetType().Name).Aggregate((a, b) => $"{a}\n{b}") ?? "";
                string message = $"Was not able to find 'FirstPersonAnimationsBehavior'. Some other mod altered players behavior in a way that break other mods.\nList of current player client entity behaviors:\n{behaviorsList}";
                LoggerUtil.Error(api, this, message);
                api.TriggerIngameError(this, "error", $"Error in Combat Overhaul mod, report to mod author in discord with client-main and server-main logs.");
            }

            bool hasThirdPersonAnimationsBehavior = player?.GetBehavior<ThirdPersonAnimationsBehavior>() != null;
            if (!hasThirdPersonAnimationsBehavior && player != null)
            {
                string behaviorsList = player.Properties.Client.Behaviors.Select(behavior => behavior.GetType().Name).Aggregate((a, b) => $"{a}\n{b}") ?? "";
                string message = $"Was not able to find 'ThirdPersonAnimationsBehavior'. Some other mod altered players behavior in a way that break other mods.\nList of current player client entity behaviors:\n{behaviorsList}";
                LoggerUtil.Error(api, this, message);
                api.TriggerIngameError(this, "error", $"Error in Combat Overhaul mod, report to mod author in discord with client-main and server-main logs.");
            }
        }, 30000);
    }
    private void CheckStatusServerSide(ICoreServerAPI api)
    {
        api.World.RegisterCallback(_ =>
        {
            EntityPlayer? player = api.World.AllOnlinePlayers
                .Select(player => player.Entity)
                .Where(entity => entity != null)
                .OfType<EntityPlayer>()
                .FirstOrDefault((EntityPlayer?)null);

            bool hasPlayerDamageModelBehavior = player?.GetBehavior<PlayerDamageModelBehavior>() != null;
            if (!hasPlayerDamageModelBehavior && player != null)
            {
                string behaviorsList = player.Properties.Server.Behaviors.Select(behavior => behavior.GetType().Name).Aggregate((a, b) => $"{a}\n{b}") ?? "";
                string message = $"Was not able to find 'PlayerDamageModelBehavior'. Some other mod altered players behavior in a way that break other mods.\nList of current player server entity behaviors:\n{behaviorsList}\n";
                LoggerUtil.Error(api, this, message);
            }

            bool hasCollidersEntityBehavior = player?.GetBehavior<CollidersEntityBehavior>() != null;
            if (!hasCollidersEntityBehavior && player != null)
            {
                string behaviorsList = player.Properties.Server.Behaviors.Select(behavior => behavior.GetType().Name).Aggregate((a, b) => $"{a}\n{b}") ?? "";
                string message = $"Was not able to find 'CollidersEntityBehavior'. Some other mod altered players behavior in a way that break other mods.\nList of current player server entity behaviors:\n{behaviorsList}\n";
                LoggerUtil.Error(api, this, message);
            }
        }, 30000);
    }

    private static InventoryBase? GetGearInventory(Entity entity)
    {
        return entity.GetBehavior<EntityBehaviorPlayerInventory>()?.Inventory;
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