using CombatOverhaul.Utils;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace CombatOverhaul;

public interface IWearableLightSource
{
    byte[] GetLightHsv(EntityPlayer player, ItemSlot slot);
}

public class WearableFueledLightSourceStats
{
    public bool NeedsFuel { get; set; } = true;
    public float FuelCapacityHours { get; set; } = 24f;
    public float FuelEfficiency { get; set; } = 1f;
    public string FuelAttribute { get; set; } = "nightVisionFuelHours";
    public string ToggleAttribute { get; set; } = "turnedOn";
    public bool ConsumeFuelWhileSleeping { get; set; } = false;
    public byte[] LightHsv { get; set; } = new byte[3] { 0, 0, 0 };
    public byte[] TurnedOffLightHsv { get; set; } = new byte[3] { 0, 0, 0 };
}

public class WearableFueledLightSource : ItemWearable, IWearableLightSource, IFueledItem, ITogglableItem
{
    public virtual string HotKeyCode => "toggleWearableLight";
    public WearableFueledLightSourceStats Stats { get; set; } = new();

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);

        Stats = Attributes.AsObject<WearableFueledLightSourceStats>();

        if (MaxStackSize > 1)
        {
            LoggerUtil.Error(api, this, $"Item '{Code}' has max stack size > 1 while WearableFueledLightSource supposed to have it set to 1");
        }

        _clickToToggle = new()
        {
            MouseButton = EnumMouseButton.Right,
            ActionLangCode = "combatoverhaul:interaction-toggle-light-source"
        };

        _hotkeyToToggle = new()
        {
            ActionLangCode = Lang.Get("combatoverhaul:interaction-toggle-light-source-hotkey"),
            HotKeyCodes = new string[1] { "toggleWearableLight" },
            MouseButton = EnumMouseButton.None
        };
    }
    public override int GetMergableQuantity(ItemStack sinkStack, ItemStack sourceStack, EnumMergePriority priority)
    {
        if (priority == EnumMergePriority.DirectMerge)
        {
            if (GetStackFuel(sourceStack) == 0f)
            {
                return base.GetMergableQuantity(sinkStack, sourceStack, priority);
            }

            return 1;
        }

        return base.GetMergableQuantity(sinkStack, sourceStack, priority);
    }
    public override void TryMergeStacks(ItemStackMergeOperation op)
    {
        if (op.CurrentPriority == EnumMergePriority.DirectMerge)
        {
            float stackFuel = GetStackFuel(op.SourceSlot.Itemstack);
            double fuelHours = GetFuelHours(op.ActingPlayer, op.SinkSlot);
            if (stackFuel > 0f && fuelHours + (double)(stackFuel / 2f) < (double)Stats.FuelCapacityHours)
            {
                SetFuelHours(op.ActingPlayer, op.SinkSlot, (double)stackFuel + fuelHours);
                op.MovedQuantity = 1;
                op.SourceSlot.TakeOut(1);
                op.SinkSlot.MarkDirty();
            }
            else if (api.Side == EnumAppSide.Client)
            {
                (api as ICoreClientAPI)?.TriggerIngameError(this, "maskfull", Lang.Get("ingameerror-mask-full")); // @TODO change error message
            }
        }
    }
    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);

        if (Stats.NeedsFuel)
        {
            double fuelHours = GetFuelHours((world as IClientWorldAccessor)?.Player, inSlot);
            dsc.AppendLine(Lang.Get("Has fuel for {0:0.#} hours", fuelHours));
            if (fuelHours <= 0.0)
            {
                dsc.AppendLine(Lang.Get("Add fuel to refuel"));
            }

            dsc.AppendLine();
        }

        dsc.AppendLine();
    }
    public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handHandling)
    {
        if ((byEntity as EntityPlayer)?.Player is IPlayer player)
        {
            Toggle(player, slot);
            handHandling = EnumHandHandling.PreventDefault;
        }
        
        base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handHandling);
    }
    public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot)
    {
        WorldInteraction[] interactions = base.GetHeldInteractionHelp(inSlot);

        return interactions.Append(_clickToToggle).Append(_hotkeyToToggle).ToArray();
    }

    public virtual void AddFuelHours(IPlayer player, ItemSlot slot, double hours)
    {
        if (slot?.Itemstack?.Attributes == null) return;

        if (hours < 0 && !TurnedOn(player, slot)) return;

        slot.Itemstack.Attributes.SetDouble("fuelHours", Math.Max(0.0, hours + GetFuelHours(player, slot)));
        slot.OnItemSlotModified(sinkStack: null);
    }
    public virtual double GetFuelHours(IPlayer player, ItemSlot slot)
    {
        if (slot?.Itemstack?.Attributes == null) return 0;

        return Math.Max(0.0, slot.Itemstack.Attributes.GetDecimal("fuelHours"));
    }
    public virtual bool ConsumeFuelWhenSleeping(IPlayer player, ItemSlot slot) => Stats.ConsumeFuelWhileSleeping;
    public virtual byte[] GetLightHsv(EntityPlayer player, ItemSlot slot) => TurnedOn(player.Player, slot) && Stats.NeedsFuel ? Stats.LightHsv : Stats.TurnedOffLightHsv;
    public virtual float GetStackFuel(ItemStack stack) => (stack.ItemAttributes?[Stats.FuelAttribute].AsFloat() ?? 0f) * Stats.FuelEfficiency;
    public virtual void SetFuelHours(IPlayer player, ItemSlot slot, double fuelHours)
    {
        if (slot?.Itemstack?.Attributes == null) return;

        fuelHours = GameMath.Clamp(fuelHours, 0, Stats.FuelCapacityHours);
        slot.Itemstack.Attributes.SetDouble("fuelHours", fuelHours);
        slot.MarkDirty();
    }
    public virtual bool TurnedOn(IPlayer player, ItemSlot slot) => slot?.Itemstack?.Attributes?.GetBool(Stats.ToggleAttribute) ?? false;
    public virtual void TurnOn(IPlayer player, ItemSlot slot)
    {
        if (GetFuelHours(player, slot) <= 0)
        {
            TurnOff(player, slot);
            return;
        }
        
        slot?.Itemstack?.Attributes?.SetBool(Stats.ToggleAttribute, true);
        slot?.MarkDirty();
    }
    public virtual void TurnOff(IPlayer player, ItemSlot slot)
    {
        slot?.Itemstack?.Attributes?.SetBool(Stats.ToggleAttribute, false);
        slot?.MarkDirty();
    }
    public virtual void Toggle(IPlayer player, ItemSlot slot)
    {
        if (TurnedOn(player, slot))
        {
            TurnOff(player, slot);
        }
        else
        {
            TurnOn(player, slot);
        }
    }

    private WorldInteraction? _clickToToggle;
    private WorldInteraction? _hotkeyToToggle;
}
