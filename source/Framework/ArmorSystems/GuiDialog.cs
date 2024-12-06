using Cairo;
using CombatOverhaul.DamageSystems;
using System.Diagnostics;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.Client.NoObf;

namespace CombatOverhaul.Armor;

public sealed class GuiDialogArmorInventory : GuiDialog
{
    public GuiDialogArmorInventory(ICoreClientAPI api) : base(api)
    {
        _api = api;

        foreach (GuiDialogCharacter characterDialog in api.Gui.LoadedGuis.OfType<GuiDialogCharacter>())
        {
            characterDialog.OnOpened += OnOpenedEvent;
            characterDialog.OnClosed += OnClosedEvent;
            _characterDialog = characterDialog;
        }

        _dummyInventory = new(api, ArmorInventory._totalSlotsNumber);
    }

    public const string DialogName = "CombatOverhaul:armor-inventory-dialog";
    public readonly string DialogTitle = Lang.Get("combatoverhaul:armor-inventory-dialog-title");

    public override bool PrefersUngrabbedMouse => false;

    public override string? ToggleKeyCombinationCode => null;


    private readonly RealDummyInventory _dummyInventory;
    private bool _inventoryLinked = false;
    private GuiComposer? _composer;
    private readonly GuiDialogCharacter? _characterDialog;
    private readonly ICoreClientAPI _api;
    private bool _recomposeDialog = false;
    private const int _recomposeDelay = 200;

    private void OnOpenedEvent()
    {
        TryOpen();
        ComposeDialog();
    }
    private void OnClosedEvent()
    {
        TryClose();
        _composer?.Dispose();
    }

    private void RecomposeDialog()
    {
        if (!opened || _recomposeDialog) return;

        _recomposeDialog = true;
        _api.World.RegisterCallback(_ => RecomposeDialogCallback(), _recomposeDelay);
    }

    private void RecomposeDialogCallback()
    {
        _recomposeDialog = false;
        ComposeDialog();
    }

