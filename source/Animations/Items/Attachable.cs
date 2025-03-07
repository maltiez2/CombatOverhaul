using CombatOverhaul.Utils;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace CombatOverhaul.Animations;

public class AnimatableAttachable : Animatable
{
    public AnimatableAttachable(CollectibleObject collObj) : base(collObj)
    {
    }

    public bool SetAttachment(long entityId, string attachmentCode, ItemStack attachmentItem, ModelTransform transform, bool activate = true, bool newAnimatableShape = false)
    {
        if (ClientApi == null) return false;
        if (!Attachments.ContainsKey(entityId)) Attachments.Add(entityId, new());
        if (!ActiveAttachments.ContainsKey(entityId)) ActiveAttachments.Add(entityId, new());
        RemoveAttachment(entityId, attachmentCode);
        Attachments[entityId][attachmentCode] = new(ClientApi, attachmentCode, attachmentItem, transform, newAnimatableShape);
        ActiveAttachments[entityId][attachmentCode] = activate;
        return true;
    }
    public bool ToggleAttachment(long entityId, string attachmentCode, bool toggle)
    {
        if (!ActiveAttachments.ContainsKey(entityId) || !ActiveAttachments[entityId].ContainsKey(attachmentCode)) return false;
        ActiveAttachments[entityId][attachmentCode] = toggle;
        return true;
    }
    public bool RemoveAttachment(long entityId, string attachmentCode)
    {
        if (!ActiveAttachments.ContainsKey(entityId) || !ActiveAttachments[entityId].ContainsKey(attachmentCode)) return false;
        Attachments[entityId][attachmentCode].Dispose();
        Attachments[entityId].Remove(attachmentCode);
        ActiveAttachments[entityId].Remove(attachmentCode);
        return true;
    }
    public bool? CheckAttachment(long entityId, string attachmentCode)
    {
        if (!ActiveAttachments.ContainsKey(entityId) || !ActiveAttachments[entityId].ContainsKey(attachmentCode)) return null;
        return ActiveAttachments[entityId][attachmentCode];
    }
    public bool ClearAttachments(long entityId)
    {
        if (!ActiveAttachments.ContainsKey(entityId)) return false;
        foreach ((_, Attachment attachment) in Attachments[entityId])
        {
            attachment.Dispose();
        }
        Attachments[entityId].Clear();
        ActiveAttachments[entityId].Clear();
        return true;
    }
    public void SetSwitchModels(long entityId, bool switchModels) => SwitchModelsPerEntity[entityId] = switchModels;

    public override void BeforeRender(ICoreClientAPI clientApi, ItemStack itemStack, Entity player, EnumItemRenderTarget target, float dt)
    {
        SwitchModelsPerEntity.TryGetValue(player.EntityId, out bool switchModels);
        SwitchModels = switchModels;

        base.BeforeRender(clientApi, itemStack, player, target, dt);

        foreach (Attachment attachment in Attachments.SelectMany(entry => entry.Value).Select(entry => entry.Value))
        {
            attachment.BeforeRender(target, player, dt);
        }
    }
    public override void OnUnloaded(ICoreAPI api)
    {
        base.OnUnloaded(api);
        foreach (Attachment attachment in Attachments.SelectMany(entry => entry.Value).Select(entry => entry.Value))
        {
            attachment.Dispose();
        }
    }


    protected readonly Dictionary<long, Dictionary<string, Attachment>> Attachments = new();
    protected readonly Dictionary<long, Dictionary<string, bool>> ActiveAttachments = new();
    protected readonly Dictionary<long, bool> SwitchModelsPerEntity = new();

    protected override void RenderShape(IShaderProgram shaderProgram, IWorldAccessor world, AnimatableShape shape, ItemRenderInfo itemStackRenderInfo, IRenderAPI render, ItemStack itemStack, Vec4f lightrgbs, Matrixf itemModelMat, ItemSlot itemSlot, Entity entity, float dt)
    {
        SwitchModelsPerEntity.TryGetValue(entity.EntityId, out bool switchModels);
        SwitchModels = switchModels;

        base.RenderShape(shaderProgram, world, shape, itemStackRenderInfo, render, itemStack, lightrgbs, ItemModelMat, itemSlot, entity, dt);

        if (Shape?.GetAnimator(entity.EntityId) == null) return;
        if (!ActiveAttachments.ContainsKey(entity.EntityId) || !Attachments.ContainsKey(entity.EntityId)) return;
        if (GetCurrentShape(itemStack) == null) return;

        foreach ((string code, bool active) in ActiveAttachments[entity.EntityId].Where(x => x.Value))
        {
            Attachments[entity.EntityId][code].Render(GetCurrentShape(itemStack), shaderProgram, itemStackRenderInfo, render, lightrgbs, itemModelMat, entity, dt);
        }
    }
}

