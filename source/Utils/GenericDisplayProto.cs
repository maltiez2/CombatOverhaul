using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace CombatOverhaul.Utils;

public abstract class GenericDisplayProto : BlockEntityContainer, ITexPositionSource
{
    public virtual string ClassCode => InventoryClassName;
    public virtual int DisplayedItems => Inventory.Count;
    public Size2i AtlasSize => ClientApi.BlockTextureAtlas.Size;
    public virtual string AttributeTransformCode => "onDisplayTransform";
    public readonly Dictionary<int, ModelTransform> EditedTransforms = new();

    public virtual TextureAtlasPosition this[string textureCode]
    {
        get
        {
            IDictionary<string, CompositeTexture> textures = NowTessellatingObj is Item item ? item.Textures : (NowTessellatingObj as Block).Textures;
            AssetLocation texturePath = null;
            CompositeTexture tex;

            // Prio 1: Get from collectible textures
            if (textures.TryGetValue(textureCode, out tex))
            {
                texturePath = tex.Baked.BakedName;
            }

            // Prio 2: Get from collectible textures, use "all" code
            if (texturePath == null && textures.TryGetValue("all", out tex))
            {
                texturePath = tex.Baked.BakedName;
            }

            // Prio 3: Get from currently tesselating shape
            if (texturePath == null)
            {
                NowTessellatingShape?.Textures.TryGetValue(textureCode, out texturePath);
            }

            // Prio 4: The code is the path
            if (texturePath == null)
            {
                texturePath = new AssetLocation(textureCode);
            }

            return getOrCreateTexPos(texturePath);
        }
    }


    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);

        ClientApi = api as ICoreClientAPI;
        if (ClientApi != null)
        {
            updateMeshes();
            api.Event.RegisterEventBusListener(OnEventBusEvent);
        }
    }
    public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
    {
        for (int index = 0; index < DisplayedItems; index++)
        {
            ItemSlot slot = Inventory[index];
            if (slot.Empty || TfMatrices == null)
            {
                continue;
            }
            mesher.AddMeshData(getMesh(slot.Itemstack), TfMatrices[index]);
        }

        return base.OnTesselation(mesher, tessThreadTesselator);
    }

    public virtual void updateMeshes(bool forceUpdate = false)
    {
        if (base.Api == null || base.Api.Side == EnumAppSide.Server) return;
        if (DisplayedItems == 0) return;

        for (int i = 0; i < DisplayedItems; i++)
        {
            updateMesh(i, forceUpdate);
        }

        TfMatrices = genTransformationMatrices();
    }

    public virtual void RegenerateMeshes()
    {
        for (int i = 0; i < DisplayedItems; i++)
        {
            if (Inventory[i].Empty) continue;
            string key = getMeshCacheKey(Inventory[i].Itemstack);
            MeshCache.Remove(key);
        }

        updateMeshes();
        MarkDirty(true);
    }

    protected CollectibleObject? NowTessellatingObj;
    protected Shape? NowTessellatingShape;
    protected ICoreClientAPI? ClientApi;
    protected float[][]? TfMatrices;


    protected virtual void updateMesh(int index, bool forceUpdate = false)
    {
        if (base.Api == null || base.Api.Side == EnumAppSide.Server) return;
        if (Inventory[index].Empty)
        {
            return;
        }

        getOrCreateMesh(Inventory[index].Itemstack, index, forceUpdate);
    }
    protected void OnEventBusEvent(string eventname, ref EnumHandling handling, IAttribute data)
    {
        if (eventname != "genjsontransform" && eventname != "oncloseedittransforms" &&
            eventname != "onapplytransforms") return;
        if (Inventory.Empty) return;

        for (int i = 0; i < DisplayedItems; i++)
        {
            if (Inventory[i].Empty) continue;
            string key = getMeshCacheKey(Inventory[i].Itemstack);
            MeshCache.Remove(key);
        }

        updateMeshes();
        MarkDirty(true);
    }
    protected virtual void RedrawAfterReceivingTreeAttributes(IWorldAccessor worldForResolving)
    {
        if (worldForResolving.Side == EnumAppSide.Client && base.Api != null)
        {
            updateMeshes();
            base.MarkDirty(true);  // always redraw on client after updating meshes
        }
    }
    protected TextureAtlasPosition getOrCreateTexPos(AssetLocation texturePath)
    {
        TextureAtlasPosition texpos = ClientApi.BlockTextureAtlas[texturePath];

        if (texpos == null)
        {
            bool ok = ClientApi.BlockTextureAtlas.GetOrInsertTexture(texturePath, out _, out texpos, null);

            if (!ok)
            {
                ClientApi.World.Logger.Warning("For render in block " + Block.Code + ", item {0} defined texture {1}, no such texture found.", NowTessellatingObj.Code, texturePath);
                return ClientApi.BlockTextureAtlas.UnknownTexturePosition;
            }
        }

        return texpos;
    }

    protected virtual string getMeshCacheKey(ItemStack stack)
    {
        IContainedMeshSource? meshSource = stack.Collectible as IContainedMeshSource;
        if (meshSource != null)
        {
            return meshSource.GetMeshCacheKey(stack);
        }

        int renderVariant = stack.Attributes?.GetInt("renderVariant", 0) ?? 0;

        return $"{stack.Collectible.Code}.{renderVariant}";
    }

    protected Dictionary<string, MeshData> MeshCache => ObjectCacheUtil.GetOrCreate(base.Api, "meshesDisplay-" + ClassCode, () => new Dictionary<string, MeshData>());

    protected MeshData getMesh(ItemStack stack)
    {
        string key = getMeshCacheKey(stack);
        MeshCache.TryGetValue(key, out MeshData? meshdata);
        return meshdata;
    }

    protected virtual MeshData getOrCreateMesh(ItemStack stack, int index, bool forceUpdate = false)
    {
        MeshData mesh = getMesh(stack);
        if (mesh != null && !forceUpdate) return mesh;

        IContainedMeshSource? meshSource = stack.Collectible as IContainedMeshSource;

        if (meshSource != null)
        {
            mesh = meshSource.GenMesh(stack, ClientApi.BlockTextureAtlas, Pos);
        }

        if (mesh == null)
        {
            ICoreClientAPI capi = base.Api as ICoreClientAPI;
            if (stack.Class == EnumItemClass.Block)
            {
                mesh = capi.TesselatorManager.GetDefaultBlockMesh(stack.Block).Clone();
            }
            else
            {
                int renderVariant = stack.Attributes.GetInt("renderVariant", 0);
                
                NowTessellatingObj = stack.Collectible;
                NowTessellatingShape = null;
                AssetLocation shapeLocation = null;
                CompositeShape compositeShape = null;
                if (stack.Item.Shape?.Base != null && renderVariant < 2)
                {
                    NowTessellatingShape = capi.TesselatorManager.GetCachedShape(stack.Item.Shape.Base);
                    shapeLocation = stack.Item.Shape.Base;
                    compositeShape = stack.Item.Shape;
                }
                else if (renderVariant >= 2)
                {
                    renderVariant -= 2;

                    if (stack.Item.Shape.BakedAlternates.Length > renderVariant)
                    {
                        NowTessellatingShape = capi.TesselatorManager.GetCachedShape(stack.Item.Shape.Alternates[renderVariant].Base);
                        shapeLocation = stack.Item.Shape.Alternates[renderVariant].Base;
                        compositeShape = stack.Item.Shape.Alternates[renderVariant];
                    }
                }
                capi.Tesselator.TesselateShape("", shapeLocation, compositeShape, out mesh, this);
                //capi.Tesselator.TesselateItem(stack.Item, out mesh, this);

                mesh.RenderPassesAndExtraBits.Fill((short)EnumChunkRenderPass.BlendNoCull);
            }
        }

        if (stack.Collectible.Attributes?[AttributeTransformCode].Exists == true)
        {
            if (EditedTransforms.ContainsKey(stack.Collectible.Id))
            {
                ModelTransform transform = EditedTransforms[stack.Collectible.Id];
                transform.EnsureDefaultValues();
                mesh.ModelTransform(transform);
            }
            else
            {
                ModelTransform transform = stack.Collectible.Attributes?[AttributeTransformCode].AsObject<ModelTransform>();
                transform.EnsureDefaultValues();
                mesh.ModelTransform(transform);
            }
        }
        else if (AttributeTransformCode == "onshelfTransform") // fallback to onDisplayTransform for onshelfTransform if it does not exist
        {
            if (stack.Collectible.Attributes?["onDisplayTransform"].Exists == true)
            {
                ModelTransform transform = stack.Collectible.Attributes?["onDisplayTransform"].AsObject<ModelTransform>();
                transform.EnsureDefaultValues();
                mesh.ModelTransform(transform);
            }
        }

        if (stack.Class == EnumItemClass.Item && (stack.Item.Shape == null || stack.Item.Shape.VoxelizeTexture))
        {
            mesh.Rotate(new Vec3f(0.5f, 0.5f, 0.5f), GameMath.PIHALF, 0, 0);
            mesh.Scale(new Vec3f(0.5f, 0.5f, 0.5f), 0.33f, 0.33f, 0.33f);
            mesh.Translate(0, -7.5f / 16f, 0f);
        }

        string key = getMeshCacheKey(stack);
        MeshCache[key] = mesh;

        return mesh;
    }
    
    protected abstract float[][] genTransformationMatrices();
}