    private void ComposeDialog()
    {
        if (_characterDialog == null)
        {
            return;
        }

        GuiComposer playerStatsCompo = _characterDialog.Composers["playerstats"];
        if (playerStatsCompo is null) { return; }

        double gap = GuiElement.scaled(GuiElementItemSlotGridBase.unscaledSlotPadding);
        double textGap = gap;
        double bgPadding = GuiElement.scaled(9);
        double textWidth = 55;

        IInventory _inv = capi.World.Player.InventoryManager.GetOwnInventory(GlobalConstants.characterInvClassName);
        if (_inv is not ArmorInventory inv)
        {
            return;
        }
        CairoFont textFont = CairoFont.WhiteSmallText();
        textFont.Orientation = EnumTextOrientation.Right;

        if (!_inventoryLinked)
        {
            inv.OnArmorSlotModified += RecomposeDialog;
            _inventoryLinked = true;
        }

        ElementBounds mainBounds = playerStatsCompo.Bounds.RightCopy(GuiElement.scaled(5));
        mainBounds.BothSizing = ElementSizing.FitToChildren;

        ElementBounds childBounds = new ElementBounds().WithSizing(ElementSizing.FitToChildren);
        ElementBounds backgroundBounds = childBounds.WithFixedPadding(bgPadding);

        ElementBounds placeholderBounds = ElementStdBounds.Slot(0, 32).WithFixedWidth(textWidth);
        ElementBounds slot0Bounds = ElementStdBounds.Slot(placeholderBounds.RightCopy(gap + GuiElement.scaled(5)).fixedX, placeholderBounds.RightCopy().fixedY);
        ElementBounds slot1Bounds = ElementStdBounds.Slot(slot0Bounds.RightCopy(gap).fixedX, slot0Bounds.RightCopy().fixedY);
        ElementBounds slot2Bounds = ElementStdBounds.Slot(slot1Bounds.RightCopy(gap).fixedX, slot1Bounds.RightCopy().fixedY);

        ElementBounds textBounds = placeholderBounds
            .BelowCopy(fixedDeltaY: gap + GuiElement.scaled(12))
            .WithFixedHeight(placeholderBounds.fixedHeight)
            .WithFixedWidth(placeholderBounds.fixedWidth);

        _composer = Composers[DialogName] = capi.Gui.CreateCompo(DialogName, mainBounds);
        _composer.AddShadedDialogBG(backgroundBounds, true);
        _composer.AddDialogTitleBar(DialogTitle, () => TryClose());
        _composer.BeginChildElements(childBounds);
        _composer.AddDynamicText("", textFont, textBounds, "textHead");
        _composer.AddDynamicText("", textFont, BelowCopySet(ref textBounds, fixedDeltaY: textGap), "textFace");
        _composer.AddDynamicText("", textFont, BelowCopySet(ref textBounds, fixedDeltaY: textGap), "textNeck");
        _composer.AddDynamicText("", textFont, BelowCopySet(ref textBounds, fixedDeltaY: textGap), "textTorso");
        _composer.AddDynamicText("", textFont, BelowCopySet(ref textBounds, fixedDeltaY: textGap), "textArms");
        _composer.AddDynamicText("", textFont, BelowCopySet(ref textBounds, fixedDeltaY: textGap), "textHands");
        _composer.AddDynamicText("", textFont, BelowCopySet(ref textBounds, fixedDeltaY: textGap), "textLegs");
        _composer.AddDynamicText("", textFont, BelowCopySet(ref textBounds, fixedDeltaY: textGap), "textFeet");
        _composer.AddStaticCustomDraw(slot0Bounds, OnDrawOuterIcon);
        _composer.AddStaticCustomDraw(slot1Bounds, OnDrawMiddleIcon);
        _composer.AddStaticCustomDraw(slot2Bounds, OnDrawSkinIcon);

        AddSlot(inv, ArmorLayers.Outer, DamageZone.Head, ref slot0Bounds, gap);
        AddSlot(inv, ArmorLayers.Outer, DamageZone.Face, ref slot0Bounds, gap);
        AddSlot(inv, ArmorLayers.Outer, DamageZone.Neck, ref slot0Bounds, gap);
        AddSlot(inv, ArmorLayers.Outer, DamageZone.Torso, ref slot0Bounds, gap);
        AddSlot(inv, ArmorLayers.Outer, DamageZone.Arms, ref slot0Bounds, gap);
        AddSlot(inv, ArmorLayers.Outer, DamageZone.Hands, ref slot0Bounds, gap);
        AddSlot(inv, ArmorLayers.Outer, DamageZone.Legs, ref slot0Bounds, gap);
        AddSlot(inv, ArmorLayers.Outer, DamageZone.Feet, ref slot0Bounds, gap);

        AddSlot(inv, ArmorLayers.Middle, DamageZone.Head, ref slot1Bounds, gap);
        AddSlot(inv, ArmorLayers.Middle, DamageZone.Face, ref slot1Bounds, gap);
        AddSlot(inv, ArmorLayers.Middle, DamageZone.Neck, ref slot1Bounds, gap);
        AddSlot(inv, ArmorLayers.Middle, DamageZone.Torso, ref slot1Bounds, gap);
        AddSlot(inv, ArmorLayers.Middle, DamageZone.Arms, ref slot1Bounds, gap);
        AddSlot(inv, ArmorLayers.Middle, DamageZone.Hands, ref slot1Bounds, gap);
        AddSlot(inv, ArmorLayers.Middle, DamageZone.Legs, ref slot1Bounds, gap);
        AddSlot(inv, ArmorLayers.Middle, DamageZone.Feet, ref slot1Bounds, gap);

        AddSlot(inv, ArmorLayers.Skin, DamageZone.Head, ref slot2Bounds, gap);
        AddSlot(inv, ArmorLayers.Skin, DamageZone.Face, ref slot2Bounds, gap);
        AddSlot(inv, ArmorLayers.Skin, DamageZone.Neck, ref slot2Bounds, gap);
        AddSlot(inv, ArmorLayers.Skin, DamageZone.Torso, ref slot2Bounds, gap);
        AddSlot(inv, ArmorLayers.Skin, DamageZone.Arms, ref slot2Bounds, gap);
        AddSlot(inv, ArmorLayers.Skin, DamageZone.Hands, ref slot2Bounds, gap);
        AddSlot(inv, ArmorLayers.Skin, DamageZone.Legs, ref slot2Bounds, gap);
        AddSlot(inv, ArmorLayers.Skin, DamageZone.Feet, ref slot2Bounds, gap);

        _composer.EndChildElements();
        try
        {
            _composer.Compose();
        }
        catch (Exception ex)
        {
            Trace.WriteLine(ex.ToString());
            return;
        }


        _composer.GetDynamicText("textHead")?.SetNewText(Lang.Get("combatoverhaul:armor-inventory-dialog-head"));
        _composer.GetDynamicText("textFace")?.SetNewText(Lang.Get("combatoverhaul:armor-inventory-dialog-face"));
        _composer.GetDynamicText("textNeck")?.SetNewText(Lang.Get("combatoverhaul:armor-inventory-dialog-neck"));
        _composer.GetDynamicText("textTorso")?.SetNewText(Lang.Get("combatoverhaul:armor-inventory-dialog-torso"));
        _composer.GetDynamicText("textArms")?.SetNewText(Lang.Get("combatoverhaul:armor-inventory-dialog-arms"));
        _composer.GetDynamicText("textHands")?.SetNewText(Lang.Get("combatoverhaul:armor-inventory-dialog-hands"));
        _composer.GetDynamicText("textLegs")?.SetNewText(Lang.Get("combatoverhaul:armor-inventory-dialog-legs"));
        _composer.GetDynamicText("textFeet")?.SetNewText(Lang.Get("combatoverhaul:armor-inventory-dialog-feet"));
    }

