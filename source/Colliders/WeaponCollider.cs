using OpenTK.Mathematics;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace CombatOverhaul.Colliders;

public interface ICollider
{
    void Render(ICoreClientAPI api, EntityAgent entityPlayer, int color = ColorUtil.WhiteArgb);
    ICollider? Transform(EntityPos origin, EntityAgent entity, ItemSlot itemSlot, ICoreClientAPI api, bool right = true);
    ICollider Transform(Matrixf modelMatrix, EntityPos origin);
}

public interface IHasCollider
{
    ICollider RelativeCollider { get; }
    ICollider InWorldCollider { get; set; }
}

public interface IHasLineCollider
{
    LineSegmentCollider RelativeCollider { get; }
    LineSegmentCollider InWorldCollider { get; set; }
}

public interface IWeaponCollider : ICollider
{
    bool RoughIntersect(Cuboidf collisionBox);
    Vector3d? IntersectCuboids(IEnumerable<Cuboidf> collisionBoxes);
    Vector3d? IntersectCuboid(Cuboidf collisionBox, out double parameter);
    (Block block, Vector3d position, double parameter)? IntersectTerrain(ICoreClientAPI api);
}

public readonly struct LineSegmentCollider : IWeaponCollider
{
    public readonly Vector3d Position;
    public readonly Vector3d Direction;

    public LineSegmentCollider(Vector3d position, Vector3d direction)
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
        Vector3d playerPosVector = new(playerPos.X, playerPos.Y, playerPos.Z);

        Vector3d tail = Position - playerPosVector;
        Vector3d head = Position + Direction - playerPosVector;

        api.Render.RenderLine(playerPos, (float)tail.X, (float)tail.Y, (float)tail.Z, (float)head.X, (float)head.Y, (float)head.Z, color);
    }
    public ICollider? Transform(EntityPos origin, EntityAgent entity, ItemSlot itemSlot, ICoreClientAPI api, bool right = true)
    {
        Matrixf? modelMatrix = ColliderTools.GetHeldItemModelMatrix(entity, itemSlot, api, right);
        if (modelMatrix is null) return null;

        return TransformSegment(this, modelMatrix, origin);
    }
    public ICollider Transform(Matrixf modelMatrix, EntityPos origin)
    {
        Vector3d tail = ColliderTools.TransformVector(Position, modelMatrix, origin);
        Vector3d head = ColliderTools.TransformVector(Direction + Position, modelMatrix, origin);

        return new LineSegmentCollider(tail, head - tail);
    }
    public LineSegmentCollider? TransformLineSegment(EntityPos origin, EntityAgent entity, ItemSlot itemSlot, ICoreClientAPI api, bool right = true)
    {
        Matrixf? modelMatrix = ColliderTools.GetHeldItemModelMatrix(entity, itemSlot, api, right);
        if (modelMatrix is null) return null;

        return TransformSegment(this, modelMatrix, origin);
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
    public Vector3d? IntersectCuboids(IEnumerable<Cuboidf> collisionBoxes)
    {
        double tMin = 0.0f;
        double tMax = 1.0f;

        foreach (Cuboidf collisionBox in collisionBoxes)
        {
            if (!CheckAxisIntersection(Direction.X, Position.X, collisionBox.MinX, collisionBox.MaxX, ref tMin, ref tMax)) continue;
            if (!CheckAxisIntersection(Direction.Y, Position.Y, collisionBox.MinY, collisionBox.MaxY, ref tMin, ref tMax)) continue;
            if (!CheckAxisIntersection(Direction.Z, Position.Z, collisionBox.MinZ, collisionBox.MaxZ, ref tMin, ref tMax)) continue;
        }

        return Position + tMin * Direction;
    }
    public Vector3d? IntersectCuboid(Cuboidf collisionBox, out double parameter)
    {
        double tMin = 0.0f;
        double tMax = 1.0f;

        parameter = 1f;

        if (!CheckAxisIntersection(Direction.X, Position.X, collisionBox.MinX, collisionBox.MaxX, ref tMin, ref tMax)) return null;
        if (!CheckAxisIntersection(Direction.Y, Position.Y, collisionBox.MinY, collisionBox.MaxY, ref tMin, ref tMax)) return null;
        if (!CheckAxisIntersection(Direction.Z, Position.Z, collisionBox.MinZ, collisionBox.MaxZ, ref tMin, ref tMax)) return null;

        parameter = tMin;

        return Position + tMin * Direction;
    }
    public (Block block, Vector3d position, double parameter)? IntersectBlock(ICoreClientAPI api, BlockPos position)
    {
        (Block, Vector3d, double parameter)? intersection = IntersectBlock(api.World.BlockAccessor, position.X, position.Y, position.Z);

        return intersection;
    }
    public (Block block, Vector3d position, double parameter)? IntersectTerrain(ICoreClientAPI api)
    {
        int minX = (int)Math.Min(Position.X, Position.X + Direction.X);
        int minY = (int)Math.Min(Position.Y, Position.Y + Direction.Y);
        int minZ = (int)Math.Min(Position.Z, Position.Z + Direction.Z);

        int maxX = (int)Math.Max(Position.X, Position.X + Direction.X);
        int maxY = (int)Math.Max(Position.Y, Position.Y + Direction.Y);
        int maxZ = (int)Math.Max(Position.Z, Position.Z + Direction.Z);

        (Block block, Vector3d position, double parameter)? closestIntersection = null;

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                for (int z = minZ; z <= maxZ; z++)
                {
                    (Block, Vector3d, double parameter)? intersection = IntersectBlock(api.World.BlockAccessor, x, y, z);
                    
                    closestIntersection ??= intersection;
                    if (closestIntersection != null && intersection != null && closestIntersection.Value.parameter > intersection.Value.parameter)
                    {
                        closestIntersection = intersection;
                    }
                }
            }
        }

        return closestIntersection;
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
        Vector3d tail = ColliderTools.TransformVector(value.Position, modelMatrix, playerPos);
        Vector3d head = ColliderTools.TransformVector(value.Direction + value.Position, modelMatrix, playerPos);

        return new(tail, head - tail);
    }
    private static bool CheckAxisIntersection(double dirComponent, double startComponent, double minComponent, double maxComponent, ref double tMin, ref double tMax)
    {
        if (Math.Abs(dirComponent) < _epsilon)
        {
            // Ray is parallel to the slab, check if it's within the slab's extent
            if (startComponent < minComponent || startComponent > maxComponent) return false;
        }
        else
        {
            // Calculate intersection distances to the slab
            double t1 = (minComponent - startComponent) / dirComponent;
            double t2 = (maxComponent - startComponent) / dirComponent;

            // Swap t1 and t2 if needed so that t1 is the intersection with the near plane
            if (t1 > t2)
            {
                (t2, t1) = (t1, t2);
            }

            // Update the minimum intersection distance
            tMin = Math.Max(tMin, t1);
            // Update the maximum intersection distance
            tMax = Math.Min(tMax, t2);

            // Early exit if intersection is not possible
            if (tMin > tMax) return false;
        }

        return true;
    }
    private (double, Vector3d?) IntersectBlockCollisionBox(Cuboidf collisionBox, int x, int y, int z)
    {
        double tMin = 0.0f;
        double tMax = 1.0f;

        if (!CheckAxisIntersection(Direction.X, Position.X, collisionBox.MinX + x, collisionBox.MaxX + x, ref tMin, ref tMax)) return (0, null);
        if (!CheckAxisIntersection(Direction.Y, Position.Y, collisionBox.MinY + y, collisionBox.MaxY + y, ref tMin, ref tMax)) return (0, null);
        if (!CheckAxisIntersection(Direction.Z, Position.Z, collisionBox.MinZ + z, collisionBox.MaxZ + z, ref tMin, ref tMax)) return (0, null);

        return (tMin, Position + tMin * Direction);
    }
    private (Block, Vector3d, double)? IntersectBlock(IBlockAccessor blockAccessor, int x, int y, int z)
    {
        BlockPos position = new(x, y, z, 0);
        Block block = blockAccessor.GetBlock(position, BlockLayersAccess.MostSolid);
        _blockPosBuffer.Set(x, y, z);

        Cuboidf[] collisionBoxes = block.GetCollisionBoxes(blockAccessor, _blockPosBuffer);
        if (collisionBoxes == null || collisionBoxes.Length == 0) return null;

        double closestIntersectionParameter = 1;
        Vector3d? closestIntersection = null;

        _blockPosVecBuffer.Set(x, y, z);
        for (int i = 0; i < collisionBoxes.Length; i++)
        {
            Cuboidf? collBox = collisionBoxes[i];
            if (collBox == null) continue;

            (double t, Vector3d? intersection) = IntersectBlockCollisionBox(collBox, x, y, z);

            if (intersection == null) continue;

            closestIntersectionParameter = Math.Min(closestIntersectionParameter, t);
            closestIntersection = intersection;
        }

        if (closestIntersection != null)
        {
            return (block, closestIntersection.Value, closestIntersectionParameter);
        }

        return null;
    }
}