using System.Numerics;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace CombatOverhaul.Colliders;

public readonly struct LineSegmentCollider : IWeaponCollider
{
    public readonly Vector3 Position;
    public readonly Vector3 Direction;

    public LineSegmentCollider(Vector3 position, Vector3 direction)
    {
        Position = position;
        Direction = direction;
    }
    public LineSegmentCollider(JsonObject json)
    {
        Position = new(json["X1"].AsFloat(0), json["Y1"].AsFloat(0), json["Z1"].AsFloat(0));
        Direction = new(json["X2"].AsFloat(0), json["Y2"].AsFloat(0), json["Z2"].AsFloat(0));
        Direction -= Position;
    }
    public LineSegmentCollider(params float[] positions)
    {
        Position = new(positions[0], positions[1], positions[2]);
        Direction = new(positions[3], positions[4], positions[5]);
        Direction -= Position;
    }

    public void Render(ICoreClientAPI api, EntityAgent entityPlayer, int color = ColorUtil.WhiteArgb)
    {
        BlockPos playerPos = entityPlayer.Pos.AsBlockPos;
        Vector3 playerPosVector = new(playerPos.X, playerPos.Y, playerPos.Z);

        Vector3 tail = Position - playerPosVector;
        Vector3 head = Position + Direction - playerPosVector;

        api.Render.RenderLine(playerPos, tail.X, tail.Y, tail.Z, head.X, head.Y, head.Z, color);
    }
    public ICollider? Transform(EntityPos origin, EntityAgent entity, ItemSlot itemSlot, ICoreClientAPI api, bool right = true)
    {
        Matrixf? modelMatrix = ColliderTools.GetHeldItemModelMatrix(entity, itemSlot, api, right);
        if (modelMatrix is null) return null;

        return TransformSegment(this, modelMatrix, origin);
    }
    public ICollider Transform(Matrixf modelMatrix, EntityPos origin)
    {
        Vector3 tail = ColliderTools.TransformVector(Position, modelMatrix, origin);
        Vector3 head = ColliderTools.TransformVector(Direction + Position, modelMatrix, origin);

        return new LineSegmentCollider(tail, head - tail);
    }

    public bool RoughIntersect(Cuboidf collisionBox)
    {
        if (collisionBox.MaxX < Position.X && collisionBox.MaxX < (Position.X + Direction.X)) return false;
        if (collisionBox.MinX > Position.X && collisionBox.MinX > (Position.X + Direction.X)) return false;

        if (collisionBox.MaxY < Position.Y && collisionBox.MaxY < (Position.Y + Direction.Y)) return false;
        if (collisionBox.MinY > Position.Y && collisionBox.MinY > (Position.Y + Direction.Y)) return false;

        if (collisionBox.MaxZ < Position.Z && collisionBox.MaxZ < (Position.Z + Direction.Z)) return false;
        if (collisionBox.MinZ > Position.Z && collisionBox.MinZ > (Position.Z + Direction.Z)) return false;

        return true;
    }
    public Vector3? IntersectCuboids(IEnumerable<Cuboidf> collisionBoxes)
    {
        float tMin = 0.0f;
        float tMax = 1.0f;

        foreach (Cuboidf collisionBox in collisionBoxes)
        {
            if (!CheckAxisIntersection(Direction.X, Position.X, collisionBox.MinX, collisionBox.MaxX, ref tMin, ref tMax)) continue;
            if (!CheckAxisIntersection(Direction.Y, Position.Y, collisionBox.MinY, collisionBox.MaxY, ref tMin, ref tMax)) continue;
            if (!CheckAxisIntersection(Direction.Z, Position.Z, collisionBox.MinZ, collisionBox.MaxZ, ref tMin, ref tMax)) continue;
        }

        return Position + tMin * Direction;
    }
    public Vector3? IntersectCuboid(Cuboidf collisionBox)
    {
        float tMin = 0.0f;
        float tMax = 1.0f;

        if (!CheckAxisIntersection(Direction.X, Position.X, collisionBox.MinX, collisionBox.MaxX, ref tMin, ref tMax)) return null;
        if (!CheckAxisIntersection(Direction.Y, Position.Y, collisionBox.MinY, collisionBox.MaxY, ref tMin, ref tMax)) return null;
        if (!CheckAxisIntersection(Direction.Z, Position.Z, collisionBox.MinZ, collisionBox.MaxZ, ref tMin, ref tMax)) return null;

        return Position + tMin * Direction;
    }
    public (Block, Vector3)? IntersectTerrain(ICoreClientAPI api)
    {
        int minX = (int)MathF.Min(Position.X, Position.X + Direction.X);
        int minY = (int)MathF.Min(Position.Y, Position.Y + Direction.Y);
        int minZ = (int)MathF.Min(Position.Z, Position.Z + Direction.Z);

        int maxX = (int)MathF.Max(Position.X, Position.X + Direction.X);
        int maxY = (int)MathF.Max(Position.Y, Position.Y + Direction.Y);
        int maxZ = (int)MathF.Max(Position.Z, Position.Z + Direction.Z);

        (Block block, Vector3 position, float parameter)? closestIntersection = null;

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                for (int z = minZ; z <= maxZ; z++)
                {
                    (Block, Vector3, float parameter)? intersection = IntersectBlock(api.World.BlockAccessor, x, y, z);
                    closestIntersection ??= intersection;
                    if (closestIntersection != null && intersection != null && closestIntersection.Value.parameter > intersection.Value.parameter)
                    {
                        closestIntersection = intersection;
                    }
                }
            }
        }

        return closestIntersection != null ? (closestIntersection.Value.block, closestIntersection.Value.position) : null;
    }

    public static IEnumerable<LineSegmentCollider> Transform(IEnumerable<LineSegmentCollider> segments, EntityAgent entity, ItemSlot itemSlot, ICoreClientAPI api, bool right = true)
    {
        EntityPos playerPos = entity.Pos;
        Matrixf? modelMatrix = ColliderTools.GetHeldItemModelMatrix(entity, itemSlot, api, right);
        if (modelMatrix is null) return Array.Empty<LineSegmentCollider>();

        return segments.Select(segment => TransformSegment(segment, modelMatrix, playerPos));
    }
    public static bool Transform(IEnumerable<IHasLineCollider> segments, EntityAgent entity, ItemSlot itemSlot, ICoreClientAPI api, bool right = true)
    {
        EntityPos playerPos = entity.Pos;
        Matrixf? modelMatrix = ColliderTools.GetHeldItemModelMatrix(entity, itemSlot, api, right);
        if (modelMatrix is null) return false;

        foreach (IHasLineCollider damageType in segments)
        {
            damageType.InWorldCollider = TransformSegment(damageType.RelativeCollider, modelMatrix, playerPos);
        }

        return true;
    }

    private static readonly BlockPos _blockPosBuffer = new(0, 0, 0, 0);
    private static readonly Vec3d _blockPosVecBuffer = new();
    private const float _epsilon = 1e-6f;

    private static LineSegmentCollider TransformSegment(LineSegmentCollider value, Matrixf modelMatrix, EntityPos playerPos)
    {
        Vector3 tail = ColliderTools.TransformVector(value.Position, modelMatrix, playerPos);
        Vector3 head = ColliderTools.TransformVector(value.Direction + value.Position, modelMatrix, playerPos);

        return new(tail, head - tail);
    }
    private static bool CheckAxisIntersection(float dirComponent, float startComponent, float minComponent, float maxComponent, ref float tMin, ref float tMax)
    {
        if (MathF.Abs(dirComponent) < _epsilon)
        {
            // Ray is parallel to the slab, check if it's within the slab's extent
            if (startComponent < minComponent || startComponent > maxComponent) return false;
        }
        else
        {
            // Calculate intersection distances to the slab
            float t1 = (minComponent - startComponent) / dirComponent;
            float t2 = (maxComponent - startComponent) / dirComponent;

            // Swap t1 and t2 if needed so that t1 is the intersection with the near plane
            if (t1 > t2)
            {
                (t2, t1) = (t1, t2);
            }

            // Update the minimum intersection distance
            tMin = MathF.Max(tMin, t1);
            // Update the maximum intersection distance
            tMax = MathF.Min(tMax, t2);

            // Early exit if intersection is not possible
            if (tMin > tMax) return false;
        }

        return true;
    }
    private (float, Vector3?) IntersectBlockCollisionBox(Cuboidf collisionBox, int x, int y, int z)
    {
        float tMin = 0.0f;
        float tMax = 1.0f;

        if (!CheckAxisIntersection(Direction.X, Position.X, collisionBox.MinX + x, collisionBox.MaxX + x, ref tMin, ref tMax)) return (0, null);
        if (!CheckAxisIntersection(Direction.Y, Position.Y, collisionBox.MinY + y, collisionBox.MaxY + y, ref tMin, ref tMax)) return (0, null);
        if (!CheckAxisIntersection(Direction.Z, Position.Z, collisionBox.MinZ + z, collisionBox.MaxZ + z, ref tMin, ref tMax)) return (0, null);

        return (tMin, Position + tMin * Direction);
    }
    private (Block, Vector3, float)? IntersectBlock(IBlockAccessor blockAccessor, int x, int y, int z)
    {
        BlockPos position = new(x, y, z, 0);
        Block block = blockAccessor.GetBlock(position, BlockLayersAccess.MostSolid);
        _blockPosBuffer.Set(x, y, z);

        Cuboidf[] collisionBoxes = block.GetCollisionBoxes(blockAccessor, _blockPosBuffer);
        if (collisionBoxes == null || collisionBoxes.Length == 0) return null;

        float closestIntersectionParameter = 0;
        Vector3? closestIntersection = null;

        _blockPosVecBuffer.Set(x, y, z);
        for (int i = 0; i < collisionBoxes.Length; i++)
        {
            Cuboidf? collBox = collisionBoxes[i];
            if (collBox == null) continue;

            (float t, Vector3? intersection) = IntersectBlockCollisionBox(collBox, x, y, z);

            if (intersection == null) continue;

            closestIntersectionParameter = MathF.Min(closestIntersectionParameter, t);
            closestIntersection = intersection;
        }

        if (closestIntersection != null)
        {
            return (block, closestIntersection.Value, closestIntersectionParameter);
        }

        return null;
    }
}