    private void AddSlot(ArmorInventory inv, ArmorLayers layers, DamageZone zone, ref ElementBounds bounds, double gap)
    {
        int slotIndex = ArmorInventory.IndexFromArmorType(layers, zone);
        bool available = inv.IsSlotAvailable(slotIndex) || !inv[slotIndex].Empty;

        if (available)
        {
            _composer.AddItemSlotGrid(inv, SendInvPacket, 1, new int[] { slotIndex }, BelowCopySet(ref bounds, fixedDeltaY: gap));
        }
        else
        {
            _dummyInventory[slotIndex].HexBackgroundColor = "#AAAAAA";
            _dummyInventory[slotIndex].Itemstack = inv[inv.GetSlotBlockingSlotIndex(layers, zone)].Itemstack;
            _composer.AddItemSlotGrid(_dummyInventory, (_) => { }, 1, new int[] { slotIndex }, BelowCopySet(ref bounds, fixedDeltaY: gap));
        }
    }

    private void OnDrawOuterIcon(Context ctx, ImageSurface surface, ElementBounds currentBounds)
    {
        IAsset asset = capi.Assets.TryGet(AssetLocation.Create("combatoverhaul:textures/icons/outer.svg"));
        if (asset == null) return;
        capi.Gui.DrawSvg(asset, surface, (int)currentBounds.drawX, (int)currentBounds.drawY, (int)GuiElement.scaled(currentBounds.fixedWidth), (int)GuiElement.scaled(currentBounds.fixedHeight), null);
    }
    private void OnDrawMiddleIcon(Context ctx, ImageSurface surface, ElementBounds currentBounds)
    {
        IAsset asset = capi.Assets.TryGet(AssetLocation.Create("combatoverhaul:textures/icons/middle.svg"));
        if (asset == null) return;
        capi.Gui.DrawSvg(asset, surface, (int)currentBounds.drawX, (int)currentBounds.drawY, (int)GuiElement.scaled(currentBounds.fixedWidth), (int)GuiElement.scaled(currentBounds.fixedHeight), null);
    }
    private void OnDrawSkinIcon(Context ctx, ImageSurface surface, ElementBounds currentBounds)
    {
        IAsset asset = capi.Assets.TryGet(AssetLocation.Create("combatoverhaul:textures/icons/skin.svg"));
        if (asset == null) return;
        capi.Gui.DrawSvg(asset, surface, (int)currentBounds.drawX, (int)currentBounds.drawY, (int)GuiElement.scaled(currentBounds.fixedWidth), (int)GuiElement.scaled(currentBounds.fixedHeight), null);
    }

    private void SendInvPacket(object packet)
    {
        capi.Network.SendPacketClient(packet);
    }
    private static ElementBounds BelowCopySet(ref ElementBounds bounds, double fixedDeltaX = 0.0, double fixedDeltaY = 0.0, double fixedDeltaWidth = 0.0, double fixedDeltaHeight = 0.0)
    {
        return bounds = bounds.BelowCopy(fixedDeltaX, fixedDeltaY, fixedDeltaWidth, fixedDeltaHeight);
    }
}

public class RealDummyInventory : DummyInventory
{
    public RealDummyInventory(ICoreAPI api, int quantitySlots = 1) : base(api, quantitySlots)
    {
    }

    public override bool CanContain(ItemSlot sinkSlot, ItemSlot sourceSlot) => false;
    public override bool CanPlayerAccess(IPlayer player, EntityPos position) => false;
    public override bool CanPlayerModify(IPlayer player, EntityPos position) => false;

    protected override ItemSlot NewSlot(int i) => new RealDummySlot(this)
    {
        DrawUnavailable = false
    };
}

public class RealDummySlot : ItemSlot
{
    public RealDummySlot(InventoryBase inventory) : base(inventory)
    {
    }

    public override bool CanTake() => false;
    public override bool CanHold(ItemSlot sourceSlot) => false;
}
