using Newtonsoft.Json.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace CombatOverhaul;

public class TextureFromAttributes : CollectibleBehavior, IContainedMeshSource
{
    private Dictionary<int, MultiTextureMeshRef> Meshrefs => ObjectCacheUtil.GetOrCreate(_api, "TextureFromAttributesMeshrefs", () => new Dictionary<int, MultiTextureMeshRef>());
    private ICoreClientAPI? _clientAPI;
    private ICoreAPI? _api;
    private readonly Item _item;
    private List<string> _materialTypes = new();
    private string _textureCode = string.Empty;
    private string _textureAttribute = string.Empty;
    private string _defaultTexture = string.Empty;
    private string[] _creativeTabs = Array.Empty<string>();

    public TextureFromAttributes(CollectibleObject collObj) : base(collObj)
    {
        _item = collObj as Item ?? throw new Exception("Only for items");
    }

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);

        _api = api;
        _clientAPI = api as ICoreClientAPI;

        AddAllTypesToCreativeInventory();
    }

    public override void Initialize(JsonObject properties)
    {
        base.Initialize(properties);

        _materialTypes = properties["textureTypes"].AsObject<List<string>>();
        _textureCode = properties["textureCode"].AsString();
        _textureAttribute = properties["textureAttribute"].AsString();
        _defaultTexture = properties["defaultTexture"].AsString();
        _creativeTabs = properties["creativeTabs"].AsObject<string[]>();
    }

    public override void OnBeforeRender(ICoreClientAPI capi, ItemStack itemstack, EnumItemRenderTarget target, ref ItemRenderInfo renderinfo)
    {
        int meshrefId = itemstack.TempAttributes.GetInt("meshRefId");
        if (meshrefId == 0 || !Meshrefs.TryGetValue(meshrefId, out renderinfo.ModelRef))
        {
            int id = Meshrefs.Count + 1;
            MultiTextureMeshRef modelref = capi.Render.UploadMultiTextureMesh(GenMesh(itemstack, capi.ItemTextureAtlas));
            renderinfo.ModelRef = Meshrefs[id] = modelref;

            itemstack.TempAttributes.SetInt("meshRefId", id);
        }
    }

    public MeshData GenMesh(ItemStack itemstack, ITextureAtlasAPI targetAtlas)
    {
        ContainedTextureSource textureSource = new(_api as ICoreClientAPI, targetAtlas, new Dictionary<string, AssetLocation>(), $"For render in '{_item.Code}'");

        textureSource.Textures.Clear();

        string? textureName = itemstack.Attributes.GetString(_textureAttribute);

        if (_clientAPI == null) return new MeshData();
        if (textureName == null) textureName = "";

        textureSource.Textures[_textureCode] = new AssetLocation(_defaultTexture);

        Shape? shape = _clientAPI.TesselatorManager.GetCachedShape(_item.Shape.Base);

        if (shape == null) return new MeshData();

        foreach ((string textureCode, AssetLocation textureLocation) in shape.Textures)
        {
            if (_item.Textures.TryGetValue(textureCode, out CompositeTexture texture))
            {
                textureSource.Textures[textureCode] = texture.Base;
            }
            else
            {
                textureSource.Textures[textureCode] = textureLocation;
            }
        }

        if (textureName != "")
        {
            textureSource.Textures[_textureCode] = new AssetLocation(textureName + ".png");
        }

        _clientAPI.Tesselator.TesselateItem(_item, out MeshData mesh, textureSource);

        return mesh;
    }

    public MeshData GenMesh(ItemStack itemstack, ITextureAtlasAPI targetAtlas, BlockPos atBlockPos)
    {
        return GenMesh(itemstack, targetAtlas);
    }

    public string GetMeshCacheKey(ItemStack itemstack)
    {
        string wood = itemstack.Attributes.GetString(_textureAttribute).Replace('/', '-');
        return _item.Code.ToShortString() + "-" + wood;
    }

    private void AddAllTypesToCreativeInventory()
    {
        List<JsonItemStack> stacks = new();

        foreach (string material in _materialTypes)
        {
            stacks.Add(GenStackJson(string.Format("{{ {1}: \"{0}\" }}", material, _textureAttribute)));
        }

        JsonItemStack noAttributesStack = new()
        {
            Code = _item.Code,
            Type = EnumItemClass.Item
        };
        noAttributesStack.Resolve(_api.World, "handle type");

        if (_item.CreativeInventoryStacks == null)
        {
            _item.CreativeInventoryStacks = new CreativeTabAndStackList[] {
                new() { Stacks = stacks.ToArray(), Tabs = _creativeTabs },
                new() { Stacks = new JsonItemStack[] { noAttributesStack }, Tabs = _item.CreativeInventoryTabs }
            };
            _item.CreativeInventoryTabs = null;
        }
        else
        {
            _item.CreativeInventoryStacks = _item.CreativeInventoryStacks.Append(new CreativeTabAndStackList() { Stacks = stacks.ToArray(), Tabs = _creativeTabs });
        }
    }
    private JsonItemStack GenStackJson(string json)
    {
        JsonItemStack stackJson = new()
        {
            Code = _item.Code,
            Type = EnumItemClass.Item,
            Attributes = new JsonObject(JToken.Parse(json))
        };

        stackJson.Resolve(_api?.World, "handle type");

        return stackJson;
    }
}


