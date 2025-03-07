using CombatOverhaul.Utils;
using OpenTK.Mathematics;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace CombatOverhaul.Colliders;

public readonly struct CuboidFace
{
    public readonly Vector3d VertexA;
    public readonly Vector3d VertexB;
    public readonly Vector3d VertexC;
    public readonly Vector3d VertexD;

    public CuboidFace(Vector4d vertexA, Vector4d vertexB, Vector4d vertexC, Vector4d vertexD)
    {
        VertexA = new(vertexA.X, vertexA.Y, vertexA.Z);
        VertexB = new(vertexB.X, vertexB.Y, vertexB.Z);
        VertexC = new(vertexC.X, vertexC.Y, vertexC.Z);
        VertexD = new(vertexD.X, vertexD.Y, vertexD.Z);
    }

    private double IntersectPlaneWithLine(Vector3d start, Vector3d direction, Vector3d normal)
    {
        double startProjection = Vector3d.Dot(normal, start);
        double directionProjection = Vector3d.Dot(normal, start + direction);
        double planeProjection = Vector3d.Dot(normal, VertexA);

        return (planeProjection - startProjection) / (directionProjection - startProjection);
    }

    public bool Collide(Vector3d segmentStart, Vector3d segmentDirection, out double parameter, out Vector3d intersection)
    {
        Vector3d normal = Vector3d.Cross(VertexB - VertexA, VertexC - VertexA);

        #region Check if segment is parallel to the plane defined by the face
        double denominator = Vector3d.Dot(normal, segmentDirection);
        if (Math.Abs(denominator) < 0.0001f)
        {
            parameter = -1;
            intersection = Vector3d.Zero;
            return false;
        }
        #endregion

        #region Compute intersection point with the plane defined by the face and check if segment intersects the plane
        parameter = IntersectPlaneWithLine(segmentStart, segmentDirection, normal);
        if (parameter < 0 || parameter > 1)
        {
            intersection = Vector3d.Zero;
            return false;
        }
        #endregion

        intersection = segmentStart + parameter * segmentDirection;

        #region Check if the intersection point is within the face boundaries
        Vector3d edge0 = VertexB - VertexA;
        Vector3d vp0 = intersection - VertexA;
        if (Vector3d.Dot(normal, Vector3d.Cross(edge0, vp0)) < 0)
        {
            return false;
        }

        Vector3d edge1 = VertexC - VertexB;
        Vector3d vp1 = intersection - VertexB;
        if (Vector3d.Dot(normal, Vector3d.Cross(edge1, vp1)) < 0)
        {
            return false;
        }

        Vector3d edge2 = VertexD - VertexC;
        Vector3d vp2 = intersection - VertexC;
        if (Vector3d.Dot(normal, Vector3d.Cross(edge2, vp2)) < 0)
        {
            return false;
        }

        Vector3d edge3 = VertexA - VertexD;
        Vector3d vp3 = intersection - VertexD;
        if (Vector3d.Dot(normal, Vector3d.Cross(edge3, vp3)) < 0)
        {
            return false;
        }
        #endregion



        return true;
    }
}

public readonly struct LineCollider
{
    public readonly Vector3d Tail;
    public readonly Vector3d Head;

    public LineCollider(Vector3d tail, Vector3d head)
    {
        Tail = tail;
        Head = head;
    }
}

