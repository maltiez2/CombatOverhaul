using CombatOverhaul.Integration;
using System.Diagnostics;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace CombatOverhaul.Animations;

public sealed class AnimatableShape : ITexPositionSource, IDisposable
{
    public Shape Shape { get; private set; }
    public MultiTextureMeshRef MeshRef { get; private set; }
    public ITextureAtlasAPI Atlas { get; private set; }

    public static AnimatableShape? Create(ICoreClientAPI api, string shapePath, Item item)
    {
        string cacheKey = $"shapeEditorCollectibleMeshes-{shapePath}";
        AssetLocation shapeLocation = new(shapePath);
        shapeLocation = shapeLocation.WithPathAppendixOnce(".json").WithPathPrefixOnce("shapes/");

        Shape? currentShape = Shape.TryGet(api, shapeLocation);
        currentShape?.ResolveReferences(api.Logger, cacheKey);

        if (currentShape == null) return null;

        AnimatableShape shape = new(api, cacheKey, currentShape, item);

        shape.Shape.ResolveReferences(api.Logger, "creating new animatable shape");

        return shape;
    }
    public AnimatorBase? GetAnimator(long entityId)
    {
        if (_animators.ContainsKey(entityId)) return _animators[entityId];

        RemoveAnimatorsForNonValidEntities();

        string cacheKey = $"{_cachePrefix}.{entityId}";

        AnimatorBase? animator = GetAnimator(_clientApi, cacheKey, Shape);

        if (animator == null) return null;

        _animators.Add(entityId, animator);
        _cacheKeys.Add(entityId, cacheKey);
        return animator;
    }
    public void Render(
        IShaderProgram shaderProgram,
        ItemRenderInfo itemStackRenderInfo,
        IRenderAPI render,
        ItemStack itemStack,
        Vec4f lightrgbs,
        Matrixf itemModelMat,
        Entity entity,
        float dt
        ) => _renderer.Render(shaderProgram, itemStackRenderInfo, render, itemStack, lightrgbs, itemModelMat, entity, dt);
    public void Dispose()
    {
        MeshRef.Dispose();
    }

    private readonly ICoreClientAPI _clientApi;
    private readonly AnimatableShapeRenderer _renderer;
    private readonly Dictionary<long, AnimatorBase> _animators = new();
    private readonly Dictionary<long, string> _cacheKeys = new();
    private readonly Item _item;
    private readonly string _cachePrefix;

