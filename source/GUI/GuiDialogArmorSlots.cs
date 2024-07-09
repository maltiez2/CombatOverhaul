using Cairo;
using CombatOverhaul.Armor;
using System.Reflection;
using System.Resources;
using System.Runtime.CompilerServices;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;
using Vintagestory.Common;

namespace CombatOverhaul.GUI;

public class GuiDialogArmorSlots : GuiDialog
{
    private bool recompose;
    private GuiComposer composer;
    private GuiDialogCharacter characterDialog;

    public const string DialogName = "combatoverhaul:armorslotsdialog";

    public override string ToggleKeyCombinationCode => null;

    public GuiDialogArmorSlots(ICoreClientAPI capi) : base(capi)
    {
        capi.Event.RegisterGameTickListener(Every500ms, 500); // remove after gui is done
        foreach (GuiDialogCharacter characterDialog in capi.Gui.LoadedGuis.Where(x => x is GuiDialogCharacter).Select(x => x as GuiDialogCharacter))
        {
            characterDialog.OnOpened += () => GuiDialogCharacter_OnOpened(characterDialog);
            characterDialog.OnClosed += () => GuiDialogCharacter_OnClose(characterDialog);
        }
    }

    private void Every500ms(float dt) // remove after gui is done
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

        //if (characterDialog.Composers["playerstats"] is null){    return; }

        double indent = GuiElement.scaled(45);
        double gap = GuiElement.scaled(GuiElementItemSlotGridBase.unscaledSlotPadding);
        double offsetY = indent + gap;

        IInventory inv = capi.World.Player.InventoryManager.GetOwnInventory(GlobalConstants.characterInvClassName);
        if (inv == null || inv.Empty)
        {
            return;
        }
        CairoFont textFont = CairoFont.WhiteMediumText();

        ElementBounds mainBounds = ElementStdBounds.AutosizedMainDialog
            .WithAlignment(EnumDialogArea.CenterMiddle)
            // todo: use playerstats borders for correct positions
            .WithFixedAlignmentOffset(50, 22.5);

        ElementBounds childBounds = new ElementBounds().WithSizing(ElementSizing.FitToChildren);
        ElementBounds backgroundBounds = childBounds.WithFixedPadding(GuiElement.scaled(15));

        ElementBounds firstBounds = ElementBounds.FixedSize(indent * 10, indent).WithFixedOffset(0, indent).WithFixedWidth(100);
        ElementBounds secondBounds = firstBounds.RightCopy(gap).WithFixedWidth(50);
        ElementBounds thirdBounds = secondBounds.RightCopy(gap);
        ElementBounds fourthBounds = thirdBounds.RightCopy(gap);
        ItemSlot slotHeadOuter = inv[ArmorInventory.IndexFromArmorType(ArmorLayers.Outer, DamageSystems.DamageZone.Head)];

