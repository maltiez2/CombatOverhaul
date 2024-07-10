using Cairo;
using CombatOverhaul.Animations;
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
        capi.Event.RegisterGameTickListener(Every500ms, 500); // REMOVE AFTER GUI IS DONE
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

    private void ComposeDialog()
    {
        if (characterDialog == null)
        {
            return;
        }

        GuiComposer playerStatsCompo = characterDialog.Composers["playerstats"];
        if (playerStatsCompo is null) { return; }

        double indent = GuiElement.scaled(39);
        double gap = GuiElement.scaled(GuiElementItemSlotGridBase.unscaledSlotPadding);
        double offsetY = GuiElement.scaled(indent) + GuiElement.scaled(gap);
        double bgPadding = GuiElement.scaled(10);
        double firstWidth = GuiElement.scaled(100);

        IInventory _inv = capi.World.Player.InventoryManager.GetOwnInventory(GlobalConstants.characterInvClassName);
        if (_inv is not ArmorInventory inv)
        {
            return;
        }
        CairoFont textFont = CairoFont.WhiteSmallText();

        // should be used for dialog position
        //double padLeftX = playerStatsCompo.Bounds.fixedPaddingX + playerStatsCompo.Bounds.drawX;
        //double padLeftY = playerStatsCompo.Bounds.fixedPaddingY + playerStatsCompo.Bounds.drawY;

        ElementBounds mainBounds = ElementStdBounds.AutosizedMainDialog
            // todo: use playerstats borders for correct positions
            .RightOf(playerStatsCompo.Bounds);

        ElementBounds childBounds = new ElementBounds().WithSizing(ElementSizing.FitToChildren);
        ElementBounds backgroundBounds = childBounds.WithFixedPadding(bgPadding);

        ElementBounds textBounds = ElementBounds.FixedSize(firstWidth, indent).WithFixedOffset(0, indent);
        ElementBounds slot0Bounds = textBounds.RightCopy(gap).WithFixedWidth(indent);
        ElementBounds slot1Bounds = slot0Bounds.RightCopy(gap);
        ElementBounds slot2Bounds = slot1Bounds.RightCopy(gap);
 
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

        AddSlot(inv, ArmorLayers.Outer, DamageSystems.DamageZone.Head, ref slot0Bounds, gap);
        AddSlot(inv, ArmorLayers.Outer, DamageSystems.DamageZone.Face, ref slot0Bounds, gap);
        AddSlot(inv, ArmorLayers.Outer, DamageSystems.DamageZone.Neck, ref slot0Bounds, gap);
        AddSlot(inv, ArmorLayers.Outer, DamageSystems.DamageZone.Torso, ref slot0Bounds, gap);
        AddSlot(inv, ArmorLayers.Outer, DamageSystems.DamageZone.Arms, ref slot0Bounds, gap);
        AddSlot(inv, ArmorLayers.Outer, DamageSystems.DamageZone.Hands, ref slot0Bounds, gap);
        AddSlot(inv, ArmorLayers.Outer, DamageSystems.DamageZone.Legs, ref slot0Bounds, gap);
        AddSlot(inv, ArmorLayers.Outer, DamageSystems.DamageZone.Feet, ref slot0Bounds, gap);

        AddSlot(inv, ArmorLayers.Middle, DamageSystems.DamageZone.Head, ref slot1Bounds, gap);
        AddSlot(inv, ArmorLayers.Middle, DamageSystems.DamageZone.Face, ref slot1Bounds, gap);
        AddSlot(inv, ArmorLayers.Middle, DamageSystems.DamageZone.Neck, ref slot1Bounds, gap);
        AddSlot(inv, ArmorLayers.Middle, DamageSystems.DamageZone.Torso, ref slot1Bounds, gap);
        AddSlot(inv, ArmorLayers.Middle, DamageSystems.DamageZone.Arms, ref slot1Bounds, gap);
        AddSlot(inv, ArmorLayers.Middle, DamageSystems.DamageZone.Hands, ref slot1Bounds, gap);
        AddSlot(inv, ArmorLayers.Middle, DamageSystems.DamageZone.Legs, ref slot1Bounds, gap);
        AddSlot(inv, ArmorLayers.Middle, DamageSystems.DamageZone.Feet, ref slot1Bounds, gap);

        AddSlot(inv, ArmorLayers.Skin, DamageSystems.DamageZone.Head, ref slot2Bounds, gap);
        AddSlot(inv, ArmorLayers.Skin, DamageSystems.DamageZone.Face, ref slot2Bounds, gap);
        AddSlot(inv, ArmorLayers.Skin, DamageSystems.DamageZone.Neck, ref slot2Bounds, gap);
        AddSlot(inv, ArmorLayers.Skin, DamageSystems.DamageZone.Torso, ref slot2Bounds, gap);
        AddSlot(inv, ArmorLayers.Skin, DamageSystems.DamageZone.Arms, ref slot2Bounds, gap);
        AddSlot(inv, ArmorLayers.Skin, DamageSystems.DamageZone.Hands, ref slot2Bounds, gap);
        AddSlot(inv, ArmorLayers.Skin, DamageSystems.DamageZone.Legs, ref slot2Bounds, gap);
        AddSlot(inv, ArmorLayers.Skin, DamageSystems.DamageZone.Feet, ref slot2Bounds, gap);

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


    public void AddSlot(ArmorInventory inv, ArmorLayers layers, DamageZone zone, ref ElementBounds bounds, double gap)
    {
        int slotIndex = ArmorInventory.IndexFromArmorType(layers, zone);
        composer.AddItemSlotGrid(inv, SendInvPacket, 1, new int[] { slotIndex }, BelowCopySet(ref bounds, fixedDeltaY: gap));
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
