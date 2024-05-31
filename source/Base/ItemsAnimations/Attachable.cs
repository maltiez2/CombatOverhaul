using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace CombatOverhaul.ItemsAnimations;

public class AnimatableAttachable : Animatable // Based on code from TeacupAngel (https://github.com/TeacupAngel)
{
    public AnimatableAttachable(CollectibleObject collObj) : base(collObj)
    {
    }

    private readonly Dictionary<long, Dictionary<string, Attachment>> mAttachments = new();
    private readonly Dictionary<long, Dictionary<string, bool>> mActiveAttachments = new();

    public bool SetAttachment(long entityId, string attachmentCode, ItemStack attachmentItem, ModelTransform transform, bool activate = true, bool newAnimatableShape = false)
    {
        if (ClientApi == null) return false;
        if (!mAttachments.ContainsKey(entityId)) mAttachments.Add(entityId, new());
        if (!mActiveAttachments.ContainsKey(entityId)) mActiveAttachments.Add(entityId, new());
        RemoveAttachment(entityId, attachmentCode);
        mAttachments[entityId][attachmentCode] = new(ClientApi, attachmentCode, attachmentItem, transform, newAnimatableShape);
        mActiveAttachments[entityId][attachmentCode] = activate;
        return true;
    }

    public bool ToggleAttachment(long entityId, string attachmentCode, bool toggle)
    {
        if (!mActiveAttachments.ContainsKey(entityId) || !mActiveAttachments[entityId].ContainsKey(attachmentCode)) return false;
        mActiveAttachments[entityId][attachmentCode] = toggle;
        return true;
    }

    public bool RemoveAttachment(long entityId, string attachmentCode)
    {
        if (!mActiveAttachments.ContainsKey(entityId) || !mActiveAttachments[entityId].ContainsKey(attachmentCode)) return false;
        mAttachments[entityId][attachmentCode].Dispose();
        mAttachments[entityId].Remove(attachmentCode);
        mActiveAttachments[entityId].Remove(attachmentCode);
        return true;
    }

    public bool? CheckAttachment(long entityId, string attachmentCode)
    {
        if (!mActiveAttachments.ContainsKey(entityId) || !mActiveAttachments[entityId].ContainsKey(attachmentCode)) return null;
        return mActiveAttachments[entityId][attachmentCode];
    }

    public bool ClearAttachments(long entityId)
    {
        if (!mActiveAttachments.ContainsKey(entityId)) return false;
        mAttachments[entityId].Clear();
        mActiveAttachments[entityId].Clear();
        return true;
    }

    public override void BeforeRender(ICoreClientAPI clientApi, ItemStack itemStack, Entity player, EnumItemRenderTarget target, float dt)
    {
        base.BeforeRender(clientApi, itemStack, player, target, dt);

        foreach (Attachment attachment in mAttachments.SelectMany(entry => entry.Value).Select(entry => entry.Value))
        {
            attachment.BeforeRender(target, player, dt);
        }
    }

    public override void RenderShape(IShaderProgram shaderProgram, IWorldAccessor world, AnimatableShape shape, ItemRenderInfo itemStackRenderInfo, IRenderAPI render, ItemStack itemStack, Vec4f lightrgbs, Matrixf itemModelMat, ItemSlot itemSlot, Entity entity, float dt)
    {
        base.RenderShape(shaderProgram, world, shape, itemStackRenderInfo, render, itemStack, lightrgbs, ItemModelMat, itemSlot, entity, dt);

        if (Shape?.GetAnimator(entity.EntityId) == null) return;
        if (!mActiveAttachments.ContainsKey(entity.EntityId) || !mAttachments.ContainsKey(entity.EntityId)) return;
        if (CurrentAnimatableShape == null) return;

        foreach ((string code, bool active) in mActiveAttachments[entity.EntityId].Where(x => x.Value))
        {
            mAttachments[entity.EntityId][code].Render(CurrentAnimatableShape, shaderProgram, itemStackRenderInfo, render, lightrgbs, itemModelMat, entity, dt);
        }
    }

    public override void OnUnloaded(ICoreAPI api)
    {
        base.OnUnloaded(api);
        foreach (Attachment attachment in mAttachments.SelectMany(entry => entry.Value).Select(entry => entry.Value))
        {
            attachment.Dispose();
        }
    }
}

public interface IAttachment : IDisposable
{
    void Render(AnimatableShape parentShape, IShaderProgram shaderProgram, ItemRenderInfo itemStackRenderInfo, IRenderAPI render, Vec4f lightrgbs, Matrixf itemModelMat, Entity entity, float dt);
}

