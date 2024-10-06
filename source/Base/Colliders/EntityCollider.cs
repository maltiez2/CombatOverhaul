using System;
using System.Numerics;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

#if DEBUG
    using VSImGui.Debug;
#endif

namespace CombatOverhaul.Colliders;

public readonly struct CuboidFace
{
    public readonly Vector3 VertexA;
    public readonly Vector3 VertexB;
    public readonly Vector3 VertexC;
    public readonly Vector3 VertexD;

    public CuboidFace(Vector4 vertexA, Vector4 vertexB, Vector4 vertexC, Vector4 vertexD)
    {
        VertexA = new(vertexA.X, vertexA.Y, vertexA.Z);
        VertexB = new(vertexB.X, vertexB.Y, vertexB.Z);
        VertexC = new(vertexC.X, vertexC.Y, vertexC.Z);
        VertexD = new(vertexD.X, vertexD.Y, vertexD.Z);
    }

    private float IntersectPlaneWithLine(Vector3 start, Vector3 direction, Vector3 normal)
    {
        float startProjection = Vector3.Dot(normal, start);
        float directionProjection = Vector3.Dot(normal, start + direction);
        float planeProjection = Vector3.Dot(normal, VertexA);

        return (planeProjection - startProjection) / (directionProjection - startProjection);
    }

    public bool Collide(Vector3 segmentStart, Vector3 segmentDirection, out float parameter, out Vector3 intersection)
    {
        Vector3 normal = Vector3.Cross(VertexB - VertexA, VertexC - VertexA);

        #region Check if segment is parallel to the plane defined by the face
        float denominator = Vector3.Dot(normal, segmentDirection);
        if (Math.Abs(denominator) < 0.0001f)
        {
            parameter = -1;
            intersection = Vector3.Zero;
            return false;
        }
        #endregion

        #region Compute intersection point with the plane defined by the face and check if segment intersects the plane
        parameter = IntersectPlaneWithLine(segmentStart, segmentDirection, normal);
        if (parameter < 0 || parameter > 1)
        {
            intersection = Vector3.Zero;
            return false;
        }
        #endregion

        intersection = segmentStart + parameter * segmentDirection;

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
}

public readonly struct CuboidAABBCollider
{
    public readonly Vector3 VertexA;
    public readonly Vector3 VertexB;

    public CuboidAABBCollider(Vector3 vertexA, Vector3 vertexB)
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

    public bool Collide(Vector3 segmentStart, Vector3 segmentDirection, out float parameter)
    {
        Vector3 min = Vector3.Min(VertexA, VertexB);
        Vector3 max = Vector3.Max(VertexA, VertexB);

        parameter = 0;

        float tmin = (min.X - segmentStart.X) / segmentDirection.X;
        float tmax = (max.X - segmentStart.X) / segmentDirection.X;

        if (tmin > tmax)
        {
            float temp = tmin;
            tmin = tmax;
            tmax = temp;
        }

        float tymin = (min.Y - segmentStart.Y) / segmentDirection.Y;
        float tymax = (max.Y - segmentStart.Y) / segmentDirection.Y;

        if (tymin > tymax)
        {
            float temp = tymin;
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

        float tzmin = (min.Z - segmentStart.Z) / segmentDirection.Z;
        float tzmax = (max.Z - segmentStart.Z) / segmentDirection.Z;

        if (tzmin > tzmax)
        {
            float temp = tzmin;
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

    public bool CollideRough(Vector3 thisTickOrigin, Vector3 previousTickOrigin, float radius, out Vector3 intersection)
    {
        Vector3 origin = (thisTickOrigin + previousTickOrigin) / 2;
        float maxRadius = (thisTickOrigin - previousTickOrigin).Length() / 2f + radius;

        intersection = new(
            Math.Clamp(origin.X, Math.Min(VertexA.X, VertexB.X), Math.Max(VertexA.X, VertexB.X)),
            Math.Clamp(origin.Y, Math.Min(VertexA.Y, VertexB.Y), Math.Max(VertexA.Y, VertexB.Y)),
            Math.Clamp(origin.Z, Math.Min(VertexA.Z, VertexB.Z), Math.Max(VertexA.Z, VertexB.Z))
        );

        float distanceSquared = Vector3.DistanceSquared(thisTickOrigin, intersection);

        return distanceSquared <= maxRadius * maxRadius;
    }
    public bool Collide(Vector3 thisTickOrigin, Vector3 previousTickOrigin, float radius, out Vector3 intersection)
    {
        intersection = new(
            Math.Clamp(thisTickOrigin.X, Math.Min(VertexA.X, VertexB.X), Math.Max(VertexA.X, VertexB.X)),
            Math.Clamp(thisTickOrigin.Y, Math.Min(VertexA.Y, VertexB.Y), Math.Max(VertexA.Y, VertexB.Y)),
            Math.Clamp(thisTickOrigin.Z, Math.Min(VertexA.Z, VertexB.Z), Math.Max(VertexA.Z, VertexB.Z))
        );

        Vector3 origin = ClosestPointOnSegment(thisTickOrigin, previousTickOrigin, intersection);

        float distanceSquared = Vector3.DistanceSquared(origin, intersection);

        return distanceSquared <= radius * radius;
    }

    public static Vector3 ClosestPointOnSegment(Vector3 a, Vector3 b, Vector3 p)
    {
        Vector3 ab = b - a;
        float t = Vector3.Dot(p - a, ab) / ab.LengthSquared();
        t = Math.Clamp(t, 0.0f, 1.0f);
        return a + ab * t;
    }
}

public sealed class ShapeElementCollider
{
    public const int VertexCount = 8;
    public Vector4[] ElementVertices { get; } = new Vector4[VertexCount];
    public Vector4[] InworldVertices { get; } = new Vector4[VertexCount];
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

        float[] transformMatrix = GetTransformMatrix(JointId, transformMatrixAll);

        EntityPos playerPos = api.World.Player.Entity.Pos;

        for (int vertex = 0; vertex < VertexCount; vertex++)
        {
            InworldVertices[vertex] = MultiplyVectorByMatrix(transformMatrix, ElementVertices[vertex]);
            InworldVertices[vertex].W = 1.0f;
            InworldVertices[vertex] = MultiplyVectorByMatrix(Renderer.ModelMat, InworldVertices[vertex]);
            InworldVertices[vertex].X += (float)playerPos.X;
            InworldVertices[vertex].Y += (float)playerPos.Y;
            InworldVertices[vertex].Z += (float)playerPos.Z;
        }
    }
    public bool Collide(Vector3 segmentStart, Vector3 segmentDirection, out float parameter, out Vector3 intersection)
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

        float closestParameter = float.MaxValue;
        bool foundIntersection = false;
        intersection = Vector3.Zero;

        foreach (CuboidFace face in faces)
        {
            if (face.Collide(segmentStart, segmentDirection, out float currentParameter, out Vector3 faceIntersection) && currentParameter < closestParameter)
            {
                closestParameter = currentParameter;
                intersection = faceIntersection;
                foundIntersection = true;
            }
        }

        parameter = closestParameter;
        return foundIntersection;
    }
    public bool Collide(Vector3 thisTickOrigin, Vector3 previousTickOrigin, float radius, out float distance, out Vector3 intersection)
    {
        Vector3[] vertices = new Vector3[VertexCount];
        for (int index = 0; index < VertexCount; index++)
        {
            vertices[index] = new(InworldVertices[index].X, InworldVertices[index].Y, InworldVertices[index].Z);
        }

        intersection = ClosestPoint(thisTickOrigin, previousTickOrigin, vertices, out Vector3 segmentClosestPoint);
        distance = Vector3.Distance(intersection, segmentClosestPoint);

        return distance <= radius;
    }
    
    private void SetElementVertices(ShapeElement element)
    {
        Vector4 from = new((float)element.From[0], (float)element.From[1], (float)element.From[2], 1);
        Vector4 to = new((float)element.To[0], (float)element.To[1], (float)element.To[2], 1);
        Vector4 diagonal = to - from;

        ElementVertices[0] = from;
        ElementVertices[6] = to;
        ElementVertices[1] = new(from.X + diagonal.X, from.Y, from.Z, from.W);
        ElementVertices[3] = new(from.X, from.Y + diagonal.Y, from.Z, from.W);
        ElementVertices[4] = new(from.X, from.Y, from.Z + diagonal.Z, from.W);
        ElementVertices[2] = new(from.X + diagonal.X, from.Y + diagonal.Y, from.Z, from.W);
        ElementVertices[7] = new(from.X, from.Y + diagonal.Y, from.Z + diagonal.Z, from.W);
        ElementVertices[5] = new(from.X + diagonal.X, from.Y, from.Z + diagonal.Z, from.W);

        float[] elementMatrixValues = new float[16];
        Mat4f.Identity(elementMatrixValues);
        Matrixf elementMatrix = new(elementMatrixValues);
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

    private static void GetElementTransformMatrix(Matrixf matrix, ShapeElement element)
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
    private static float[] GetTransformMatrix(int jointId, float[] TransformationMatrices4x3)
    {
        float[] transformMatrix = new float[16];
        Mat4f.Identity(transformMatrix);
        for (int elementIndex = 0; elementIndex < 16; elementIndex++)
        {
            int? transformMatricesIndex = GetIndex(jointId, elementIndex);
            if (transformMatricesIndex != null)
            {
                transformMatrix[elementIndex] = TransformationMatrices4x3[transformMatricesIndex.Value];
            }
        }
        return transformMatrix;
    }
    private static void GetElementTransformMatrixA(Matrixf matrix, ShapeElement element, float[] TransformationMatrices4x3)
    {
        if (element.ParentElement != null)
        {
            GetElementTransformMatrixA(matrix, element.ParentElement, TransformationMatrices4x3);
        }

        matrix
            .Translate(element.RotationOrigin[0], element.RotationOrigin[1], element.RotationOrigin[2])
            .RotateX((float)element.RotationX * GameMath.DEG2RAD)
            .RotateY((float)element.RotationY * GameMath.DEG2RAD)
            .RotateZ((float)element.RotationZ * GameMath.DEG2RAD)
            .Translate(0f - element.RotationOrigin[0], 0f - element.RotationOrigin[1], 0f - element.RotationOrigin[2])
            .Translate(element.From[0], element.From[1], element.From[2]);
    }
    private static Vector4 MultiplyVectorByMatrix(float[] matrix, Vector4 vector)
    {
        Vector4 result = new(0, 0, 0, 0);
        for (int i = 0; i < 4; i++)
        {
            for (int j = 0; j < 4; j++)
            {
                result[i] += matrix[4 * j + i] * vector[j];
            }
        }
        return result;
    }
    private static Vector3 ClosestPoint(Vector3 start, Vector3 end, Vector3[] obbVertices, out Vector3 segmentClosestPoint)
    {
        // Assuming the OBB vertices are ordered and form a valid OBB
        // Calculate the center of the OBB
        Vector3 center = (obbVertices[0] + obbVertices[1] + obbVertices[2] + obbVertices[3] +
                          obbVertices[4] + obbVertices[5] + obbVertices[6] + obbVertices[7]) / 8.0f;

        // Calculate the axes of the OBB
        Vector3[] axes = new Vector3[3];
        axes[0] = Vector3.Normalize(obbVertices[1] - obbVertices[0]); // X-axis
        axes[1] = Vector3.Normalize(obbVertices[3] - obbVertices[0]); // Y-axis
        axes[2] = Vector3.Normalize(obbVertices[4] - obbVertices[0]); // Z-axis

        // Calculate the half-sizes of the OBB along each axis
        float[] halfSizes = new float[3];
        halfSizes[0] = Vector3.Distance(obbVertices[0], obbVertices[1]) / 2.0f; // X half-size
        halfSizes[1] = Vector3.Distance(obbVertices[0], obbVertices[3]) / 2.0f; // Y half-size
        halfSizes[2] = Vector3.Distance(obbVertices[0], obbVertices[4]) / 2.0f; // Z half-size

        // Calculate the closest point on the OBB
        Vector3 closestPoint = center;
        segmentClosestPoint = ClosestPointOnSegment(start, end, center);
        Vector3 direction = segmentClosestPoint - center;

        for (int i = 0; i < 3; i++)
        {
            float distance = Vector3.Dot(direction, axes[i]);
            distance = Math.Clamp(distance, -halfSizes[i], halfSizes[i]);
            closestPoint += distance * axes[i];
        }

        return closestPoint;
    }
    public static Vector3 ClosestPointOnSegment(Vector3 A, Vector3 B, Vector3 P)
    {
        Vector3 AB = B - A;
        Vector3 AP = P - A;

        float AB_dot_AB = Vector3.Dot(AB, AB);
        float AP_dot_AB = Vector3.Dot(AP, AB);
        float t = AP_dot_AB / AB_dot_AB;

        // Clamp t to the range [0, 1]
        t = Math.Max(0, Math.Min(1, t));

        // Compute the closest point
        Vector3 closest = A + t * AB;
        return closest;
    }

    public void Render(ICoreClientAPI api, EntityAgent entityPlayer, int color = ColorUtil.WhiteArgb)
    {
        EntityAgent player = api.World.Player.Entity;

        BlockPos playerPos = player.Pos.AsBlockPos;
        Vec3f deltaPos = 0 - new Vec3f(playerPos.X, playerPos.Y, playerPos.Z);

        RenderLine(api, InworldVertices[0], InworldVertices[1], playerPos, deltaPos, color); //ColorUtil.ToRgba(255, 0, 0, 255));
        RenderLine(api, InworldVertices[0], InworldVertices[3], playerPos, deltaPos, color); //ColorUtil.ToRgba(255, 0, 255, 0));
        RenderLine(api, InworldVertices[0], InworldVertices[4], playerPos, deltaPos, color); // ColorUtil.ToRgba(255, 255, 0, 0));

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

    private static void RenderLine(ICoreClientAPI api, Vector4 start, Vector4 end, BlockPos playerPos, Vec3f deltaPos, int color)
    {
        api.Render.RenderLine(playerPos, start.X + deltaPos.X, start.Y + deltaPos.Y, start.Z + deltaPos.Z, end.X + deltaPos.X, end.Y + deltaPos.Y, end.Z + deltaPos.Z, color);
    }
}