public readonly struct TriangleCollider
{
    public readonly Vector3d VertexA;
    public readonly Vector3d VertexB;
    public readonly Vector3d VertexC;

    public TriangleCollider(Vector3d vertexA, Vector3d vertexB, Vector3d vertexC)
    {
        VertexA = vertexA;
        VertexB = vertexB;
        VertexC = vertexC;
    }

    public static bool IntersectTriangles(TriangleCollider first, TriangleCollider second)
    {
        if (!IntersectTrianglePlanes(first, second))
            return false;

        return GenerateSeparatingAxesAndCheckOverlap(first, second);
    }

    private static bool IntersectTrianglePlanes(TriangleCollider first, TriangleCollider second)
    {
        // Compute plane normals
        Vector3d N1 = Vector3d.Normalize(Vector3d.Cross(first.VertexB - first.VertexA, first.VertexC - first.VertexA));
        Vector3d N2 = Vector3d.Normalize(Vector3d.Cross(second.VertexB - second.VertexA, second.VertexC - second.VertexA));

        // Planes dont intersect if these normalized vectors are parallel.
        return Math.Abs(Vector3d.Dot(N1, N2)) < 1.0f;
    }

    private static bool GenerateSeparatingAxesAndCheckOverlap(TriangleCollider first, TriangleCollider second)
    {
        Vector3d firstEdgeAB = first.VertexB - first.VertexA;
        Vector3d firstEdgeBC = first.VertexC - first.VertexB;
        Vector3d firstEdgeCA = first.VertexA - first.VertexC;
        Vector3d secondEdgeAB = second.VertexB - second.VertexA;
        Vector3d secondEdgeBC = second.VertexC - second.VertexB;
        Vector3d secondEdgeCA = second.VertexA - second.VertexC;

        Vector3d axisABAB = Vector3d.Cross(firstEdgeAB, secondEdgeAB);
        Vector3d axisABBC = Vector3d.Cross(firstEdgeAB, secondEdgeBC);
        Vector3d axisABCA = Vector3d.Cross(firstEdgeAB, secondEdgeCA);
        Vector3d axisBCAB = Vector3d.Cross(firstEdgeBC, secondEdgeAB);
        Vector3d axisBCBC = Vector3d.Cross(firstEdgeBC, secondEdgeBC);
        Vector3d axisBCCA = Vector3d.Cross(firstEdgeBC, secondEdgeCA);
        Vector3d axisCAAB = Vector3d.Cross(firstEdgeCA, secondEdgeAB);
        Vector3d axisCABC = Vector3d.Cross(firstEdgeCA, secondEdgeBC);
        Vector3d axisCACA = Vector3d.Cross(firstEdgeCA, secondEdgeCA);

        bool overlap =
            OverlapOnAxis(first, second, axisABAB) ||
            OverlapOnAxis(first, second, axisABBC) ||
            OverlapOnAxis(first, second, axisABCA) ||
            OverlapOnAxis(first, second, axisBCAB) ||
            OverlapOnAxis(first, second, axisBCBC) ||
            OverlapOnAxis(first, second, axisBCCA) ||
            OverlapOnAxis(first, second, axisCAAB) ||
            OverlapOnAxis(first, second, axisCABC) ||
            OverlapOnAxis(first, second, axisCACA);

        return overlap;
    }

    private static bool OverlapOnAxis(TriangleCollider first, TriangleCollider second, Vector3d axis)
    {
        Vector3d firstProjection = new(Vector3d.Dot(first.VertexA, axis), Vector3d.Dot(first.VertexB, axis), Vector3d.Dot(first.VertexC, axis));
        Vector3d secondProjection = new(Vector3d.Dot(second.VertexA, axis), Vector3d.Dot(second.VertexB, axis), Vector3d.Dot(second.VertexC, axis));

        double firstMin = Math.Min(firstProjection.X, Math.Min(firstProjection.Y, firstProjection.Z));
        double firstMax = Math.Max(firstProjection.X, Math.Max(firstProjection.Y, firstProjection.Z));
        double secondMin = Math.Min(secondProjection.X, Math.Min(secondProjection.Y, secondProjection.Z));
        double secondMax = Math.Max(secondProjection.X, Math.Max(secondProjection.Y, secondProjection.Z));

        // Check for overlap
        return firstMin >= secondMin && secondMax >= firstMax;
    }

}