public readonly struct RectangularCollider : IParryCollider
{
    public readonly Vector3 VertexA;
    public readonly Vector3 VertexB;
    public readonly Vector3 VertexC;
    public readonly Vector3 VertexD;

    public RectangularCollider(Vector4 vertexA, Vector4 vertexB, Vector4 vertexC, Vector4 vertexD)
    {
        VertexA = new(vertexA.X, vertexA.Y, vertexA.Z);
        VertexB = new(vertexB.X, vertexB.Y, vertexB.Z);
        VertexC = new(vertexC.X, vertexC.Y, vertexC.Z);
        VertexD = new(vertexD.X, vertexD.Y, vertexD.Z);
    }
    public RectangularCollider(Vector3 vertexA, Vector3 vertexB, Vector3 vertexC, Vector3 vertexD)
    {
        VertexA = new(vertexA.X, vertexA.Y, vertexA.Z);
        VertexB = new(vertexB.X, vertexB.Y, vertexB.Z);
        VertexC = new(vertexC.X, vertexC.Y, vertexC.Z);
        VertexD = new(vertexD.X, vertexD.Y, vertexD.Z);
    }
    public RectangularCollider(float[] vertices)
    {
        VertexA = new(vertices[0], vertices[1], vertices[2]);
        VertexB = new(vertices[3], vertices[4], vertices[5]);
        VertexC = new(vertices[6], vertices[7], vertices[8]);
        VertexD = new(vertices[9], vertices[10], vertices[11]);
    }
    
    public void Render(ICoreClientAPI api, EntityAgent entityPlayer, int color = -1)
    {
        BlockPos playerPos = entityPlayer.Pos.AsBlockPos;
        Vector3 playerPosVector = new(playerPos.X, playerPos.Y, playerPos.Z);

        Vector3 pointA = VertexA - playerPosVector;
        Vector3 pointB = VertexB - playerPosVector;
        Vector3 pointC = VertexC - playerPosVector;
        Vector3 pointD = VertexD - playerPosVector;

        api.Render.RenderLine(playerPos, pointA.X, pointA.Y, pointA.Z, pointB.X, pointB.Y, pointB.Z, color);
        api.Render.RenderLine(playerPos, pointB.X, pointB.Y, pointB.Z, pointC.X, pointC.Y, pointC.Z, color);
        api.Render.RenderLine(playerPos, pointC.X, pointC.Y, pointC.Z, pointD.X, pointD.Y, pointD.Z, color);
        api.Render.RenderLine(playerPos, pointD.X, pointD.Y, pointD.Z, pointA.X, pointA.Y, pointA.Z, color);
    }
    public bool IntersectSegment(LineSegmentCollider segment, out float parameter, out Vector3 intersection)
    {
        Vector3 normal = Vector3.Cross(VertexB - VertexA, VertexC - VertexA);

        #region Check if segment is parallel to the plane defined by the face
        float denominator = Vector3.Dot(normal, segment.Direction);
        if (Math.Abs(denominator) < 0.0001f)
        {
            parameter = -1;
            intersection = Vector3.Zero;
            return false;
        }
        #endregion

        #region Compute intersection point with the plane defined by the face and check if segment intersects the plane
        parameter = IntersectPlaneWithLine(segment.Position, segment.Direction, normal);
        if (parameter < 0 || parameter > 1)
        {
            intersection = Vector3.Zero;
            return false;
        }
        #endregion

        intersection = segment.Position + parameter * segment.Direction;

        #region Check if the intersection point is within the face boundaries
        Vector3 edge0 = VertexB - VertexA;
        Vector3 vp0 = intersection - VertexA;
        if (Vector3.Dot(normal, Vector3.Cross(edge0, vp0)) < 0)
        {
            return false;
        }

        Vector3 edge1 = VertexC - VertexB;
        Vector3 vp1 = intersection - VertexB;
        if (Vector3.Dot(normal, Vector3.Cross(edge1, vp1)) < 0)
        {
            return false;
        }

        Vector3 edge2 = VertexD - VertexC;
        Vector3 vp2 = intersection - VertexC;
        if (Vector3.Dot(normal, Vector3.Cross(edge2, vp2)) < 0)
        {
            return false;
        }

        Vector3 edge3 = VertexA - VertexD;
        Vector3 vp3 = intersection - VertexD;
        if (Vector3.Dot(normal, Vector3.Cross(edge3, vp3)) < 0)
        {
            return false;
        }
        #endregion



        return true;
    }
    public ICollider? Transform(EntityPos origin, EntityAgent entity, ItemSlot itemSlot, ICoreClientAPI api, bool right = true)
    {
        Matrixf? modelMatrix = ColliderTools.GetHeldItemModelMatrix(entity, itemSlot, api, right);
        if (modelMatrix is null) return null;

        return TransformCollider(this, modelMatrix, origin);
    }
    public ICollider Transform(Matrixf modelMatrix, EntityPos origin)
    {
        Vector3 vertexA = ColliderTools.TransformVector(VertexA, modelMatrix, origin);
        Vector3 vertexB = ColliderTools.TransformVector(VertexB, modelMatrix, origin);
        Vector3 vertexC = ColliderTools.TransformVector(VertexC, modelMatrix, origin);
        Vector3 vertexD = ColliderTools.TransformVector(VertexD, modelMatrix, origin);

        return new RectangularCollider(vertexA, vertexB, vertexC, vertexD);
    }

    public static bool Transform(IEnumerable<IHasRectangularCollider> segments, EntityPlayer entity, ItemSlot itemSlot, ICoreClientAPI api, bool right = true)
    {
        EntityPos playerPos = entity.Pos;
        Matrixf? modelMatrix = ColliderTools.GetHeldItemModelMatrix(entity, itemSlot, api, right);
        if (modelMatrix is null) return false;

        foreach (IHasRectangularCollider damageType in segments)
        {
            damageType.InWorldCollider = TransformCollider(damageType.RelativeCollider, modelMatrix, playerPos);
        }

        return true;
    }

    private static RectangularCollider TransformCollider(RectangularCollider value, Matrixf modelMatrix, EntityPos playerPos)
    {
        Vector3 vertexA = ColliderTools.TransformVector(value.VertexA, modelMatrix, playerPos);
        Vector3 vertexB = ColliderTools.TransformVector(value.VertexB, modelMatrix, playerPos);
        Vector3 vertexC = ColliderTools.TransformVector(value.VertexC, modelMatrix, playerPos);
        Vector3 vertexD = ColliderTools.TransformVector(value.VertexD, modelMatrix, playerPos);

        return new(vertexA, vertexB, vertexC, vertexD);
    }
    private float IntersectPlaneWithLine(Vector3 start, Vector3 direction, Vector3 normal)
    {
        float startProjection = Vector3.Dot(normal, start);
        float directionProjection = Vector3.Dot(normal, start + direction);
        float planeProjection = Vector3.Dot(normal, VertexA);

        return (planeProjection - startProjection) / (directionProjection - startProjection);
    }
}