    private AnimatableShape(ICoreClientAPI api, string cacheKey, Shape currentShape, Item item)
    {
        _clientApi = api;
        _cachePrefix = cacheKey;
        Shape = currentShape;
        Atlas = api.ItemTextureAtlas;
        _item = item;

        MeshData meshData = InitializeMeshData(api, cacheKey, currentShape, this);
        MeshRef = InitializeMeshRef(api, meshData);
        _renderer = new(api, this);
    }
    private void RemoveAnimatorsForNonValidEntities()
    {
        foreach ((long entityId, _) in _animators)
        {
            Entity? entity = _clientApi.World.GetEntityById(entityId);

            if (entity == null || !entity.Alive)
            {
                _animators.Remove(entityId);
                _cacheKeys.Remove(entityId);
            }
        }
    }
    private static MeshData InitializeMeshData(ICoreClientAPI clientApi, string cacheDictKey, Shape shape, ITexPositionSource texSource)
    {
        shape.ResolveReferences(clientApi.World.Logger, cacheDictKey);
        CacheInvTransforms(shape.Elements);
        shape.ResolveAndFindJoints(clientApi.Logger, cacheDictKey);

        clientApi.Tesselator.TesselateShapeWithJointIds("collectible", shape, out MeshData meshData, texSource, null);

        return meshData.Clone();
    }
    private static MultiTextureMeshRef InitializeMeshRef(ICoreClientAPI clientApi, MeshData meshData)
    {
        MultiTextureMeshRef? meshRef = null;

        if (RuntimeEnv.MainThreadId == Environment.CurrentManagedThreadId)
        {
            meshRef = clientApi.Render.UploadMultiTextureMesh(meshData);
        }
        else
        {
            clientApi.Event.EnqueueMainThreadTask(() =>
            {
                meshRef = clientApi.Render.UploadMultiTextureMesh(meshData);
            }, "uploadmesh");
        }

        Debug.Assert(meshRef != null);
        return meshRef;
    }
    private static void CacheInvTransforms(ShapeElement[] elements)
    {
        if (elements == null) return;

        for (int i = 0; i < elements.Length; i++)
        {
            elements[i].CacheInverseTransformMatrix();
            CacheInvTransforms(elements[i].Children);
        }
    }
    private static AnimatorBase? GetAnimator(ICoreClientAPI clientApi, string cacheDictKey, Shape? shape)
    {
        if (shape == null)
        {
            return null;
        }

        Dictionary<string, AnimCacheEntry>? animationCache;
        clientApi.ObjectCache.TryGetValue("proceduralAnimatorsCache", out object? animCacheObj);
        animationCache = animCacheObj as Dictionary<string, AnimCacheEntry>;
        if (animationCache == null)
        {
            clientApi.ObjectCache["proceduralAnimatorsCache"] = animationCache = new Dictionary<string, AnimCacheEntry>();
        }

        AnimatorBase animator;

        if (animationCache.TryGetValue(cacheDictKey, out AnimCacheEntry? cacheObj))
        {
            animator = clientApi.Side == EnumAppSide.Client ?
                new ClientAnimator(() => 1, cacheObj.RootPoses, cacheObj.Animations, cacheObj.RootElems, shape.JointsById) :
                new ServerAnimator(() => 1, cacheObj.RootPoses, cacheObj.Animations, cacheObj.RootElems, shape.JointsById)
            ;
        }
        else
        {
            for (int i = 0; shape.Animations != null && i < shape.Animations.Length; i++)
            {
                try
                {
                    shape.Animations[i].GenerateAllFrames(shape.Elements, shape.JointsById);
                }
                catch (Exception ex) { }
            }

            animator = clientApi.Side == EnumAppSide.Client ?
                new ClientAnimator(() => 1, shape.Animations, shape.Elements, shape.JointsById) :
                new ServerAnimator(() => 1, shape.Animations, shape.Elements, shape.JointsById)
            ;

            animationCache[cacheDictKey] = new AnimCacheEntry()
            {
                Animations = shape.Animations,
                RootElems = (animator as ClientAnimator)?.RootElements,
                RootPoses = (animator as ClientAnimator)?.RootPoses
            };
        }

        return animator;
    }

    #region ITexPositionSource
    public Size2i? AtlasSize => Atlas?.Size;
    public TextureAtlasPosition? this[string textureCode]
    {
        get
        {
            AssetLocation? texturePath = null;
            if (_item.Textures.ContainsKey(textureCode))
            {
                texturePath = _item.Textures[textureCode].Base;
            }
            else
            {
                Shape?.Textures.TryGetValue(textureCode, out texturePath);
            }

            if (texturePath == null)
            {
                texturePath = new AssetLocation(textureCode);
            }

            return GetOrCreateTexPos(texturePath);
        }
    }
    private TextureAtlasPosition? GetOrCreateTexPos(AssetLocation texturePath)
    {
        if (Atlas == null) return null;

        TextureAtlasPosition texturePosition = Atlas[texturePath];

        if (texturePosition == null)
        {
            IAsset texAsset = _clientApi.Assets.TryGet(texturePath.Clone().WithPathPrefixOnce("textures/").WithPathAppendixOnce(".png"));
            if (texAsset != null)
            {
                Atlas.GetOrInsertTexture(texturePath, out _, out texturePosition);
            }
            else
            {
                _clientApi.World.Logger.Warning($"[Animation Manager] texture {texturePath}, not no such texture found.");
            }
        }

        return texturePosition;
    }
    #endregion
}