public readonly struct CuboidAABBCollider
{
    public readonly Vector3d VertexA;
    public readonly Vector3d VertexB;

    public CuboidAABBCollider(Vector3d vertexA, Vector3d vertexB)
    {
        VertexA = vertexA;
        VertexB = vertexB;
    }
    public CuboidAABBCollider(Cuboidf cuboid)
    {
        VertexA = new(cuboid.X1, cuboid.Y1, cuboid.Z1);
        VertexB = new(cuboid.X2, cuboid.Y2, cuboid.Z2);
    }

    public CuboidAABBCollider(Entity entity)
    {
        Cuboidf collisionBox = entity.CollisionBox.Clone();
        EntityPos position = entity.Pos;
        collisionBox.X1 += (float)position.X;
        collisionBox.Y1 += (float)position.Y;
        collisionBox.Z1 += (float)position.Z;
        collisionBox.X2 += (float)position.X;
        collisionBox.Y2 += (float)position.Y;
        collisionBox.Z2 += (float)position.Z;

        VertexA = new(collisionBox.X1, collisionBox.Y1, collisionBox.Z1);
        VertexB = new(collisionBox.X2, collisionBox.Y2, collisionBox.Z2);
    }

    public bool Collide(Vector3d segmentStart, Vector3d segmentDirection, out double parameter)
    {
        Vector3d min = Vector3d.ComponentMin(VertexA, VertexB);
        Vector3d max = Vector3d.ComponentMax(VertexA, VertexB);

        parameter = 0;

        double tmin = (min.X - segmentStart.X) / segmentDirection.X;
        double tmax = (max.X - segmentStart.X) / segmentDirection.X;

        if (tmin > tmax)
        {
            double temp = tmin;
            tmin = tmax;
            tmax = temp;
        }

        double tymin = (min.Y - segmentStart.Y) / segmentDirection.Y;
        double tymax = (max.Y - segmentStart.Y) / segmentDirection.Y;

        if (tymin > tymax)
        {
            double temp = tymin;
            tymin = tymax;
            tymax = temp;
        }

        if ((tmin > tymax) || (tymin > tmax))
        {
            return false;
        }

        if (tymin > tmin)
        {
            tmin = tymin;
        }

        if (tymax < tmax)
        {
            tmax = tymax;
        }

        double tzmin = (min.Z - segmentStart.Z) / segmentDirection.Z;
        double tzmax = (max.Z - segmentStart.Z) / segmentDirection.Z;

        if (tzmin > tzmax)
        {
            double temp = tzmin;
            tzmin = tzmax;
            tzmax = temp;
        }

        if ((tmin > tzmax) || (tzmin > tmax))
        {
            return false;
        }

        parameter = tzmin;

        return true;
    }

    public bool CollideRough(Vector3d thisTickOrigin, Vector3d previousTickOrigin, double radius, out Vector3d intersection)
    {
        Vector3d origin = (thisTickOrigin + previousTickOrigin) / 2;
        double maxRadius = (thisTickOrigin - previousTickOrigin).Length / 2f + radius;

        intersection = new(
            Math.Clamp(origin.X, Math.Min(VertexA.X, VertexB.X), Math.Max(VertexA.X, VertexB.X)),
            Math.Clamp(origin.Y, Math.Min(VertexA.Y, VertexB.Y), Math.Max(VertexA.Y, VertexB.Y)),
            Math.Clamp(origin.Z, Math.Min(VertexA.Z, VertexB.Z), Math.Max(VertexA.Z, VertexB.Z))
        );

        double distanceSquared = Vector3d.DistanceSquared(thisTickOrigin, intersection);

        return distanceSquared <= maxRadius * maxRadius;
    }
    public bool Collide(Vector3d thisTickOrigin, Vector3d previousTickOrigin, double radius, out Vector3d intersection)
    {
        intersection = new(
            Math.Clamp(thisTickOrigin.X, Math.Min(VertexA.X, VertexB.X), Math.Max(VertexA.X, VertexB.X)),
            Math.Clamp(thisTickOrigin.Y, Math.Min(VertexA.Y, VertexB.Y), Math.Max(VertexA.Y, VertexB.Y)),
            Math.Clamp(thisTickOrigin.Z, Math.Min(VertexA.Z, VertexB.Z), Math.Max(VertexA.Z, VertexB.Z))
        );

        Vector3d origin = ClosestPointOnSegment(thisTickOrigin, previousTickOrigin, intersection);

        double distanceSquared = Vector3d.DistanceSquared(origin, intersection);

        return distanceSquared <= radius * radius;
    }

    public static Vector3d ClosestPointOnSegment(Vector3d a, Vector3d b, Vector3d p)
    {
        Vector3d ab = b - a;
        double t = Vector3d.Dot(p - a, ab) / ab.LengthSquared;
        t = Math.Clamp(t, 0.0f, 1.0f);
        return a + ab * t;
    }
}

public sealed class ShapeElementCollider
{
    public const int VertexCount = 8;
    public Vector4d[] ElementVertices { get; } = new Vector4d[VertexCount];
    public Vector4d[] InworldVertices { get; } = new Vector4d[VertexCount];
    public int JointId { get; set; }

    public EntityShapeRenderer? Renderer { get; set; } = null;
    public bool HasRenderer { get; set; } = false;

    public ShapeElementCollider(ShapeElement element)
    {
        JointId = element.JointId;
        SetElementVertices(element);
    }