public readonly struct OctagonalCollider : IParryCollider
{
    public readonly Vector3 VertexA;
    public readonly Vector3 VertexB;
    public readonly Vector3 VertexC;
    public readonly Vector3 VertexD;
    public readonly Vector3 VertexE;
    public readonly Vector3 VertexF;
    public readonly Vector3 VertexG;
    public readonly Vector3 VertexH;

    public OctagonalCollider(Vector4 vertexA, Vector4 vertexB, Vector4 vertexC, Vector4 vertexD, Vector4 vertexE, Vector4 vertexF, Vector4 vertexG, Vector4 vertexH)
    {
        VertexA = new(vertexA.X, vertexA.Y, vertexA.Z);
        VertexB = new(vertexB.X, vertexB.Y, vertexB.Z);
        VertexC = new(vertexC.X, vertexC.Y, vertexC.Z);
        VertexD = new(vertexD.X, vertexD.Y, vertexD.Z);
        VertexE = new(vertexE.X, vertexE.Y, vertexE.Z);
        VertexF = new(vertexF.X, vertexF.Y, vertexF.Z);
        VertexG = new(vertexG.X, vertexG.Y, vertexG.Z);
        VertexH = new(vertexH.X, vertexH.Y, vertexH.Z);
    }
    public OctagonalCollider(Vector3 vertexA, Vector3 vertexB, Vector3 vertexC, Vector3 vertexD, Vector3 vertexE, Vector3 vertexF, Vector3 vertexG, Vector3 vertexH)
    {
        VertexA = new(vertexA.X, vertexA.Y, vertexA.Z);
        VertexB = new(vertexB.X, vertexB.Y, vertexB.Z);
        VertexC = new(vertexC.X, vertexC.Y, vertexC.Z);
        VertexD = new(vertexD.X, vertexD.Y, vertexD.Z);
        VertexE = new(vertexE.X, vertexE.Y, vertexE.Z);
        VertexF = new(vertexF.X, vertexF.Y, vertexF.Z);
        VertexG = new(vertexG.X, vertexG.Y, vertexG.Z);
        VertexH = new(vertexH.X, vertexH.Y, vertexH.Z);
    }
    public OctagonalCollider(float[] vertices)
    {
        VertexA = new(vertices[0], vertices[1], vertices[2]);
        VertexB = new(vertices[3], vertices[4], vertices[5]);
        VertexC = new(vertices[6], vertices[7], vertices[8]);
        VertexD = new(vertices[9], vertices[10], vertices[11]);
        VertexE = new(vertices[12], vertices[13], vertices[14]);
        VertexF = new(vertices[15], vertices[16], vertices[17]);
        VertexG = new(vertices[18], vertices[19], vertices[20]);
        VertexH = new(vertices[21], vertices[22], vertices[23]);
    }

    public void Render(ICoreClientAPI api, EntityAgent entityPlayer, int color = -1)
    {
        BlockPos playerPos = entityPlayer.Pos.AsBlockPos;
        Vector3 playerPosVector = new(playerPos.X, playerPos.Y, playerPos.Z);

        Vector3 pointA = VertexA - playerPosVector;
        Vector3 pointB = VertexB - playerPosVector;
        Vector3 pointC = VertexC - playerPosVector;
        Vector3 pointD = VertexD - playerPosVector;
        Vector3 pointE = VertexE - playerPosVector;
        Vector3 pointF = VertexF - playerPosVector;
        Vector3 pointG = VertexG - playerPosVector;
        Vector3 pointH = VertexH - playerPosVector;

        api.Render.RenderLine(playerPos, pointA.X, pointA.Y, pointA.Z, pointB.X, pointB.Y, pointB.Z, color);
        api.Render.RenderLine(playerPos, pointB.X, pointB.Y, pointA.Z, pointC.X, pointC.Y, pointC.Z, color);
        api.Render.RenderLine(playerPos, pointC.X, pointC.Y, pointA.Z, pointD.X, pointD.Y, pointD.Z, color);
        api.Render.RenderLine(playerPos, pointD.X, pointD.Y, pointA.Z, pointE.X, pointE.Y, pointE.Z, color);
        api.Render.RenderLine(playerPos, pointE.X, pointE.Y, pointA.Z, pointF.X, pointF.Y, pointF.Z, color);
        api.Render.RenderLine(playerPos, pointF.X, pointF.Y, pointA.Z, pointG.X, pointG.Y, pointG.Z, color);
        api.Render.RenderLine(playerPos, pointG.X, pointG.Y, pointA.Z, pointH.X, pointH.Y, pointH.Z, color);
        api.Render.RenderLine(playerPos, pointH.X, pointH.Y, pointA.Z, pointA.X, pointA.Y, pointA.Z, color);
    }
    public bool IntersectSegment(LineSegmentCollider segment, out float parameter, out Vector3 intersection)
    {
        Vector3 normal = Vector3.Cross(VertexB - VertexA, VertexC - VertexA);

        #region Check if segment is parallel to the plane defined by the face
        float denominator = Vector3.Dot(normal, segment.Direction);
        if (Math.Abs(denominator) < 0.0001f)
        {
            parameter = -1;
            intersection = Vector3.Zero;
            return false;
        }
        #endregion

        #region Compute intersection point with the plane defined by the face and check if segment intersects the plane
        parameter = IntersectPlaneWithLine(segment.Position, segment.Direction, normal);
        if (parameter < 0 || parameter > 1)
        {
            intersection = Vector3.Zero;
            return false;
        }
        #endregion

        intersection = segment.Position + parameter * segment.Direction;

        #region Check if the intersection point is within the face boundaries
        Vector3 edge0 = VertexB - VertexA;
        Vector3 vp0 = intersection - VertexA;
        if (Vector3.Dot(normal, Vector3.Cross(edge0, vp0)) < 0)
        {
            return false;
        }

        Vector3 edge1 = VertexC - VertexB;
        Vector3 vp1 = intersection - VertexB;
        if (Vector3.Dot(normal, Vector3.Cross(edge1, vp1)) < 0)
        {
            return false;
        }

        Vector3 edge2 = VertexD - VertexC;
        Vector3 vp2 = intersection - VertexC;
        if (Vector3.Dot(normal, Vector3.Cross(edge2, vp2)) < 0)
        {
            return false;
        }

        Vector3 edge3 = VertexE - VertexD;
        Vector3 vp3 = intersection - VertexD;
        if (Vector3.Dot(normal, Vector3.Cross(edge3, vp3)) < 0)
        {
            return false;
        }

        Vector3 edge4 = VertexF - VertexE;
        Vector3 vp4 = intersection - VertexE;
        if (Vector3.Dot(normal, Vector3.Cross(edge4, vp4)) < 0)
        {
            return false;
        }

        Vector3 edge5 = VertexG - VertexF;
        Vector3 vp5 = intersection - VertexF;
        if (Vector3.Dot(normal, Vector3.Cross(edge5, vp5)) < 0)
        {
            return false;
        }

        Vector3 edge6 = VertexH - VertexG;
        Vector3 vp6 = intersection - VertexG;
        if (Vector3.Dot(normal, Vector3.Cross(edge6, vp6)) < 0)
        {
            return false;
        }

        Vector3 edge7 = VertexA - VertexH;
        Vector3 vp7 = intersection - VertexH;
        if (Vector3.Dot(normal, Vector3.Cross(edge7, vp7)) < 0)
        {
            return false;
        }
        #endregion

        return true;
    }
    public ICollider? Transform(EntityPos origin, EntityAgent entity, ItemSlot itemSlot, ICoreClientAPI api, bool right = true)
    {
        Matrixf? modelMatrix = ColliderTools.GetHeldItemModelMatrix(entity, itemSlot, api, right);
        if (modelMatrix is null) return null;

        return TransformCollider(this, modelMatrix, origin);
    }
    public ICollider Transform(Matrixf modelMatrix, EntityPos origin)
    {
        Vector3 vertexA = ColliderTools.TransformVector(VertexA, modelMatrix, origin);
        Vector3 vertexB = ColliderTools.TransformVector(VertexB, modelMatrix, origin);
        Vector3 vertexC = ColliderTools.TransformVector(VertexC, modelMatrix, origin);
        Vector3 vertexD = ColliderTools.TransformVector(VertexD, modelMatrix, origin);
        Vector3 vertexE = ColliderTools.TransformVector(VertexE, modelMatrix, origin);
        Vector3 vertexF = ColliderTools.TransformVector(VertexF, modelMatrix, origin);
        Vector3 vertexG = ColliderTools.TransformVector(VertexG, modelMatrix, origin);
        Vector3 vertexH = ColliderTools.TransformVector(VertexH, modelMatrix, origin);

        return new OctagonalCollider(vertexA, vertexB, vertexC, vertexD, vertexE, vertexF, vertexG, vertexH);
    }

    public static bool Transform(IEnumerable<IHasOctagonalCollider> segments, EntityPlayer entity, ItemSlot itemSlot, ICoreClientAPI api, bool right = true)
    {
        EntityPos playerPos = entity.Pos;
        Matrixf? modelMatrix = ColliderTools.GetHeldItemModelMatrix(entity, itemSlot, api, right);
        if (modelMatrix is null) return false;

        foreach (IHasOctagonalCollider damageType in segments)
        {
            damageType.InWorldCollider = TransformCollider(damageType.RelativeCollider, modelMatrix, playerPos);
        }

        return true;
    }

    private static OctagonalCollider TransformCollider(OctagonalCollider value, Matrixf modelMatrix, EntityPos playerPos)
    {
        Vector3 vertexA = ColliderTools.TransformVector(value.VertexA, modelMatrix, playerPos);
        Vector3 vertexB = ColliderTools.TransformVector(value.VertexB, modelMatrix, playerPos);
        Vector3 vertexC = ColliderTools.TransformVector(value.VertexC, modelMatrix, playerPos);
        Vector3 vertexD = ColliderTools.TransformVector(value.VertexD, modelMatrix, playerPos);
        Vector3 vertexE = ColliderTools.TransformVector(value.VertexE, modelMatrix, playerPos);
        Vector3 vertexF = ColliderTools.TransformVector(value.VertexF, modelMatrix, playerPos);
        Vector3 vertexG = ColliderTools.TransformVector(value.VertexG, modelMatrix, playerPos);
        Vector3 vertexH = ColliderTools.TransformVector(value.VertexH, modelMatrix, playerPos);

        return new(vertexA, vertexB, vertexC, vertexD, vertexE, vertexF, vertexG, vertexH);
    }
    private float IntersectPlaneWithLine(Vector3 start, Vector3 direction, Vector3 normal)
    {
        float startProjection = Vector3.Dot(normal, start);
        float directionProjection = Vector3.Dot(normal, start + direction);
        float planeProjection = Vector3.Dot(normal, VertexA);

        return (planeProjection - startProjection) / (directionProjection - startProjection);
    }
}