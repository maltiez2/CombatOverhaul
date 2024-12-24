using CombatOverhaul.Utils;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace CombatOverhaul.Armor;

public class ItemWearableArmor : ItemWearable
{
    public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handHandling)
    {
        if (byEntity.Controls.ShiftKey)
        {
            base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handHandling);
            return;
        }

        if (slot.Itemstack.Item == null) return;

        IPlayer? player = (byEntity as EntityPlayer)?.Player;
        if (player == null) return;

        ArmorInventory? inventory = GetGearInventory(byEntity) as ArmorInventory;
        if (inventory == null) return;

        ArmorBehavior? behavior = GetCollectibleBehavior<ArmorBehavior>(true);
        if (behavior == null) return;

        string code = slot.Itemstack.Item.Code;
        ArmorType armorType = behavior.ArmorType;

        try
        {
            IEnumerable<int> slots = inventory.GetSlotBlockingSlotsIndices(armorType);

            foreach (int index in slots)
            {
                ItemStack stack = inventory[index].TakeOutWhole();
                if (!player.InventoryManager.TryGiveItemstack(stack))
                {
                    byEntity.Api.World.SpawnItemEntity(stack, byEntity.ServerPos.AsBlockPos);
                }
                inventory[index].MarkDirty();
            }

            int slotIndex = inventory.GetFittingSlotIndex(armorType);
            inventory[slotIndex].TryFlipWith(slot);

            inventory[slotIndex].MarkDirty();
            slot.MarkDirty();

            handHandling = EnumHandHandling.PreventDefault;
        }
        catch (Exception exception)
        {
            LoggerUtil.Error(api, this, $"Error on equipping '{code}' that occupies {armorType}:\n{exception}");
        }
    }
    public override void OnCreatedByCrafting(ItemSlot[] inSlots, ItemSlot outputSlot, GridRecipe byRecipe)
    {
        int newDurability = 0;

        if (outputSlot is not DummySlot)
        {
            EnsureConditionExists(outputSlot);
            outputSlot.Itemstack.Attributes.SetFloat("condition", 1);

            if (byRecipe.Name.Path.Contains("repair"))
            {
                CalculateRepairValueProperly(inSlots, outputSlot, out float repairValue, out int matCostPerMatType);

                int curDur = outputSlot.Itemstack.Collectible.GetRemainingDurability(outputSlot.Itemstack);
                int maxDur = GetMaxDurability(outputSlot.Itemstack);

                newDurability = Math.Min(maxDur, (int)(curDur + maxDur * repairValue));
            }
        }

        base.OnCreatedByCrafting(inSlots, outputSlot, byRecipe);

        // Prevent derp in the handbook
        if (outputSlot is DummySlot) return;

        if (byRecipe.Name.Path.Contains("repair"))
        {
            outputSlot.Itemstack.Attributes.SetInt("durability", newDurability);
        }
    }
    public override bool ConsumeCraftingIngredients(ItemSlot[] inSlots, ItemSlot outputSlot, GridRecipe recipe)
    {
        // Consume as much materials in the input grid as needed
        if (recipe.Name.Path.Contains("repair"))
        {
            CalculateRepairValueProperly(inSlots, outputSlot, out float repairValue, out int matCostPerMatType);

            foreach (ItemSlot islot in inSlots)
            {
                if (islot.Empty) continue;

                if (islot.Itemstack.Collectible == this) { islot.Itemstack = null; continue; }

                islot.TakeOut(matCostPerMatType);
            }

            return true;
        }

        return false;
    }

    protected static InventoryBase? GetGearInventory(Entity entity)
    {
        return entity.GetBehavior<EntityBehaviorPlayerInventory>().Inventory;
    }
    protected virtual void EnsureConditionExists(ItemSlot slot)
    {
        // Prevent derp in the handbook
        if (slot is DummySlot) return;

        if (!slot.Itemstack.Attributes.HasAttribute("condition") && api.Side == EnumAppSide.Server)
        {
            if (slot.Itemstack.ItemAttributes?["warmth"].Exists == true && slot.Itemstack.ItemAttributes?["warmth"].AsFloat() != 0)
            {
                if (slot is ItemSlotTrade)
                {
                    slot.Itemstack.Attributes.SetFloat("condition", (float)api.World.Rand.NextDouble() * 0.25f + 0.75f);
                }
                else
                {
                    slot.Itemstack.Attributes.SetFloat("condition", (float)api.World.Rand.NextDouble() * 0.4f);
                }

                slot.MarkDirty();
            }
        }
    }
    protected virtual void CalculateRepairValueProperly(ItemSlot[] inSlots, ItemSlot outputSlot, out float repairValue, out int matCostPerMatType)
    {
        int origMatCount = GetOrigMatCount(inSlots, outputSlot);
        if (origMatCount == 0)
        {
            origMatCount = Attributes["materialCount"].AsInt(1);
        }

        ItemSlot? armorSlot = inSlots.FirstOrDefault(slot => slot.Itemstack?.Collectible is ItemWearable);
        int curDur = outputSlot.Itemstack.Collectible.GetRemainingDurability(armorSlot.Itemstack);
        int maxDur = GetMaxDurability(outputSlot.Itemstack);

        // How much 1x mat repairs in %
        float repairValuePerItem = 2f / origMatCount;
        // How much the mat repairs in durability
        float repairDurabilityPerItem = repairValuePerItem * maxDur;
        // Divide missing durability by repair per item = items needed for full repair 
        int fullRepairMatCount = (int)Math.Max(1, Math.Round((maxDur - curDur) / repairDurabilityPerItem));
        // Limit repair value to smallest stack size of all repair mats
        int minMatStackSize = GetInputRepairCount(inSlots);
        // Divide the cost amongst all mats
        int matTypeCount = GetRepairMatTypeCount(inSlots);

        int availableRepairMatCount = Math.Min(fullRepairMatCount, minMatStackSize * matTypeCount);
        matCostPerMatType = Math.Min(fullRepairMatCount, minMatStackSize);

        // Repairing costs half as many materials as newly creating it
        repairValue = (float)availableRepairMatCount / origMatCount * 2;
    }
    protected virtual int GetRepairMatTypeCount(ItemSlot[] slots)
    {
        List<ItemStack> stackTypes = new();
        foreach (ItemSlot slot in slots)
        {
            if (slot.Empty) continue;
            bool found = false;
            if (slot.Itemstack.Collectible is ItemWearable) continue;

            foreach (ItemStack stack in stackTypes)
            {
                if (slot.Itemstack.Satisfies(stack))
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                stackTypes.Add(slot.Itemstack);
            }
        }

        return stackTypes.Count;
    }
}