    public void Transform(float[] transformMatrixAll, ICoreClientAPI api)
    {
        if (Renderer == null) return;

        double[] transformMatrix = GetTransformMatrix(JointId, transformMatrixAll);

        EntityPos playerPos = api.World.Player.Entity.Pos;

        for (int vertex = 0; vertex < VertexCount; vertex++)
        {
            InworldVertices[vertex] = MultiplyVectorByMatrix(transformMatrix, ElementVertices[vertex]);
            InworldVertices[vertex].W = 1.0f;
            InworldVertices[vertex] = MultiplyVectorByMatrix(Renderer.ModelMat, InworldVertices[vertex]);
            InworldVertices[vertex].X += playerPos.X;
            InworldVertices[vertex].Y += playerPos.Y;
            InworldVertices[vertex].Z += playerPos.Z;
        }
    }
    public bool Collide(Vector3d segmentStart, Vector3d segmentDirection, out double parameter, out Vector3d intersection)
    {
        CuboidFace[] faces = new[]
        {
            new CuboidFace(InworldVertices[0], InworldVertices[1], InworldVertices[2], InworldVertices[3]),
            new CuboidFace(InworldVertices[4], InworldVertices[5], InworldVertices[6], InworldVertices[7]),
            new CuboidFace(InworldVertices[0], InworldVertices[1], InworldVertices[5], InworldVertices[4]),
            new CuboidFace(InworldVertices[2], InworldVertices[3], InworldVertices[7], InworldVertices[6]),
            new CuboidFace(InworldVertices[0], InworldVertices[3], InworldVertices[7], InworldVertices[4]),
            new CuboidFace(InworldVertices[1], InworldVertices[2], InworldVertices[6], InworldVertices[5])
        };

        double closestParameter = double.MaxValue;
        bool foundIntersection = false;
        intersection = Vector3d.Zero;

        foreach (CuboidFace face in faces)
        {
            if (face.Collide(segmentStart, segmentDirection, out double currentParameter, out Vector3d faceIntersection) && currentParameter < closestParameter)
            {
                closestParameter = currentParameter;
                intersection = faceIntersection;
                foundIntersection = true;
            }
        }

        parameter = closestParameter;
        return foundIntersection;
    }
    public bool Collide(Vector3d thisTickOrigin, Vector3d previousTickOrigin, double radius, out double distance, out Vector3d intersection)
    {
        Vector3d[] vertices = new Vector3d[VertexCount];
        for (int index = 0; index < VertexCount; index++)
        {
            vertices[index] = new(InworldVertices[index].X, InworldVertices[index].Y, InworldVertices[index].Z);
        }

        intersection = ClosestPoint(thisTickOrigin, previousTickOrigin, vertices, out Vector3d segmentClosestPoint);
        distance = Vector3d.Distance(intersection, segmentClosestPoint);

        return distance <= radius;
    }
    public bool Collide(Vector3d thisTickOrigin, Vector3d previousTickOrigin, double radius, out double distance, out Vector3d intersection, out Vector3d segmentClosestPoint)
    {
        Vector3d[] vertices = new Vector3d[VertexCount];
        for (int index = 0; index < VertexCount; index++)
        {
            vertices[index] = new(InworldVertices[index].X, InworldVertices[index].Y, InworldVertices[index].Z);
        }

        intersection = ClosestPoint(thisTickOrigin, previousTickOrigin, vertices, out segmentClosestPoint);
        distance = Vector3d.Distance(intersection, segmentClosestPoint);

        return distance <= radius;
    }

    private void SetElementVertices(ShapeElement element)
    {
        Vector4d from = new(element.From[0], element.From[1], element.From[2], 1);
        Vector4d to = new(element.To[0], element.To[1], element.To[2], 1);
        Vector4d diagonal = to - from;

        ElementVertices[0] = from;
        ElementVertices[6] = to;
        ElementVertices[1] = new(from.X + diagonal.X, from.Y, from.Z, from.W);
        ElementVertices[3] = new(from.X, from.Y + diagonal.Y, from.Z, from.W);
        ElementVertices[4] = new(from.X, from.Y, from.Z + diagonal.Z, from.W);
        ElementVertices[2] = new(from.X + diagonal.X, from.Y + diagonal.Y, from.Z, from.W);
        ElementVertices[7] = new(from.X, from.Y + diagonal.Y, from.Z + diagonal.Z, from.W);
        ElementVertices[5] = new(from.X + diagonal.X, from.Y, from.Z + diagonal.Z, from.W);

        double[] elementMatrixValues = new double[16];
        Mat4d.Identity(elementMatrixValues);
        Matrixd elementMatrix = new(elementMatrixValues);
        if (element.ParentElement != null) GetElementTransformMatrix(elementMatrix, element.ParentElement);

        elementMatrix
            .Translate(element.RotationOrigin[0], element.RotationOrigin[1], element.RotationOrigin[2])
            .RotateX((float)element.RotationX * GameMath.DEG2RAD)
            .RotateY((float)element.RotationY * GameMath.DEG2RAD)
            .RotateZ((float)element.RotationZ * GameMath.DEG2RAD)
            .Translate(0f - element.RotationOrigin[0], 0f - element.RotationOrigin[1], 0f - element.RotationOrigin[2]);

        for (int vertex = 0; vertex < VertexCount; vertex++)
        {
            ElementVertices[vertex] = ElementVertices[vertex] / 16f;
            ElementVertices[vertex] = MultiplyVectorByMatrix(elementMatrix.Values, ElementVertices[vertex]);
            ElementVertices[vertex].W = 1f;
        }
    }

