using CombatOverhaul.Implementations;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.Client.NoObf;

namespace CombatOverhaul.Integration;

public interface IMouseWheelInput
{
    bool OnMouseWheel(ItemSlot slot, IClientPlayer byPlayer, float delta);
}

internal static class MouseWheelPatch
{
    public static void Patch(string harmonyId, ICoreClientAPI api)
    {
        _clientApi = api;

        new Harmony(harmonyId).Patch(
            typeof(HudHotbar).GetMethod("OnMouseWheel", AccessTools.all),
            prefix: new HarmonyMethod(AccessTools.Method(typeof(MouseWheelPatch), nameof(OnMouseWheel)))
            );
    }

    public static void Unpatch(string harmonyId)
    {
        new Harmony(harmonyId).Unpatch(typeof(HudHotbar).GetMethod("OnMouseWheel", AccessTools.all), HarmonyPatchType.Prefix, harmonyId);
    }

    private static ICoreClientAPI? _clientApi;
    private static int _prevValue = int.MaxValue;
    private static bool OnMouseWheel(MouseWheelEventArgs args)
    {
        if (_clientApi == null) return true;
        if (args.delta == 0 || args.value == _prevValue) return true;
        _prevValue = args.value;

        ItemSlot slot = _clientApi.World.Player.InventoryManager.ActiveHotbarSlot;

        if (slot.Itemstack?.Item is IMouseWheelInput item)
        {
            IClientPlayer player = _clientApi.World.Player;
            bool handled = item.OnMouseWheel(slot, player, args.deltaPrecise);

            return !handled;
        }

        return true;
    }
}