public class ItemHelmetWithVisor : ItemWearableArmor
{
    public override void OnLoaded(ICoreAPI api)
    {
        AttachableToEntity = IAttachableToEntity.FromCollectible(this) != null;
        base.OnLoaded(api);
    }
    public override void OnBeforeRender(ICoreClientAPI capi, ItemStack itemstack, EnumItemRenderTarget target, ref ItemRenderInfo renderinfo)
    {
        if (!AttachableToEntity) return;

        Dictionary<string, MultiTextureMeshRef> meshrefs = ObjectCacheUtil.GetOrCreate(capi, "wearableAttachmentMeshRefs", () => new Dictionary<string, MultiTextureMeshRef>());
        string key = GetMeshCacheKey(itemstack);

        if (!meshrefs.TryGetValue(key, out renderinfo.ModelRef))
        {
            ITexPositionSource texSource = capi.Tesselator.GetTextureSource(itemstack.Item);
            MeshData? mesh = GenMesh(capi, itemstack, texSource);
            renderinfo.ModelRef = meshrefs[key] = mesh == null ? renderinfo.ModelRef : capi.Render.UploadMultiTextureMesh(mesh);
        }

        if (Attributes["visibleDamageEffect"].AsBool())
        {
            renderinfo.DamageEffect = Math.Max(0, 1 - (float)GetRemainingDurability(itemstack) / GetMaxDurability(itemstack) * 1.1f);
        }
    }
    public override string GetMeshCacheKey(ItemStack itemstack)
    {
        if (Opened(itemstack))
        {
            return "wearableModelRef-" + itemstack.Collectible.Code.ToString() + "-opened";
        }
        else
        {
            return "wearableModelRef-" + itemstack.Collectible.Code.ToString() + "-closed";
        }
    }

    public void Open(ItemSlot slot) => slot?.Itemstack?.Attributes.SetBool(VisorStateAttribute, true);
    public void Close(ItemSlot slot) => slot?.Itemstack?.Attributes.SetBool(VisorStateAttribute, false);
    public void Switch(ItemSlot slot) => slot?.Itemstack?.Attributes.SetBool(VisorStateAttribute, !Opened(slot.Itemstack));