    private static void GetElementTransformMatrix(Matrixd matrix, ShapeElement element)
    {
        if (element.ParentElement != null)
        {
            GetElementTransformMatrix(matrix, element.ParentElement);
        }

        if (element.RotationOrigin == null)
        {
            element.RotationOrigin = new double[3] { 0, 0, 0 };
        }

        matrix
            .Translate(element.RotationOrigin[0], element.RotationOrigin[1], element.RotationOrigin[2])
            .RotateX((float)element.RotationX * GameMath.DEG2RAD)
            .RotateY((float)element.RotationY * GameMath.DEG2RAD)
            .RotateZ((float)element.RotationZ * GameMath.DEG2RAD)
            .Translate(0f - element.RotationOrigin[0], 0f - element.RotationOrigin[1], 0f - element.RotationOrigin[2])
            .Translate(element.From[0], element.From[1], element.From[2]);
    }
    private static int? GetIndex(int jointId, int matrixElementIndex)
    {
        int index = 16 * jointId;
        int offset = matrixElementIndex; /*matrixElementIndex switch
        {
            0 => 0,
            1 => 1,
            2 => 2,
            4 => 3,
            5 => 4,
            6 => 5,
            8 => 6,
            9 => 7,
            10 => 8,
            12 => 9,
            13 => 10,
            14 => 11,
            _ => -1
        };*/

        if (offset < 0) return null;

        return index + offset;
    }
    private static double[] GetTransformMatrix(int jointId, float[] TransformationMatrices4x4)
    {
        double[] transformMatrix = new double[16];
        Mat4d.Identity(transformMatrix);
        for (int elementIndex = 0; elementIndex < 16; elementIndex++)
        {
            int? transformMatricesIndex = GetIndex(jointId, elementIndex);
            if (transformMatricesIndex != null)
            {
                transformMatrix[elementIndex] = TransformationMatrices4x4[transformMatricesIndex.Value];
            }
        }
        return transformMatrix;
    }
    private static void GetElementTransformMatrixA(Matrixd matrix, ShapeElement element, double[] TransformationMatrices4x4)
    {
        if (element.ParentElement != null)
        {
            GetElementTransformMatrixA(matrix, element.ParentElement, TransformationMatrices4x4);
        }

        matrix
            .Translate(element.RotationOrigin[0], element.RotationOrigin[1], element.RotationOrigin[2])
            .RotateX(element.RotationX * GameMath.DEG2RAD)
            .RotateY(element.RotationY * GameMath.DEG2RAD)
            .RotateZ(element.RotationZ * GameMath.DEG2RAD)
            .Translate(0f - element.RotationOrigin[0], 0f - element.RotationOrigin[1], 0f - element.RotationOrigin[2])
            .Translate(element.From[0], element.From[1], element.From[2]);
    }
    private static Vector4d MultiplyVectorByMatrix(double[] matrix, Vector4d vector)
    {
        Vector4d result = new(0, 0, 0, 0);
        for (int i = 0; i < 4; i++)
        {
            for (int j = 0; j < 4; j++)
            {
                result[i] += matrix[4 * j + i] * vector[j];
            }
        }
        return result;
    }
    private static Vector4d MultiplyVectorByMatrix(float[] matrix, Vector4d vector)
    {
        Vector4d result = new(0, 0, 0, 0);
        for (int i = 0; i < 4; i++)
        {
            for (int j = 0; j < 4; j++)
            {
                result[i] += matrix[4 * j + i] * vector[j];
            }
        }
        return result;
    }
    private Vector3d ClosestPoint(Vector3d start, Vector3d end, Vector3d[] obbVertices, out Vector3d segmentClosestPoint)
    {
        // Assuming the OBB vertices are ordered and form a valid OBB
        // Calculate the center of the OBB
        Vector3d center = (obbVertices[0] + obbVertices[1] + obbVertices[2] + obbVertices[3] +
                          obbVertices[4] + obbVertices[5] + obbVertices[6] + obbVertices[7]) / 8.0f;

        // Calculate the axes of the OBB
        Vector3d[] axes = new Vector3d[3];
        axes[0] = Vector3d.Normalize(obbVertices[1] - obbVertices[0]); // X-axis
        axes[1] = Vector3d.Normalize(obbVertices[3] - obbVertices[0]); // Y-axis
        axes[2] = Vector3d.Normalize(obbVertices[4] - obbVertices[0]); // Z-axis

        // Calculate the half-sizes of the OBB along each axis
        double[] halfSizes = new double[3];
        halfSizes[0] = Vector3d.Distance(obbVertices[0], obbVertices[1]) / 2.0f; // X half-size
        halfSizes[1] = Vector3d.Distance(obbVertices[0], obbVertices[3]) / 2.0f; // Y half-size
        halfSizes[2] = Vector3d.Distance(obbVertices[0], obbVertices[4]) / 2.0f; // Z half-size

        // Calculate the closest point on the OBB
        Vector3d closestPoint = center;
        segmentClosestPoint = ClosestPointOnSegment(start, end, center);
        Vector3d direction = segmentClosestPoint - center;

        for (int i = 0; i < 3; i++)
        {
            double distance = Vector3d.Dot(direction, axes[i]);
            distance = Math.Clamp(distance, -halfSizes[i], halfSizes[i]);
            closestPoint += distance * axes[i];
        }

        return closestPoint;
    }
    public static Vector3d ClosestPointOnSegment(Vector3d A, Vector3d B, Vector3d P)
    {
        Vector3d AB = B - A;
        Vector3d AP = P - A;

        double AB_dot_AB = Vector3d.Dot(AB, AB);
        double AP_dot_AB = Vector3d.Dot(AP, AB);
        double t = AP_dot_AB / AB_dot_AB;

        // Clamp t to the range [0, 1]
        t = Math.Max(0, Math.Min(1, t));

        // Compute the closest point
        Vector3d closest = A + t * AB;
        return closest;
    }