public sealed class Attachment : IDisposable
{
    public Attachment(ICoreClientAPI api, string attachmentPointCode, ItemStack attachment, ModelTransform transform, bool newAnimatableShape = false)
    {
        _api = api;
        _itemStack = attachment.Clone();
        _attachedTransform = transform;
        _attachmentPointCode = attachmentPointCode;

        if (attachment.Attributes.HasAttribute("attachableShape"))
        {
            _shape = AnimatableShape.Create(api, attachment.Attributes.GetAsString("attachableShape"), attachment.Item);
            _disposeShape = true;
        }
        else if (attachment.Item?.Attributes?.KeyExists("attachableShape") == true)
        {
            string shapePath = attachment.Item.Attributes["attachableShape"].AsString();

            if (!_api.Assets.Exists(new(shapePath + ".json")))
            {
                LoggerUtil.Warn(_api, this, $"Shape was not found: {shapePath}");
                return;
            }

            _shape = AnimatableShape.Create(api, attachment.Item.Attributes["attachableShape"].AsString(), attachment.Item);
            _disposeShape = true;
        }
        else if (!newAnimatableShape && attachment.Item.HasBehavior(typeof(Animatable), true) && attachment.Item.GetCollectibleBehavior(typeof(Animatable), true) is Animatable behavior)
        {
            _behavior = behavior;
            _disposeShape = false;
        }
        else
        {
            _shape = AnimatableShape.Create(api, attachment.Item.Shape.Base.ToString(), attachment.Item);
            _disposeShape = true;
        }
    }

    public void Render(AnimatableShape parentShape, IShaderProgram shaderProgram, ItemRenderInfo itemStackRenderInfo, IRenderAPI render, Vec4f lightrgbs, Matrixf itemModelMat, Entity entity, float dt)
    {
        ItemRenderInfo attachedRenderInfo = GetAttachmentRenderInfo(itemStackRenderInfo.dt);
        AttachmentPointAndPose? attachmentPointAndPose = parentShape.GetAnimator(entity.EntityId)?.GetAttachmentPointPose(_attachmentPointCode);
        if (attachmentPointAndPose == null)
        {
            LoggerUtil.Warn(_api, this, $"Attachment point '{_attachmentPointCode}' not found");
            return;
        }
        AttachmentPoint attachmentPoint = attachmentPointAndPose.AttachPoint;
        CalculateMeshMatrix(itemModelMat, itemStackRenderInfo, attachedRenderInfo, attachmentPointAndPose, attachmentPoint);

        GetShape()?.Render(shaderProgram, attachedRenderInfo, render, _itemStack, lightrgbs, _attachedMeshMatrix, entity, dt);
    }
    public void BeforeRender(EnumItemRenderTarget target, Entity entity, float dt)
    {
        _behavior?.BeforeRender(_api, _itemStack, entity, target, dt);
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_disposeShape) _shape?.Dispose();
    }


    private readonly ICoreClientAPI _api;
    private readonly ModelTransform _attachedTransform;
    private readonly ItemStack _itemStack;
    private readonly string _attachmentPointCode;
    private readonly AnimatableShape? _shape;
    private readonly Animatable? _behavior;
    private readonly bool _disposeShape;

    private Matrixf _attachedMeshMatrix = new();
    private bool _disposed = false;

    private AnimatableShape? GetShape() => _shape ?? _behavior?.CurrentAnimatableShape;
    private void CalculateMeshMatrix(Matrixf modelMat, ItemRenderInfo renderInfo, ItemRenderInfo attachedRenderInfo, AttachmentPointAndPose apap, AttachmentPoint ap)
    {
        _attachedMeshMatrix = modelMat.Clone()
            .Translate(-renderInfo.Transform.Origin.X, -renderInfo.Transform.Origin.Y, -renderInfo.Transform.Origin.Z)
            .Mul(apap.AnimModelMatrix)
            .Translate((ap.PosX + attachedRenderInfo.Transform.Translation.X) / 16f, (ap.PosY + attachedRenderInfo.Transform.Translation.Y) / 16f, (ap.PosZ + attachedRenderInfo.Transform.Translation.Z) / 16f)
            .Translate(renderInfo.Transform.Origin.X, renderInfo.Transform.Origin.Y, renderInfo.Transform.Origin.Z)
            .RotateX((float)(ap.RotationX + attachedRenderInfo.Transform.Rotation.X) * GameMath.DEG2RAD)
            .RotateY((float)(ap.RotationY + attachedRenderInfo.Transform.Rotation.Y) * GameMath.DEG2RAD)
            .RotateZ((float)(ap.RotationZ + attachedRenderInfo.Transform.Rotation.Z) * GameMath.DEG2RAD)
            .Scale(attachedRenderInfo.Transform.ScaleXYZ.X, attachedRenderInfo.Transform.ScaleXYZ.Y, attachedRenderInfo.Transform.ScaleXYZ.Z)
            .Translate(-attachedRenderInfo.Transform.Origin.X / 16f, -attachedRenderInfo.Transform.Origin.Y / 16f, -attachedRenderInfo.Transform.Origin.Z / 16f)
            .Translate(-renderInfo.Transform.Origin.X, -renderInfo.Transform.Origin.Y, -renderInfo.Transform.Origin.Z);
    }
    private ItemRenderInfo GetAttachmentRenderInfo(float dt)
    {
        DummySlot dummySlot = new(_itemStack);
        ItemRenderInfo renderInfo = _api.Render.GetItemStackRenderInfo(dummySlot, EnumItemRenderTarget.Ground, dt);
        renderInfo.Transform = _attachedTransform;
        return renderInfo;
    }
}
