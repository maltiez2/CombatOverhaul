using System.Numerics;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace CombatOverhaul.Colliders;

internal static class ColliderTools
{
    public static Vector3 TransformVector(Vector3 value, Matrixf modelMatrix, EntityPos playerPos)
    {
        _inputBuffer.X = value.X;
        _inputBuffer.Y = value.Y;
        _inputBuffer.Z = value.Z;

        Mat4f.MulWithVec4(modelMatrix.Values, _inputBuffer, _outputBuffer);

        _outputBuffer.X += (float)playerPos.X;
        _outputBuffer.Y += (float)playerPos.Y;
        _outputBuffer.Z += (float)playerPos.Z;

        return new(_outputBuffer.X, _outputBuffer.Y, _outputBuffer.Z);
    }
    public static Matrixf? GetHeldItemModelMatrix(EntityAgent entity, ItemSlot itemSlot, ICoreClientAPI api, bool right = true)
    {
        if (entity.Properties.Client.Renderer is not EntityShapeRenderer entityShapeRenderer) return null;

        ItemStack? itemStack = itemSlot?.Itemstack;
        if (itemStack == null) return null;

        AttachmentPointAndPose? attachmentPointAndPose = entity.AnimManager?.Animator?.GetAttachmentPointPose(right ? "RightHand" : "LeftHand");
        if (attachmentPointAndPose == null) return null;

        AttachmentPoint attachPoint = attachmentPointAndPose.AttachPoint;
        ItemRenderInfo itemStackRenderInfo = api.Render.GetItemStackRenderInfo(itemSlot, right ? EnumItemRenderTarget.HandTp : EnumItemRenderTarget.HandTpOff, 0f);
        if (itemStackRenderInfo?.Transform == null) return null;

        return _matrixBuffer.Set(entityShapeRenderer.ModelMat).Mul(attachmentPointAndPose.AnimModelMatrix).Translate(itemStackRenderInfo.Transform.Origin.X, itemStackRenderInfo.Transform.Origin.Y, itemStackRenderInfo.Transform.Origin.Z)
            .Scale(itemStackRenderInfo.Transform.ScaleXYZ.X, itemStackRenderInfo.Transform.ScaleXYZ.Y, itemStackRenderInfo.Transform.ScaleXYZ.Z)
            .Translate(attachPoint.PosX / 16.0 + itemStackRenderInfo.Transform.Translation.X, attachPoint.PosY / 16.0 + itemStackRenderInfo.Transform.Translation.Y, attachPoint.PosZ / 16.0 + itemStackRenderInfo.Transform.Translation.Z)
            .RotateX((float)(attachPoint.RotationX + itemStackRenderInfo.Transform.Rotation.X) * (MathF.PI / 180f))
            .RotateY((float)(attachPoint.RotationY + itemStackRenderInfo.Transform.Rotation.Y) * (MathF.PI / 180f))
            .RotateZ((float)(attachPoint.RotationZ + itemStackRenderInfo.Transform.Rotation.Z) * (MathF.PI / 180f))
            .Translate(0f - itemStackRenderInfo.Transform.Origin.X, 0f - itemStackRenderInfo.Transform.Origin.Y, 0f - itemStackRenderInfo.Transform.Origin.Z);
    }
    public static bool Transform(IEnumerable<IHasCollider> colliders, EntityAgent entity, ItemSlot itemSlot, ICoreClientAPI api, bool right = true)
    {
        EntityPos playerPos = entity.Pos;
        Matrixf? modelMatrix = GetHeldItemModelMatrix(entity, itemSlot, api, right);
        if (modelMatrix is null) return false;

        foreach (IHasCollider collider in colliders)
        {
            collider.InWorldCollider = collider.RelativeCollider.Transform(modelMatrix, playerPos);
        }

        return true;
    }

    private static readonly Vec4f _inputBuffer = new(0, 0, 0, 1);
    private static readonly Vec4f _outputBuffer = new(0, 0, 0, 1);
    private static readonly Matrixf _matrixBuffer = new();
}

public sealed class LineSegmentColliderJson
{
    public float[] Tail { get; set; } = Array.Empty<float>();
    public float[] Head { get; set; } = Array.Empty<float>();

    public LineSegmentCollider Get() => new(new Vector3(Tail[0], Tail[1], Tail[2]), new Vector3(Head[0] - Tail[0], Head[1] - Tail[1], Head[2] - Tail[2]));
}

public sealed class RectangularColliderJson
{
    public float[] A { get; set; } = Array.Empty<float>();
    public float[] B { get; set; } = Array.Empty<float>();
    public float[] C { get; set; } = Array.Empty<float>();
    public float[] D { get; set; } = Array.Empty<float>();

    public RectangularCollider Get() => new(
        new Vector3(A[0], A[1], A[2]),
        new Vector3(B[0], B[1], B[2]),
        new Vector3(C[0], C[1], C[2]),
        new Vector3(D[0], D[1], D[2])
        );
}

public sealed class OctagonalColliderJson
{
    public float[] A { get; set; } = Array.Empty<float>();
    public float[] B { get; set; } = Array.Empty<float>();
    public float[] C { get; set; } = Array.Empty<float>();
    public float[] D { get; set; } = Array.Empty<float>();
    public float[] E { get; set; } = Array.Empty<float>();
    public float[] F { get; set; } = Array.Empty<float>();
    public float[] G { get; set; } = Array.Empty<float>();
    public float[] H { get; set; } = Array.Empty<float>();

    public OctagonalCollider Get() => new(
        new Vector3(A[0], A[1], A[2]),
        new Vector3(B[0], B[1], B[2]),
        new Vector3(C[0], C[1], C[2]),
        new Vector3(D[0], D[1], D[2]),
        new Vector3(E[0], E[1], E[2]),
        new Vector3(F[0], F[1], F[2]),
        new Vector3(G[0], G[1], G[2]),
        new Vector3(H[0], H[1], H[2])
        );
}