    public void Render(ICoreClientAPI api, EntityAgent entityPlayer, int color = ColorUtil.WhiteArgb)
    {
        EntityAgent player = api.World.Player.Entity;

        BlockPos playerPos = player.Pos.AsBlockPos;
        Vec3f deltaPos = 0 - new Vec3f(playerPos.X, playerPos.Y, playerPos.Z);

        RenderLine(api, InworldVertices[0], InworldVertices[1], playerPos, deltaPos, color);
        RenderLine(api, InworldVertices[0], InworldVertices[3], playerPos, deltaPos, color);
        RenderLine(api, InworldVertices[0], InworldVertices[4], playerPos, deltaPos, color);

        RenderLine(api, InworldVertices[1], InworldVertices[1], playerPos, deltaPos, color);
        RenderLine(api, InworldVertices[1], InworldVertices[5], playerPos, deltaPos, color);
        RenderLine(api, InworldVertices[2], InworldVertices[6], playerPos, deltaPos, color);
        RenderLine(api, InworldVertices[2], InworldVertices[3], playerPos, deltaPos, color);
        RenderLine(api, InworldVertices[3], InworldVertices[7], playerPos, deltaPos, color);
        RenderLine(api, InworldVertices[4], InworldVertices[7], playerPos, deltaPos, color);
        RenderLine(api, InworldVertices[4], InworldVertices[5], playerPos, deltaPos, color);
        RenderLine(api, InworldVertices[6], InworldVertices[7], playerPos, deltaPos, color);
        RenderLine(api, InworldVertices[6], InworldVertices[5], playerPos, deltaPos, color);
        RenderLine(api, InworldVertices[2], InworldVertices[1], playerPos, deltaPos, color);
    }

    private static void RenderLine(ICoreClientAPI api, Vector4d start, Vector4d end, BlockPos playerPos, Vec3f deltaPos, int color)
    {
        api.Render.RenderLine(playerPos, (float)start.X + deltaPos.X, (float)start.Y + deltaPos.Y, (float)start.Z + deltaPos.Z, (float)end.X + deltaPos.X, (float)end.Y + deltaPos.Y, (float)end.Z + deltaPos.Z, color);
    }
}