internal class AnimatableShapeRenderer
{
    public AnimatableShapeRenderer(ICoreClientAPI api, AnimatableShape shape)
    {
        _clientApi = api;
        _shape = shape;
    }
    public void Render(IShaderProgram shaderProgram, ItemRenderInfo itemStackRenderInfo, IRenderAPI render, ItemStack itemStack, Vec4f lightrgbs, Matrixf itemModelMat, Entity entity, float dt)
    {
        RenderAnimatableShape(shaderProgram, _clientApi.World, _shape, itemStackRenderInfo, render, itemStack, entity, lightrgbs, itemModelMat);
        SpawnParticles(itemModelMat, itemStack, dt, ref _timeAccumulation, _clientApi, entity);
    }

    private float _timeAccumulation = 0;
    private readonly ICoreClientAPI _clientApi;
    private readonly AnimatableShape _shape;

    private static void RenderAnimatableShape(IShaderProgram shaderProgram, IWorldAccessor world, AnimatableShape shape, ItemRenderInfo itemStackRenderInfo, IRenderAPI render, ItemStack itemStack, Entity entity, Vec4f lightrgbs, Matrixf itemModelMat)
    {
        string textureSampleName = "tex";

        shaderProgram.Use();

        AnimatorBase? animator = shape.GetAnimator(entity.EntityId);
        if (animator == null)
        {
            shaderProgram.Stop();
            return;
        }
        FillShaderValues(shaderProgram, itemStackRenderInfo, render, itemStack, lightrgbs, itemModelMat, world, animator);

        if (!itemStackRenderInfo.CullFaces)
        {
            render.GlDisableCullFace();
        }
        render.RenderMultiTextureMesh(shape.MeshRef, textureSampleName);
        if (!itemStackRenderInfo.CullFaces)
        {
            render.GlEnableCullFace();
        }

        shaderProgram.Uniform("damageEffect", 0f);
        shaderProgram.Stop();
    }
    private static void SpawnParticles(Matrixf itemModelMat, ItemStack itemStack, float dt, ref float timeAccumulation, ICoreClientAPI api, Entity entity)
    {
        if (itemStack.Collectible?.ParticleProperties == null) return;

        float windStrength = Math.Max(0f, 1f - api.World.BlockAccessor.GetDistanceToRainFall(entity.Pos.AsBlockPos) / 5f);
        AdvancedParticleProperties[] particleProperties = itemStack.Collectible.ParticleProperties;
        if (itemStack.Collectible == null || api.IsGamePaused)
        {
            return;
        }

        EntityPlayer entityPlayer = api.World.Player.Entity;

        Vec4f vec4f = itemModelMat.TransformVector(new Vec4f(itemStack.Collectible.TopMiddlePos.X, itemStack.Collectible.TopMiddlePos.Y, itemStack.Collectible.TopMiddlePos.Z, 1f));
        timeAccumulation += dt;
        if (particleProperties != null && particleProperties.Length != 0 && timeAccumulation > 0.05f)
        {
            timeAccumulation %= 0.025f;
            foreach (AdvancedParticleProperties advancedParticleProperties in particleProperties)
            {
                advancedParticleProperties.WindAffectednesAtPos = windStrength;
                advancedParticleProperties.WindAffectednes = windStrength;
                advancedParticleProperties.basePos.X = vec4f.X + entity.Pos.X + (0.0 - (entity.Pos.X - entityPlayer.CameraPos.X));
                advancedParticleProperties.basePos.Y = vec4f.Y + entity.Pos.Y + (0.0 - (entity.Pos.Y - entityPlayer.CameraPos.Y));
                advancedParticleProperties.basePos.Z = vec4f.Z + entity.Pos.Z + (0.0 - (entity.Pos.Z - entityPlayer.CameraPos.Z));
                entity.World.SpawnParticles(advancedParticleProperties);
            }
        }
    }
    private static void ZeroTransformCorrection(List<float> elementTransforms)
    {
        bool zeroTransform = elementTransforms.Count(value => value == 0) == elementTransforms.Count;
        if (zeroTransform)
        {
            for (int i = 0; i < elementTransforms.Count; i += 4)
            {
                if (elementTransforms[i] == 0)
                {
                    elementTransforms[i] = 1;
                }
            }
        }
    }

