using CombatOverhaul.Integration;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace CombatOverhaul.Animations;

public class Animatable : CollectibleBehavior
{
    public Shape? CurrentShape => CurrentAnimatableShape?.Shape;
    public AnimatableShape? CurrentAnimatableShape => (CurrentFirstPerson ? ShapeFirstPerson : Shape) ?? Shape ?? ShapeFirstPerson;
    public Shape? FirstPersonShape => ShapeFirstPerson?.Shape;
    public Shape? ThirdPersonShape => Shape?.Shape;
    public bool DetachedAnchor { get; set; } = false;
    public bool SwitchArms { get; set; } = false;

    public Animatable(CollectibleObject collObj) : base(collObj)
    {
    }

    public override void Initialize(JsonObject properties)
    {
        AnimatedShapePath = properties["animated-shape"].AsString(null);
        AnimatedShapeFirstPersonPath = properties["animated-shape-fp"].AsString(null);

        base.Initialize(properties);
    }
    public override void OnLoaded(ICoreAPI api)
    {
        ModSystem = api.ModLoader.GetModSystem<CombatOverhaulAnimationsSystem>();

        if (api is ICoreClientAPI clientApi)
        {
            if (collObj is not Item)
            {
                throw new InvalidOperationException("CollectibleBehavior Animatable can only be used on Items, not Blocks!");
            }

            ClientApi = clientApi;

            InitAnimatable();
        }
    }

    public virtual void BeforeRender(ICoreClientAPI clientApi, ItemStack itemStack, Entity player, EnumItemRenderTarget target, float dt)
    {
        CurrentFirstPerson = IsFirstPerson(player);

        if (CurrentAnimatableShape != null) CalculateAnimation(CurrentAnimatableShape.GetAnimator(player.EntityId), CurrentAnimatableShape.Shape, clientApi, player, target, dt);
    }
    public bool RenderHeldItem(float[] modelMat, ICoreClientAPI api, ItemSlot itemSlot, Entity entity, Vec4f lightrgbs, float dt, bool isShadowPass, bool right, ItemRenderInfo renderInfo)
    {
        if (CurrentAnimatableShape == null || itemSlot.Itemstack == null || ModSystem?.AnimatedItemShaderProgram == null) return false;

        ItemRenderInfo? itemStackRenderInfo = PrepareShape(api, ItemModelMat, modelMat, itemSlot, entity, right, dt);

        if (itemStackRenderInfo == null) return false;

        if (!AnimationsManager.PlayAnimationsInThirdPerson && (!IsOwner(entity) || !IsFirstPerson(entity)))
        {
            //ClientApi?.Render.RenderMultiTextureMesh(renderInfo.ModelRef);
            return false;
        }

        if (isShadowPass)
        {
            //ShadowPass(api, itemStackRenderInfo, ItemModelMat, CurrentAnimatableShape); // Vanilla shadows are bugged and item casts shadows on itself (from tp onto fp)
        }
        else
        {
            IShaderProgram? shader = CurrentFirstPerson ? ModSystem.AnimatedItemShaderProgramFirstPerson : ModSystem.AnimatedItemShaderProgram;

            if (shader == null)
            {
                return false;
            }

            RenderShape(shader, api.World, CurrentAnimatableShape, itemStackRenderInfo, api.Render, itemSlot.Itemstack, lightrgbs, ItemModelMat, itemSlot, entity, dt);
        }

        return true;
    }


    protected CombatOverhaulAnimationsSystem? ModSystem;
    protected Dictionary<string, AnimationMetaData> ActiveAnimationsByCode = new();
    protected ICoreClientAPI? ClientApi;
    protected string? AnimatedShapePath;
    protected string? AnimatedShapeFirstPersonPath;
    protected AnimatableShape? Shape;
    protected AnimatableShape? ShapeFirstPerson;
    protected Matrixf ItemModelMat = new();
    protected float TimeAccumulation = 0;
    protected bool CurrentFirstPerson = false;

