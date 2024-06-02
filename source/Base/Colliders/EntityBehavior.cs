using CombatOverhaul.Integration;
using System.Numerics;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

#if DEBUG
using VSImGui.Debug;
#endif

namespace CombatOverhaul.Colliders;


public enum ColliderTypes
{
    /// <summary>
    /// Normal damage received<br/>
    /// No special effects
    /// </summary>
    Torso,
    /// <summary>
    /// High damage received<br/>
    /// Affects: sight, bite attacks
    /// </summary>
    Head,
    /// <summary>
    /// Low damage received<br/>
    /// Affects: punch, throw and weapon attacks
    /// </summary>
    Arm,
    /// <summary>
    /// Low damage received<br/>
    /// Affects: movement, kick attacks
    /// </summary>
    Leg,
    /// <summary>
    /// Very high damage received<br/>
    /// No special effects
    /// </summary>
    Critical,
    /// <summary>
    /// No damage received<br/>
    /// No special effects
    /// </summary>
    Resistant
}

internal sealed class ColliderTypesJson
{
    public string[] Torso { get; set; } = Array.Empty<string>();
    public string[] Head { get; set; } = Array.Empty<string>();
    public string[] Arm { get; set; } = Array.Empty<string>();
    public string[] Leg { get; set; } = Array.Empty<string>();
    public string[] Critical { get; set; } = Array.Empty<string>();
    public string[] Resistant { get; set; } = Array.Empty<string>();
}

public sealed class CollidersEntityBehavior : EntityBehavior
{
    public CollidersEntityBehavior(Entity entity) : base(entity)
    {
#if DEBUG
        //DebugWidgets.CheckBox("AMlib", "rendering", "Render debug colliders", () => RenderColliders, (value) => RenderColliders = value);
#endif
    }

    public CuboidAABBCollider BoundingBox { get; private set; }
    public bool HasOBBCollider { get; private set; } = false;
    public bool UnprocessedElementsLeft { get; set; } = false;
    public HashSet<string> ShapeElementsToProcess { get; private set; } = new();
    public Dictionary<string, ColliderTypes> CollidersTypes { get; private set; } = new();
    public List<string> CollidersIds { get; private set; } = new();
    public Dictionary<int, ShapeElementCollider> Colliders { get; private set; } = new();
    public override string PropertyName() => "animationmanagerlib:colliders";
    internal ProceduralClientAnimator? Animator { get; set; }
    static public bool RenderColliders { get; set; } = false;

    public override void Initialize(EntityProperties properties, JsonObject attributes)
    {
        if (attributes.KeyExists("colliderShapeElements"))
        {
            HasOBBCollider = true;
            ShapeElementsToProcess = new(attributes["colliderShapeElements"].AsArray(Array.Empty<string>()));
            UnprocessedElementsLeft = true;
        }

        if (attributes.KeyExists("elementsTypes"))
        {
            ColliderTypesJson types = attributes["elementsTypes"].AsObject<ColliderTypesJson>();
            foreach (string collider in types.Torso)
            {
                CollidersTypes.Add(collider, ColliderTypes.Torso);
            }
            foreach (string collider in types.Head)
            {
                CollidersTypes.Add(collider, ColliderTypes.Head);
            }
            foreach (string collider in types.Arm)
            {
                CollidersTypes.Add(collider, ColliderTypes.Arm);
            }
            foreach (string collider in types.Leg)
            {
                CollidersTypes.Add(collider, ColliderTypes.Leg);
            }
            foreach (string collider in types.Critical)
            {
                CollidersTypes.Add(collider, ColliderTypes.Critical);
            }
        }
    }
    public override void OnGameTick(float deltaTime)
    {
        if (HasOBBCollider && Animator != null && entity.Api is ICoreClientAPI clientApi && entity.IsRendered) RecalculateColliders(Animator, clientApi);
    }

    public void Render(ICoreClientAPI api, EntityAgent entityPlayer, EntityShapeRenderer renderer, int color = ColorUtil.WhiteArgb)
    {
        //if (api.World.Player.Entity.EntityId == entityPlayer.EntityId) return;

        if (!HasOBBCollider) return;

        foreach (ShapeElementCollider collider in Colliders.Values)
        {
            if (!collider.HasRenderer)
            {
                collider.Renderer ??= renderer;
                collider.HasRenderer = true;
            }

#if DEBUG
            if (RenderColliders) collider.Render(api, entityPlayer, color);
#endif
        }
    }
    public bool Collide(Vector3 segmentStart, Vector3 segmentDirection, out int collider, out float parameter, out Vector3 intersection)
    {

        parameter = float.MaxValue;
        bool foundIntersection = false;
        collider = -1;
        intersection = Vector3.Zero;

        if (!HasOBBCollider)
        {
            CuboidAABBCollider AABBCollider = new(entity.CollisionBox);
            AABBCollider.Collide(segmentStart, segmentDirection, out parameter);
            intersection = segmentStart + parameter * segmentDirection;
            return true;
        }

        if (!BoundingBox.Collide(segmentStart, segmentDirection, out _))
        {
            return false;
        }

        foreach ((int key, ShapeElementCollider shapeElementCollider) in Colliders)
        {
            if (shapeElementCollider.Collide(segmentStart, segmentDirection, out float currentParameter, out Vector3 currentIntersection) && currentParameter < parameter)
            {
                parameter = currentParameter;
                collider = key;
                intersection = currentIntersection;
                foundIntersection = true;
            }
        }

        return foundIntersection;
    }

    public ColliderTypes GetColliderType(int colliderId) => CollidersTypes[CollidersIds[colliderId]];

    private void RecalculateColliders(ProceduralClientAnimator animator, ICoreClientAPI clientApi)
    {
        foreach ((_, ShapeElementCollider collider) in Colliders)
        {
            collider.Transform(animator.TransformationMatrices4x3, clientApi);
        }
        CalculateBoundingBox();
    }
    private void CalculateBoundingBox()
    {
        Vector3 min = new(float.MaxValue, float.MaxValue, float.MaxValue);
        Vector3 max = new(float.MinValue, float.MinValue, float.MinValue);

        foreach (ShapeElementCollider collider in Colliders.Values)
        {
            for (int vertex = 0; vertex < ShapeElementCollider.VertexCount; vertex++)
            {
                Vector4 inworldVertex = collider.InworldVertices[vertex];
                min.X = Math.Min(min.X, inworldVertex.X);
                min.Y = Math.Min(min.Y, inworldVertex.Y);
                min.Z = Math.Min(min.Z, inworldVertex.Z);
                max.X = Math.Max(max.X, inworldVertex.X);
                max.Y = Math.Max(max.Y, inworldVertex.Y);
                max.Z = Math.Max(max.Z, inworldVertex.Z);
            }
        }

        BoundingBox = new CuboidAABBCollider(min, max);

        /*entity.SelectionBox.X1 = min.X - (float)entity.Pos.X;
        entity.SelectionBox.Y1 = min.Y - (float)entity.Pos.Y;
        entity.SelectionBox.Z1 = min.Z - (float)entity.Pos.Z;
        entity.SelectionBox.X2 = max.X - (float)entity.Pos.X;
        entity.SelectionBox.Y2 = max.Y - (float)entity.Pos.Y;
        entity.SelectionBox.Z2 = max.Z - (float)entity.Pos.Z;*/
    }
}