    protected bool AttachableToEntity;
    protected Shape? NowTesselatingShape;
    protected string VisorStateAttribute = "visorOpened";

    protected virtual MeshData? GenMesh(ICoreClientAPI capi, ItemStack itemstack, ITexPositionSource texSource)
    {
        JsonObject attrObj = itemstack.Collectible.Attributes;
        EntityProperties props = capi.World.GetEntityType(new AssetLocation(attrObj?["wearerEntityCode"].ToString() ?? "player"));
        Shape entityShape = props.Client.LoadedShape;
        AssetLocation shapePathForLogging = props.Client.Shape.Base;
        Shape newShape;


        if (!AttachableToEntity)
        {
            // No need to step parent anything if its just a texture on the seraph
            newShape = entityShape;
        }
        else
        {
            newShape = new Shape()
            {
                Elements = entityShape.CloneElements(),
                Animations = entityShape.CloneAnimations(),
                AnimationsByCrc32 = entityShape.AnimationsByCrc32,
                JointsById = entityShape.JointsById,
                TextureWidth = entityShape.TextureWidth,
                TextureHeight = entityShape.TextureHeight,
                Textures = null,
            };
        }

        MeshData meshdata;
        if (attrObj["wearableInvShape"].Exists)
        {
            AssetLocation shapePath = new("shapes/" + attrObj["wearableInvShape"] + ".json");
            Shape shape = Vintagestory.API.Common.Shape.TryGet(capi, shapePath);
            capi.Tesselator.TesselateShape(itemstack.Collectible, shape, out meshdata);
        }
        else
        {
            CompositeShape compArmorShape = !attrObj[ShapeAttribute(itemstack)].Exists ? (itemstack.Class == EnumItemClass.Item ? itemstack.Item.Shape : itemstack.Block.Shape) : attrObj[ShapeAttribute(itemstack)].AsObject<CompositeShape>(null, itemstack.Collectible.Code.Domain);

            if (compArmorShape == null)
            {
                capi.World.Logger.Warning("Wearable shape {0} {1} does not define a shape through either the shape property or the attachShape Attribute. Item will be invisible.", itemstack.Class, itemstack.Collectible.Code);
                return null;
            }

            AssetLocation shapePath = compArmorShape.Base.CopyWithPath("shapes/" + compArmorShape.Base.Path + ".json");

            Shape armorShape = Vintagestory.API.Common.Shape.TryGet(capi, shapePath);
            if (armorShape == null)
            {
                capi.World.Logger.Warning("Wearable shape {0} defined in {1} {2} not found or errored, was supposed to be at {3}. Item will be invisible.", compArmorShape.Base, itemstack.Class, itemstack.Collectible.Code, shapePath);
                return null;
            }

            newShape.StepParentShape(armorShape, shapePath.ToShortString(), shapePathForLogging.ToShortString(), capi.Logger, (key, code) => { });

            if (compArmorShape.Overlays != null)
            {
                foreach (CompositeShape? overlay in compArmorShape.Overlays)
                {
                    Shape oshape = Vintagestory.API.Common.Shape.TryGet(capi, overlay.Base.CopyWithPath("shapes/" + overlay.Base.Path + ".json"));
                    if (oshape == null)
                    {
                        capi.World.Logger.Warning("Wearable shape {0} overlay {4} defined in {1} {2} not found or errored, was supposed to be at {3}. Item will be invisible.", compArmorShape.Base, itemstack.Class, itemstack.Collectible.Code, shapePath, overlay.Base);
                        continue;
                    }

                    newShape.StepParentShape(oshape, overlay.Base.ToShortString(), shapePathForLogging.ToShortString(), capi.Logger, (key, Code) => { });
                }
            }

            NowTesselatingShape = newShape;
            capi.Tesselator.TesselateShapeWithJointIds("entity", newShape, out meshdata, texSource, new Vec3f());
            NowTesselatingShape = null;
        }

        return meshdata;
    }

    protected virtual bool Opened(ItemStack? stack) => stack?.Attributes.GetBool(VisorStateAttribute, false) ?? false;
    protected virtual string ShapeAttribute(ItemStack stack) => Opened(stack) ? "attachShapeOpened" : "attachShapeClosed";
}