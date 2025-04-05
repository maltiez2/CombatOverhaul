using CombatOverhaul.Armor;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.Client.NoObf;
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
}