    protected virtual void InitAnimatable()
    {
        Item? item = (collObj as Item);

        if (item == null || ClientApi == null || (item.Shape == null && AnimatedShapePath == null && AnimatedShapeFirstPersonPath == null)) return;

        Shape = AnimatableShape.Create(ClientApi, AnimatedShapePath ?? AnimatedShapeFirstPersonPath ?? item.Shape?.Base.ToString() ?? "", item);
        ShapeFirstPerson = AnimatableShape.Create(ClientApi, AnimatedShapeFirstPersonPath ?? AnimatedShapePath ?? item.Shape?.Base.ToString() ?? "", item);
    }
    protected virtual void RenderShape(IShaderProgram shaderProgram, IWorldAccessor world, AnimatableShape shape, ItemRenderInfo itemStackRenderInfo, IRenderAPI render, ItemStack itemStack, Vec4f lightrgbs, Matrixf itemModelMat, ItemSlot itemSlot, Entity entity, float dt)
    {
        CurrentAnimatableShape?.Render(shaderProgram, itemStackRenderInfo, render, itemStack, lightrgbs, itemModelMat, entity, dt);
    }
    protected virtual void CalculateAnimation(AnimatorBase? animator, Shape shape, ICoreClientAPI clientApi, Entity entity, EnumItemRenderTarget target, float dt)
    {
        if (
            animator != null &&
            !clientApi.IsGamePaused &&
            target == EnumItemRenderTarget.HandFp
        )
        {
            if (animator is ClientAnimator clientAnimator && entity is EntityAgent agent)
                AnimationPatch._animators[clientAnimator] = agent;

            animator.OnFrame(ActiveAnimationsByCode, dt);
        }
    }
    protected static bool IsFirstPerson(Entity entity)
    {
        bool owner = (entity.Api as ICoreClientAPI)?.World.Player.Entity.EntityId == entity.EntityId;
        if (!owner) return false;

        bool firstPerson = entity.Api is ICoreClientAPI { World.Player.CameraMode: EnumCameraMode.FirstPerson };

        return firstPerson;
    }
    protected ItemRenderInfo? PrepareShape(ICoreClientAPI api, Matrixf itemModelMat, float[] modelMat, ItemSlot itemSlot, Entity entity, bool right, float dt)
    {
        ItemStack? itemStack = itemSlot?.Itemstack;
        if (itemStack == null)
        {
            return null;
        }

        string attachmentPoint = GetAttachmentPointName(right, entity);

        AttachmentPointAndPose? attachmentPointAndPose = entity.AnimManager?.Animator?.GetAttachmentPointPose(attachmentPoint);
        if (attachmentPointAndPose == null)
        {
            return null;
        }

        AttachmentPoint attachPoint = attachmentPointAndPose.AttachPoint;
        ItemRenderInfo itemStackRenderInfo = api.Render.GetItemStackRenderInfo(itemSlot, right ? EnumItemRenderTarget.HandTp : EnumItemRenderTarget.HandTpOff, dt);
        if (itemStackRenderInfo?.Transform == null)
        {
            return null;
        }

        itemModelMat.Set(modelMat).Mul(attachmentPointAndPose.AnimModelMatrix).Translate(itemStackRenderInfo.Transform.Origin.X, itemStackRenderInfo.Transform.Origin.Y, itemStackRenderInfo.Transform.Origin.Z)
            .Scale(itemStackRenderInfo.Transform.ScaleXYZ.X, itemStackRenderInfo.Transform.ScaleXYZ.Y, itemStackRenderInfo.Transform.ScaleXYZ.Z)
            .Translate(attachPoint.PosX / 16.0 + itemStackRenderInfo.Transform.Translation.X, attachPoint.PosY / 16.0 + itemStackRenderInfo.Transform.Translation.Y, attachPoint.PosZ / 16.0 + itemStackRenderInfo.Transform.Translation.Z)
            .RotateX((float)(attachPoint.RotationX + itemStackRenderInfo.Transform.Rotation.X) * (MathF.PI / 180f))
            .RotateY((float)(attachPoint.RotationY + itemStackRenderInfo.Transform.Rotation.Y) * (MathF.PI / 180f))
            .RotateZ((float)(attachPoint.RotationZ + itemStackRenderInfo.Transform.Rotation.Z) * (MathF.PI / 180f))
            .Translate(0f - itemStackRenderInfo.Transform.Origin.X, 0f - itemStackRenderInfo.Transform.Origin.Y, 0f - itemStackRenderInfo.Transform.Origin.Z);

        return itemStackRenderInfo;
    }
    protected string GetAttachmentPointName(bool right, Entity entity)
    {
        if (DetachedAnchor)
        {
            DetachedAnchor = false;
            return "DetachedAnchor";
        }

        if (!SwitchArms)
        {
            return right ? "RightHand" : "LeftHand";
        }
        else
        {
            SwitchArms = false;
            return right ? "LeftHand" : "RightHand";
        }
    }
    protected static void ShadowPass(ICoreClientAPI api, ItemRenderInfo itemStackRenderInfo, Matrixf itemModelMat, AnimatableShape shape)
    {
        IRenderAPI render = api.Render;

        string textureSampleName = "tex2d";
        render.CurrentActiveShader.BindTexture2D("tex2d", itemStackRenderInfo.TextureId, 0);
        float[] array = Mat4f.Mul(itemModelMat.Values, api.Render.CurrentModelviewMatrix, itemModelMat.Values);
        Mat4f.Mul(array, api.Render.CurrentProjectionMatrix, array);
        api.Render.CurrentActiveShader.UniformMatrix("mvpMatrix", array);
        api.Render.CurrentActiveShader.Uniform("origin", new Vec3f());

        if (!itemStackRenderInfo.CullFaces)
        {
            render.GlDisableCullFace();
        }

        render.RenderMultiTextureMesh(shape.MeshRef, textureSampleName);
        if (!itemStackRenderInfo.CullFaces)
        {
            render.GlEnableCullFace();
        }
    }
    protected static bool IsOwner(Entity entity) => (entity.Api as ICoreClientAPI)?.World.Player.Entity.EntityId == entity.EntityId;
}
