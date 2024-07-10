using Cairo;
using CombatOverhaul.Armor;
using CombatOverhaul.DamageSystems;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.Client.NoObf;

namespace CombatOverhaul.GUI;

public class GuiDialogArmorSlots : GuiDialog
{
    private bool recompose;
    private GuiComposer composer;
    private GuiDialogCharacter characterDialog;

    public const string DialogName = "combatoverhaul:armorslotsdialog";
    public readonly string DialogTitle = Lang.Get("combatoverhaul:armorslotsdialog-title");

    public override string ToggleKeyCombinationCode => null;

    public GuiDialogArmorSlots(ICoreClientAPI capi) : base(capi)
    {
        //capi.Event.RegisterGameTickListener(Every500ms, 500); // REMOVE AFTER GUI IS DONE
        foreach (GuiDialogCharacter characterDialog in capi.Gui.LoadedGuis.Where(x => x is GuiDialogCharacter).Select(x => x as GuiDialogCharacter))
        {
            characterDialog.OnOpened += () => GuiDialogCharacter_OnOpened(characterDialog);
            characterDialog.OnClosed += () => GuiDialogCharacter_OnClose(characterDialog);
        }
    }

    //private void Every500ms(float dt) // REMOVE AFTER GUI IS DONE
    //{
    //    if (characterDialog == null || !characterDialog.IsOpened())
    //    {
    //        return;
    //    }
    //    ComposeDialog();
    //}

    private void GuiDialogCharacter_OnOpened(GuiDialogCharacter characterDialog)
    {
        this.characterDialog = characterDialog;
        TryOpen();
        ComposeDialog();
    }

    private void GuiDialogCharacter_OnClose(GuiDialogCharacter characterDialog)
    {
        TryClose();
        composer?.Dispose();
    }

    private bool _inventoryLinked = false;

    private void ComposeDialog()
    {
        if (characterDialog == null)
        {
            return;
        }

        GuiComposer playerStatsCompo = characterDialog.Composers["playerstats"];
        if (playerStatsCompo is null) { return; }

        double gap = GuiElement.scaled(GuiElementItemSlotGridBase.unscaledSlotPadding);
        double textGap = gap;
        double bgPadding = GuiElement.scaled(9);
        double textWidth = 55;// GuiElement.scaled(75);

        IInventory _inv = capi.World.Player.InventoryManager.GetOwnInventory(GlobalConstants.characterInvClassName);
        if (_inv is not ArmorInventory inv)
        {
            return;
        }
        CairoFont textFont = CairoFont.WhiteSmallText();
        textFont.Orientation = EnumTextOrientation.Right;

        if (!_inventoryLinked)
        {
            inv._onSlotModified += ComposeDialog;
            _inventoryLinked = true;
        }

        ElementBounds statsBoundsRightCopy = playerStatsCompo.Bounds.RightCopy();

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

        composer = Composers[DialogName] = capi.Gui.CreateCompo(DialogName, mainBounds);
        composer.AddShadedDialogBG(backgroundBounds, false);
        composer.AddDialogTitleBarWithBg(DialogTitle, () => TryClose());
        composer.BeginChildElements(childBounds);
        composer.AddDynamicText("", textFont, textBounds, "textHead");
        composer.AddDynamicText("", textFont, BelowCopySet(ref textBounds, fixedDeltaY: textGap), "textFace");
        composer.AddDynamicText("", textFont, BelowCopySet(ref textBounds, fixedDeltaY: textGap), "textNeck");
        composer.AddDynamicText("", textFont, BelowCopySet(ref textBounds, fixedDeltaY: textGap), "textTorso");
        composer.AddDynamicText("", textFont, BelowCopySet(ref textBounds, fixedDeltaY: textGap), "textArms");
        composer.AddDynamicText("", textFont, BelowCopySet(ref textBounds, fixedDeltaY: textGap), "textHands");
        composer.AddDynamicText("", textFont, BelowCopySet(ref textBounds, fixedDeltaY: textGap), "textLegs");
        composer.AddDynamicText("", textFont, BelowCopySet(ref textBounds, fixedDeltaY: textGap), "textFeet");
        composer.AddStaticCustomDraw(slot0Bounds, OnDrawOuterIcon);
        composer.AddStaticCustomDraw(slot1Bounds, OnDrawMiddleIcon);
        composer.AddStaticCustomDraw(slot2Bounds, OnDrawSkinIcon);

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

        composer.EndChildElements();
        composer.Compose();

        composer.GetDynamicText("textHead")?.SetNewText(Lang.Get("combatoverhaul:Head"));
        composer.GetDynamicText("textFace")?.SetNewText(Lang.Get("combatoverhaul:Face"));
        composer.GetDynamicText("textNeck")?.SetNewText(Lang.Get("combatoverhaul:Neck"));
        composer.GetDynamicText("textTorso")?.SetNewText(Lang.Get("combatoverhaul:Torso"));
        composer.GetDynamicText("textArms")?.SetNewText(Lang.Get("combatoverhaul:Arms"));
        composer.GetDynamicText("textHands")?.SetNewText(Lang.Get("combatoverhaul:Hands"));
        composer.GetDynamicText("textLegs")?.SetNewText(Lang.Get("combatoverhaul:Legs"));
        composer.GetDynamicText("textFeet")?.SetNewText(Lang.Get("combatoverhaul:Feet"));
    }

    private RealDummyInventory? _dummyInventory;

    public void AddSlot(ArmorInventory inv, ArmorLayers layers, DamageZone zone, ref ElementBounds bounds, double gap)
    {
        _dummyInventory ??= new(capi, ArmorInventory._totalSlotsNumber);
        
        int slotIndex = ArmorInventory.IndexFromArmorType(layers, zone);
        bool available = inv.IsSlotAvailable(slotIndex) || !inv[slotIndex].Empty;

        if (available)
        {
            composer.AddItemSlotGrid(inv, SendInvPacket, 1, new int[] { slotIndex }, BelowCopySet(ref bounds, fixedDeltaY: gap));
        }
        else
        {
            _dummyInventory[slotIndex].HexBackgroundColor = "#999999";
            _dummyInventory[slotIndex].Itemstack = inv[inv.GetSlotBlockingSlotIndex(layers, zone)].Itemstack;
            composer.AddItemSlotGrid(_dummyInventory, (_) => { }, 1, new int[] { slotIndex }, BelowCopySet(ref bounds, fixedDeltaY: gap));
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

    protected void SendInvPacket(object packet)
    {
        capi.Network.SendPacketClient(packet);
    }

    public static ElementBounds BelowCopySet(ref ElementBounds bounds, double fixedDeltaX = 0.0, double fixedDeltaY = 0.0, double fixedDeltaWidth = 0.0, double fixedDeltaHeight = 0.0)
    {
        return bounds = bounds.BelowCopy(fixedDeltaX, fixedDeltaY, fixedDeltaWidth, fixedDeltaHeight);
    }
}