        try
        {
            composer = Composers[DialogName] = capi.Gui.CreateCompo(DialogName, mainBounds)
            .AddDialogBG(backgroundBounds, false)
            .AddDialogTitleBarWithBg("test", () => TryClose())
            .BeginChildElements(childBounds)
                .AddDynamicText("", textFont, BelowCopySet(ref firstBounds, fixedDeltaY: gap), "textHead")
                .AddDynamicText("", textFont, BelowCopySet(ref firstBounds, fixedDeltaY: gap), "textFace")
                .AddDynamicText("", textFont, BelowCopySet(ref firstBounds, fixedDeltaY: gap), "textNeck")
                .AddDynamicText("", textFont, BelowCopySet(ref firstBounds, fixedDeltaY: gap), "textTorso")
                .AddDynamicText("", textFont, BelowCopySet(ref firstBounds, fixedDeltaY: gap), "textArms")
                .AddDynamicText("", textFont, BelowCopySet(ref firstBounds, fixedDeltaY: gap), "textHands")
                .AddDynamicText("", textFont, BelowCopySet(ref firstBounds, fixedDeltaY: gap), "textLegs")
                .AddDynamicText("", textFont, BelowCopySet(ref firstBounds, fixedDeltaY: gap), "textFeet")

                .AddStaticCustomDraw(secondBounds, OnDrawOuterIcon)
                .AddStaticCustomDraw(thirdBounds, OnDrawMiddleIcon)
                .AddStaticCustomDraw(fourthBounds, OnDrawSkinIcon)

                .AddItemSlotGrid(inv, SendInvPacket, 3, new int[] { ArmorInventory.IndexFromArmorType(ArmorLayers.Outer, DamageSystems.DamageZone.Head) }, BelowCopySet(ref secondBounds, fixedDeltaY: gap))
                .AddItemSlotGrid(inv, SendInvPacket, 3, new int[] { ArmorInventory.IndexFromArmorType(ArmorLayers.Outer, DamageSystems.DamageZone.Face) }, BelowCopySet(ref secondBounds, fixedDeltaY: gap))
                .AddItemSlotGrid(inv, SendInvPacket, 3, new int[] { ArmorInventory.IndexFromArmorType(ArmorLayers.Outer, DamageSystems.DamageZone.Neck) }, BelowCopySet(ref secondBounds, fixedDeltaY: gap))
                .AddItemSlotGrid(inv, SendInvPacket, 3, new int[] { ArmorInventory.IndexFromArmorType(ArmorLayers.Outer, DamageSystems.DamageZone.Torso) }, BelowCopySet(ref secondBounds, fixedDeltaY: gap))
                .AddItemSlotGrid(inv, SendInvPacket, 3, new int[] { ArmorInventory.IndexFromArmorType(ArmorLayers.Outer, DamageSystems.DamageZone.Arms) }, BelowCopySet(ref secondBounds, fixedDeltaY: gap))
                .AddItemSlotGrid(inv, SendInvPacket, 3, new int[] { ArmorInventory.IndexFromArmorType(ArmorLayers.Outer, DamageSystems.DamageZone.Hands) }, BelowCopySet(ref secondBounds, fixedDeltaY: gap))
                .AddItemSlotGrid(inv, SendInvPacket, 3, new int[] { ArmorInventory.IndexFromArmorType(ArmorLayers.Outer, DamageSystems.DamageZone.Legs) }, BelowCopySet(ref secondBounds, fixedDeltaY: gap))
                .AddItemSlotGrid(inv, SendInvPacket, 3, new int[] { ArmorInventory.IndexFromArmorType(ArmorLayers.Outer, DamageSystems.DamageZone.Feet) }, BelowCopySet(ref secondBounds, fixedDeltaY: gap))

                .AddItemSlotGrid(inv, SendInvPacket, 3, new int[] { ArmorInventory.IndexFromArmorType(ArmorLayers.Middle, DamageSystems.DamageZone.Head) }, BelowCopySet(ref thirdBounds, fixedDeltaY: gap))
                .AddItemSlotGrid(inv, SendInvPacket, 3, new int[] { ArmorInventory.IndexFromArmorType(ArmorLayers.Middle, DamageSystems.DamageZone.Face) }, BelowCopySet(ref thirdBounds, fixedDeltaY: gap))
                .AddItemSlotGrid(inv, SendInvPacket, 3, new int[] { ArmorInventory.IndexFromArmorType(ArmorLayers.Middle, DamageSystems.DamageZone.Neck) }, BelowCopySet(ref thirdBounds, fixedDeltaY: gap))
                .AddItemSlotGrid(inv, SendInvPacket, 3, new int[] { ArmorInventory.IndexFromArmorType(ArmorLayers.Middle, DamageSystems.DamageZone.Torso) }, BelowCopySet(ref thirdBounds, fixedDeltaY: gap))
                .AddItemSlotGrid(inv, SendInvPacket, 3, new int[] { ArmorInventory.IndexFromArmorType(ArmorLayers.Middle, DamageSystems.DamageZone.Arms) }, BelowCopySet(ref thirdBounds, fixedDeltaY: gap))
                .AddItemSlotGrid(inv, SendInvPacket, 3, new int[] { ArmorInventory.IndexFromArmorType(ArmorLayers.Middle, DamageSystems.DamageZone.Hands) }, BelowCopySet(ref thirdBounds, fixedDeltaY: gap))
                .AddItemSlotGrid(inv, SendInvPacket, 3, new int[] { ArmorInventory.IndexFromArmorType(ArmorLayers.Middle, DamageSystems.DamageZone.Legs) }, BelowCopySet(ref thirdBounds, fixedDeltaY: gap))
                .AddItemSlotGrid(inv, SendInvPacket, 3, new int[] { ArmorInventory.IndexFromArmorType(ArmorLayers.Middle, DamageSystems.DamageZone.Feet) }, BelowCopySet(ref thirdBounds, fixedDeltaY: gap))

                .AddItemSlotGrid(inv, SendInvPacket, 3, new int[] { ArmorInventory.IndexFromArmorType(ArmorLayers.Skin, DamageSystems.DamageZone.Head) }, BelowCopySet(ref fourthBounds, fixedDeltaY: gap))
                .AddItemSlotGrid(inv, SendInvPacket, 3, new int[] { ArmorInventory.IndexFromArmorType(ArmorLayers.Skin, DamageSystems.DamageZone.Face) }, BelowCopySet(ref fourthBounds, fixedDeltaY: gap))
                .AddItemSlotGrid(inv, SendInvPacket, 3, new int[] { ArmorInventory.IndexFromArmorType(ArmorLayers.Skin, DamageSystems.DamageZone.Neck) }, BelowCopySet(ref fourthBounds, fixedDeltaY: gap))
                .AddItemSlotGrid(inv, SendInvPacket, 3, new int[] { ArmorInventory.IndexFromArmorType(ArmorLayers.Skin, DamageSystems.DamageZone.Torso) }, BelowCopySet(ref fourthBounds, fixedDeltaY: gap))
                .AddItemSlotGrid(inv, SendInvPacket, 3, new int[] { ArmorInventory.IndexFromArmorType(ArmorLayers.Skin, DamageSystems.DamageZone.Arms) }, BelowCopySet(ref fourthBounds, fixedDeltaY: gap))
                .AddItemSlotGrid(inv, SendInvPacket, 3, new int[] { ArmorInventory.IndexFromArmorType(ArmorLayers.Skin, DamageSystems.DamageZone.Hands) }, BelowCopySet(ref fourthBounds, fixedDeltaY: gap))
                .AddItemSlotGrid(inv, SendInvPacket, 3, new int[] { ArmorInventory.IndexFromArmorType(ArmorLayers.Skin, DamageSystems.DamageZone.Legs) }, BelowCopySet(ref fourthBounds, fixedDeltaY: gap))
                .AddItemSlotGrid(inv, SendInvPacket, 3, new int[] { ArmorInventory.IndexFromArmorType(ArmorLayers.Skin, DamageSystems.DamageZone.Feet) }, BelowCopySet(ref fourthBounds, fixedDeltaY: gap))
            .EndChildElements()
            .Compose();
        }
        catch (Exception ex) { }
        composer?.GetDynamicText("textHead")?.SetNewText("Head");
        composer?.GetDynamicText("textFace")?.SetNewText("Face");
        composer?.GetDynamicText("textNeck")?.SetNewText("Neck");
        composer?.GetDynamicText("textTorso")?.SetNewText("Torso");
        composer?.GetDynamicText("textArms")?.SetNewText("Arms");
        composer?.GetDynamicText("textHands")?.SetNewText("Hands");
        composer?.GetDynamicText("textLegs")?.SetNewText("Legs");
        composer?.GetDynamicText("textFeet")?.SetNewText("Feet");
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
