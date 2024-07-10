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

    private void Every500ms(float dt) // REMOVE AFTER GUI IS DONE
    {
        if (characterDialog == null || !characterDialog.IsOpened())
        {
            return;
        }
        ComposeDialog();
    }

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

        double indent = GuiElement.scaled(32);
        double gap = GuiElement.scaled(GuiElementItemSlotGridBase.unscaledSlotPadding);
        double offsetY = GuiElement.scaled(indent) + GuiElement.scaled(gap);
        double bgPadding = GuiElement.scaled(5);
        double firstWidth = GuiElement.scaled(60);

        IInventory _inv = capi.World.Player.InventoryManager.GetOwnInventory(GlobalConstants.characterInvClassName);
        if (_inv is not ArmorInventory inv)
        {
            return;
        }
        CairoFont textFont = CairoFont.WhiteSmallText();

        if (!_inventoryLinked)
        {
            inv._onSlotModified += ComposeDialog;
            _inventoryLinked = true;
        }

        // should be used for dialog position
        //double padLeftX = playerStatsCompo.Bounds.fixedPaddingX + playerStatsCompo.Bounds.drawX;
        //double padLeftY = playerStatsCompo.Bounds.fixedPaddingY + playerStatsCompo.Bounds.drawY;

        ElementBounds mainBounds = ElementStdBounds.AutosizedMainDialog
            // todo: use playerstats borders for correct positions
            .RightOf(playerStatsCompo.Bounds);

        ElementBounds childBounds = new ElementBounds().WithSizing(ElementSizing.FitToChildren);
        ElementBounds backgroundBounds = childBounds.WithFixedPadding(bgPadding);

        ElementBounds textBounds = ElementStdBounds.Slot(0, indent).WithFixedWidth(firstWidth);
        ElementBounds slot0Bounds = ElementStdBounds.Slot(textBounds.RightCopy().fixedX, textBounds.RightCopy().fixedY);
        ElementBounds slot1Bounds = ElementStdBounds.Slot(slot0Bounds.RightCopy().fixedX, slot0Bounds.RightCopy().fixedY);
        ElementBounds slot2Bounds = ElementStdBounds.Slot(slot1Bounds.RightCopy().fixedX, slot1Bounds.RightCopy().fixedY);
        //try
        //{
        composer = Composers[DialogName] = capi.Gui.CreateCompo(DialogName, mainBounds)
        .AddDialogBG(backgroundBounds, false)
        .AddDialogTitleBarWithBg(DialogTitle, () => TryClose())
        .BeginChildElements(childBounds);
        composer.AddDynamicText("", textFont, BelowCopySet(ref textBounds, fixedDeltaY: gap), "textHead");
        composer.AddDynamicText("", textFont, BelowCopySet(ref textBounds, fixedDeltaY: gap), "textFace");
        composer.AddDynamicText("", textFont, BelowCopySet(ref textBounds, fixedDeltaY: gap), "textNeck");
        composer.AddDynamicText("", textFont, BelowCopySet(ref textBounds, fixedDeltaY: gap), "textTorso");
        composer.AddDynamicText("", textFont, BelowCopySet(ref textBounds, fixedDeltaY: gap), "textArms");
        composer.AddDynamicText("", textFont, BelowCopySet(ref textBounds, fixedDeltaY: gap), "textHands");
        composer.AddDynamicText("", textFont, BelowCopySet(ref textBounds, fixedDeltaY: gap), "textLegs");
        composer.AddDynamicText("", textFont, BelowCopySet(ref textBounds, fixedDeltaY: gap), "textFeet");
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
        //}
        //catch (Exception ex) { }
        composer?.GetDynamicText("textHead")?.SetNewText(Lang.Get("combatoverhaul:Head"));
        composer?.GetDynamicText("textFace")?.SetNewText(Lang.Get("combatoverhaul:Face"));
        composer?.GetDynamicText("textNeck")?.SetNewText(Lang.Get("combatoverhaul:Neck"));
        composer?.GetDynamicText("textTorso")?.SetNewText(Lang.Get("combatoverhaul:Torso"));
        composer?.GetDynamicText("textArms")?.SetNewText(Lang.Get("combatoverhaul:Arms"));
        composer?.GetDynamicText("textHands")?.SetNewText(Lang.Get("combatoverhaul:Hands"));
        composer?.GetDynamicText("textLegs")?.SetNewText(Lang.Get("combatoverhaul:Legs"));
        composer?.GetDynamicText("textFeet")?.SetNewText(Lang.Get("combatoverhaul:Feet"));
    }

    // breaks absolutely everything
    //public void AddSlot(ArmorInventory inv, ArmorLayers layers, DamageZone zone, ref ElementBounds bounds, double gap)
    //{
    //    int slotIndex = ArmorInventory.IndexFromArmorType(layers, zone);
    //    bool available = inv.IsSlotAvailable(layers, zone);
    //    if (available)
    //    {
    //        composer.AddItemSlotGrid(inv, SendInvPacket, 1, new int[] { slotIndex }, BelowCopySet(ref bounds, fixedDeltaY: gap));
    //    }
    //    else if (!available)
    //    {
    //        RealDummyInventory dummyInv = new RealDummyInventory(capi, 1);
    //        dummyInv[0].HexBackgroundColor = "#999999";
    //        if (inv[slotIndex].Itemstack != null)
    //        {
    //            dummyInv[0].Itemstack = inv[slotIndex].Itemstack.Clone();
    //        }
    //        composer.AddItemSlotGrid(dummyInv, (_) => { }, 1, new int[] { 0 }, BelowCopySet(ref bounds, fixedDeltaY: gap));
    //    }
    //}

    private RealDummyInventory? _dummyInventory;

    public void AddSlot(ArmorInventory inv, ArmorLayers layers, DamageZone zone, ref ElementBounds bounds, double gap)
    {
        if (_dummyInventory == null)
        {
            _dummyInventory = new(capi, ArmorInventory._totalSlotsNumber);
        }
        
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
        capi.Gui.DrawSvg(asset, surface, (int)currentBounds.drawX, (int)currentBounds.drawY, (int)currentBounds.fixedWidth, (int)currentBounds.fixedHeight, null);
    }

    private void OnDrawMiddleIcon(Context ctx, ImageSurface surface, ElementBounds currentBounds)
    {
        IAsset asset = capi.Assets.TryGet(AssetLocation.Create("combatoverhaul:textures/icons/middle.svg"));
        if (asset == null) return;
        capi.Gui.DrawSvg(asset, surface, (int)currentBounds.drawX, (int)currentBounds.drawY, (int)currentBounds.fixedWidth, (int)currentBounds.fixedHeight, null);
    }

    private void OnDrawSkinIcon(Context ctx, ImageSurface surface, ElementBounds currentBounds)
    {
        IAsset asset = capi.Assets.TryGet(AssetLocation.Create("combatoverhaul:textures/icons/skin.svg"));
        if (asset == null) return;
        capi.Gui.DrawSvg(asset, surface, (int)currentBounds.drawX, (int)currentBounds.drawY, (int)currentBounds.fixedWidth, (int)currentBounds.fixedHeight, null);
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