    private static void FillShaderValues(IShaderProgram shaderProgram, ItemRenderInfo itemStackRenderInfo, IRenderAPI render, ItemStack itemStack, Vec4f lightrgbs, Matrixf itemModelMatrix, IWorldAccessor world, AnimatorBase animator)
    {
        FillShaderValues(shaderProgram, itemStackRenderInfo, render, itemStack, lightrgbs, itemModelMatrix, world);

        List<float> elementTransforms = new();

        for (int index = 0; index < animator.TransformationMatrices.Length; index++)
        {
            if (index % 4 == 3) continue;
            elementTransforms.Add(animator.TransformationMatrices[index]);
        }

        ZeroTransformCorrection(elementTransforms);

        shaderProgram.UniformMatrices4x3(
            "elementTransforms",
            GlobalConstants.MaxAnimatedElements,
            elementTransforms.ToArray()
        );
    }

    private static void FillShaderValues(IShaderProgram shaderProgram, ItemRenderInfo itemStackRenderInfo, IRenderAPI render, ItemStack itemStack, Vec4f lightrgbs, Matrixf itemModelMatrix, IWorldAccessor world)
    {
        shaderProgram.Uniform("dontWarpVertices", 0);
        shaderProgram.Uniform("addRenderFlags", 0);
        shaderProgram.Uniform("normalShaded", 1);
        shaderProgram.Uniform("rgbaTint", ColorUtil.WhiteArgbVec);
        shaderProgram.Uniform("alphaTest", itemStackRenderInfo.AlphaTest);
        shaderProgram.Uniform("damageEffect", itemStackRenderInfo.DamageEffect);
        shaderProgram.Uniform("overlayOpacity", itemStackRenderInfo.OverlayOpacity);
        if (itemStackRenderInfo.OverlayTexture != null && itemStackRenderInfo.OverlayOpacity > 0f)
        {
            shaderProgram.Uniform("tex2dOverlay", itemStackRenderInfo.OverlayTexture.TextureId);
            shaderProgram.Uniform("overlayTextureSize", new Vec2f(itemStackRenderInfo.OverlayTexture.Width, itemStackRenderInfo.OverlayTexture.Height));
            shaderProgram.Uniform("baseTextureSize", new Vec2f(itemStackRenderInfo.TextureSize.Width, itemStackRenderInfo.TextureSize.Height));
            TextureAtlasPosition textureAtlasPosition = render.GetTextureAtlasPosition(itemStack);
            shaderProgram.Uniform("baseUvOrigin", new Vec2f(textureAtlasPosition.x1, textureAtlasPosition.y1));
        }

        int num = (int)itemStack.Collectible.GetTemperature(world, itemStack);
        float[] incandescenceColorAsColor4f = ColorUtil.GetIncandescenceColorAsColor4f(num);
        int num2 = GameMath.Clamp((num - 500) / 3, 0, 255);
        shaderProgram.Uniform("extraGlow", num2);
        shaderProgram.Uniform("rgbaAmbientIn", render.AmbientColor);
        shaderProgram.Uniform("rgbaLightIn", lightrgbs);
        shaderProgram.Uniform("rgbaGlowIn", new Vec4f(incandescenceColorAsColor4f[0], incandescenceColorAsColor4f[1], incandescenceColorAsColor4f[2], num2 / 255f));
        shaderProgram.Uniform("rgbaFogIn", render.FogColor);
        shaderProgram.Uniform("fogMinIn", render.FogMin);
        shaderProgram.Uniform("fogDensityIn", render.FogDensity);
        shaderProgram.Uniform("normalShaded", itemStackRenderInfo.NormalShaded ? 1 : 0);
        shaderProgram.UniformMatrix("projectionMatrix", render.CurrentProjectionMatrix);
        shaderProgram.UniformMatrix("viewMatrix", render.CameraMatrixOriginf);
        shaderProgram.UniformMatrix("modelMatrix", itemModelMatrix.Values);
        shaderProgram.Uniform("depthOffset", PlayerRenderingPatches.FpHandsOffset);
    }
    private static float GetDepthOffset(IWorldAccessor world)
    {
        return (world.Api as ICoreClientAPI)?.Settings.Bool["immersiveFpMode"] ?? false ? 0.0f : -0.3f;
    }
}