public sealed class Attachment : IAttachment
{
    private readonly ICoreClientAPI mApi;
    private readonly ModelTransform mAttachedTransform;
    private readonly ItemStack mItemStack;
    private readonly string mAttachmentPointCode;
    private readonly AnimatableShape? mShape;
    private readonly Animatable? mBehavior;
    private readonly bool mDisposeShape;

    private Matrixf mAttachedMeshMatrix = new();
    private bool mDisposed = false;

    public Attachment(ICoreClientAPI api, string attachmentPointCode, ItemStack attachment, ModelTransform transform, bool newAnimatableShape = false)
    {
        mApi = api;
        mItemStack = attachment.Clone();
        mAttachedTransform = transform;
        mAttachmentPointCode = attachmentPointCode;

        if (!newAnimatableShape && attachment.Item.HasBehavior(typeof(Animatable), true) && attachment.Item.GetCollectibleBehavior(typeof(Animatable), true) is Animatable behavior)
        {
            mBehavior = behavior;
            mDisposeShape = false;
        }
        else
        {
            mShape = AnimatableShape.Create(api, attachment.Item.Shape.Base.ToString(), attachment.Item);
            mDisposeShape = true;
        }
    }

    public void Render(AnimatableShape parentShape, IShaderProgram shaderProgram, ItemRenderInfo itemStackRenderInfo, IRenderAPI render, Vec4f lightrgbs, Matrixf itemModelMat, Entity entity, float dt)
    {
        ItemRenderInfo attachedRenderInfo = GetAttachmentRenderInfo(itemStackRenderInfo.dt);
        AttachmentPointAndPose? attachmentPointAndPose = parentShape.GetAnimator(entity.EntityId)?.GetAttachmentPointPose(mAttachmentPointCode);
        if (attachmentPointAndPose == null)
        {
            mApi.Logger.VerboseDebug($"[Animation Manager lib] [Attachment] [Render()] Attachment point '{mAttachmentPointCode}' not found");
            return;
        }
        AttachmentPoint attachmentPoint = attachmentPointAndPose.AttachPoint;
        CalculateMeshMatrix(itemModelMat, itemStackRenderInfo, attachedRenderInfo, attachmentPointAndPose, attachmentPoint);

        GetShape()?.Render(shaderProgram, attachedRenderInfo, render, mItemStack, lightrgbs, mAttachedMeshMatrix, entity, dt);
    }

    public void BeforeRender(EnumItemRenderTarget target, Entity entity, float dt)
    {
        mBehavior?.BeforeRender(mApi, mItemStack, entity, target, dt);
    }

    private AnimatableShape? GetShape() => mShape ?? mBehavior?.CurrentAnimatableShape;

    private void CalculateMeshMatrix(Matrixf modelMat, ItemRenderInfo renderInfo, ItemRenderInfo attachedRenderInfo, AttachmentPointAndPose apap, AttachmentPoint ap)
    {
        mAttachedMeshMatrix = modelMat.Clone()
            .Translate(-renderInfo.Transform.Origin.X, -renderInfo.Transform.Origin.Y, -renderInfo.Transform.Origin.Z)
            .Mul(apap.AnimModelMatrix)
            .Translate((ap.PosX + attachedRenderInfo.Transform.Translation.X) / 16f, (ap.PosY + attachedRenderInfo.Transform.Translation.Y) / 16f, (ap.PosZ + attachedRenderInfo.Transform.Translation.Z) / 16f)
            .Translate(renderInfo.Transform.Origin.X, renderInfo.Transform.Origin.Y, renderInfo.Transform.Origin.Z)
            .RotateX((float)(ap.RotationX) * GameMath.DEG2RAD)
            .RotateY((float)(ap.RotationY) * GameMath.DEG2RAD)
            .RotateZ((float)(ap.RotationZ) * GameMath.DEG2RAD)
            .Scale(attachedRenderInfo.Transform.ScaleXYZ.X, attachedRenderInfo.Transform.ScaleXYZ.Y, attachedRenderInfo.Transform.ScaleXYZ.Z)
            .Translate(-attachedRenderInfo.Transform.Origin.X / 16f, -attachedRenderInfo.Transform.Origin.Y / 16f, -attachedRenderInfo.Transform.Origin.Z / 16f)
            .Translate(-renderInfo.Transform.Origin.X, -renderInfo.Transform.Origin.Y, -renderInfo.Transform.Origin.Z);
    }

    private ItemRenderInfo GetAttachmentRenderInfo(float dt)
    {
        DummySlot dummySlot = new(mItemStack);
        ItemRenderInfo renderInfo = mApi.Render.GetItemStackRenderInfo(dummySlot, EnumItemRenderTarget.Ground, dt);
        renderInfo.Transform = mAttachedTransform;
        return renderInfo;
    }

    public void Dispose()
    {
        if (mDisposed) return;
        mDisposed = true;

        if (mDisposeShape) mShape?.Dispose();
    }
}
