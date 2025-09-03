using CombatOverhaul.Armor;
using CombatOverhaul.Integration;
using CombatOverhaul.source;
using CombatOverhaul.Utils;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
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

        if  (api is ICoreClientAPI clientApi)
        {
            CheckStatusClientSide(clientApi);
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
            ThrowException(api, $"(Combat Overhaul) Gear inventory class was replaced with '{className}' by some other mod, shutting down the client. Report this issue into Combat Overhaul thread with client-main logs attached.");
        }

        bool immersiveFirstPersonMode = api.Settings.Bool["immersiveFpMode"];
        if (immersiveFirstPersonMode)
        {
            LoggerUtil.Error(api, this, $"Immersive first person mode is enabled. It is not supported. Turn this setting off.");
            AnnoyPlayer(api, "(Combat Overhaul) Immersive first person mode is enabled. It is not supported. Turn this setting off to prevent this message.", () => api.Settings.Bool["immersiveFpMode"]);
        }
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