public class TextureConfig
{
    public string Code { get; set; } = "";
    public string Attribute { get; set; } = "";
    public string Default { get; set; } = "";
    public string[] HandbookValues { get; set; } = Array.Empty<string>();
}

public class TexturesFromAttributesProperties
{
    public TextureConfig[] Textures { get; set; } = Array.Empty<TextureConfig>();
    public string[] CreativeTabs { get; set; } = Array.Empty<string>();
    public bool AddNoAttributesItem { get; set; } = true;
}

public class TexturesFromAttributes : CollectibleBehavior, IContainedMeshSource
{
    private Dictionary<int, MultiTextureMeshRef> Meshrefs => ObjectCacheUtil.GetOrCreate(_api, "TextureFromAttributesMeshrefs", () => new Dictionary<int, MultiTextureMeshRef>());
    private ICoreClientAPI? _clientAPI;
    private ICoreAPI? _api;
    private readonly Item _item;

    private TexturesFromAttributesProperties? _properties;

    public TexturesFromAttributes(CollectibleObject collObj) : base(collObj)
    {
        _item = collObj as Item ?? throw new Exception("Only for items");
    }

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);

        _api = api;
        _clientAPI = api as ICoreClientAPI;

        AddAllTypesToCreativeInventory();
    }

    public override void OnUnloaded(ICoreAPI api)
    {
        if (api.ObjectCache.ContainsKey("TextureFromAttributesMeshrefs") && Meshrefs.Count > 0)
        {
            foreach ((int _, MultiTextureMeshRef meshRef) in Meshrefs)
            {
                meshRef.Dispose();
            }

            ObjectCacheUtil.Delete(api, "TextureFromAttributesMeshrefs");
        }
        base.OnUnloaded(api);
    }

    public override void Initialize(JsonObject properties)
    {
        base.Initialize(properties);

        _properties = properties.AsObject<TexturesFromAttributesProperties>();
    }

    public override void OnBeforeRender(ICoreClientAPI capi, ItemStack itemstack, EnumItemRenderTarget target, ref ItemRenderInfo renderinfo)
    {
        int meshrefId = itemstack.TempAttributes.GetInt("meshRefId");
        if (meshrefId == 0 || !Meshrefs.TryGetValue(meshrefId, out renderinfo.ModelRef))
        {
            int id = Meshrefs.Count + 1;
            MultiTextureMeshRef modelref = capi.Render.UploadMultiTextureMesh(GenMesh(itemstack, capi.ItemTextureAtlas));
            renderinfo.ModelRef = Meshrefs[id] = modelref;

            itemstack.TempAttributes.SetInt("meshRefId", id);
        }
    }

    public MultiTextureMeshRef GetMeshRef(ICoreClientAPI capi, ItemStack itemstack, EnumItemRenderTarget target, ref ItemRenderInfo renderinfo, Shape shape)
    {
        int meshrefId = itemstack.TempAttributes.GetInt("meshRefId");
        if (meshrefId == 0 || !Meshrefs.TryGetValue(meshrefId, out renderinfo.ModelRef))
        {
            int id = Meshrefs.Count + 1;
            MultiTextureMeshRef modelref = capi.Render.UploadMultiTextureMesh(GenMesh(itemstack, capi.ItemTextureAtlas, shape));
            renderinfo.ModelRef = Meshrefs[id] = modelref;

            itemstack.TempAttributes.SetInt("meshRefId", id);
        }
        return renderinfo.ModelRef;
    }

    public MeshData GenMesh(ItemStack itemstack, ITextureAtlasAPI targetAtlas, Shape? overrideShape = null)
    {
        ContainedTextureSource textureSource = new(_api as ICoreClientAPI, targetAtlas, new Dictionary<string, AssetLocation>(), $"For render in '{_item.Code}'");

        textureSource.Textures.Clear();

        if (_clientAPI == null || _properties == null) return new MeshData();

        Shape? shape = overrideShape ?? _clientAPI.TesselatorManager.GetCachedShape(_item.Shape.Base);

        if (shape == null) return new MeshData();

        foreach ((string textureCode, AssetLocation textureLocation) in shape.Textures)
        {
            if (_item.Textures.TryGetValue(textureCode, out CompositeTexture? texture))
            {
                textureSource.Textures[textureCode] = texture.Base;
            }
            else
            {
                textureSource.Textures[textureCode] = textureLocation;
            }
        }

        foreach (TextureConfig textureProperty in _properties.Textures)
        {
            string texturePath = itemstack.Attributes.GetString(textureProperty.Attribute) ?? textureProperty.Default;

            textureSource.Textures[textureProperty.Code] = new AssetLocation(texturePath + ".png");
        }

        _clientAPI.Tesselator.TesselateItem(_item, out MeshData mesh, textureSource);

        return mesh;
    }

    public MeshData GenMesh(ItemStack itemstack, ITextureAtlasAPI targetAtlas, BlockPos atBlockPos)
    {
        return GenMesh(itemstack, targetAtlas);
    }

    public string GetMeshCacheKey(ItemStack itemstack)
    {
        string cacheKey = _item.Code.ToShortString();

        foreach (TextureConfig textureProperty in _properties.Textures)
        {
            cacheKey += "-" + itemstack.Attributes.GetString(textureProperty.Attribute)?.Replace('/', '-') ?? "default";
        }

        return cacheKey;
    }

    private void ConstructStackRecursively(List<JsonItemStack> stacks, string jsonAttributes, int index)
    {
        if (_properties == null) return;

        if (_properties.Textures.Length <= index)
        {
            jsonAttributes += "}";
            stacks.Add(GenStackJson(jsonAttributes));
            return;
        }

        TextureConfig textureProperties = _properties.Textures[index];

        if (jsonAttributes != "{") jsonAttributes += ", ";
        foreach (string texturePath in textureProperties.HandbookValues)
        {
            string jsonAttributesCopy = (string)jsonAttributes.Clone();
            jsonAttributesCopy += $"{textureProperties.Attribute}: \"{texturePath}\"";
            ConstructStackRecursively(stacks, jsonAttributesCopy, index + 1);
        }
    }

    private void AddAllTypesToCreativeInventory()
    {
        if (_properties == null) return;
        
        List<JsonItemStack> stacks = new();

        ConstructStackRecursively(stacks, "{", 0);

        JsonItemStack noAttributesStack = new()
        {
            Code = _item.Code,
            Type = EnumItemClass.Item
        };
        noAttributesStack.Resolve(_api?.World, "handle type");

        if (_item.CreativeInventoryStacks == null)
        {
            if (_properties.AddNoAttributesItem || stacks.Count == 0)
            {
                _item.CreativeInventoryStacks = new CreativeTabAndStackList[] {
                    new() { Stacks = stacks.ToArray(), Tabs = _properties.CreativeTabs },
                    new() { Stacks = new JsonItemStack[] { noAttributesStack }, Tabs = _item.CreativeInventoryTabs }
                };
                _item.CreativeInventoryTabs = null;
            }
            else
            {
                _item.CreativeInventoryStacks = new CreativeTabAndStackList[] {
                    new() { Stacks = stacks.ToArray(), Tabs = _properties.CreativeTabs },
                    new() { Stacks = new JsonItemStack[] { stacks[0] }, Tabs = _item.CreativeInventoryTabs }
                };
                _item.CreativeInventoryTabs = null;
            }
            
        }
    }
    private JsonItemStack GenStackJson(string json)
    {
        JsonItemStack stackJson = new()
        {
            Code = _item.Code,
            Type = EnumItemClass.Item,
            Attributes = new JsonObject(JToken.Parse(json))
        };

        stackJson.Resolve(_api?.World, "textures type");

        return stackJson;
    }
}