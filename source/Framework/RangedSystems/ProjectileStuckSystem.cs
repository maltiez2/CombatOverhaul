using CombatOverhaul.Colliders;
using ProtoBuf;
using System.Numerics;
using Vintagestory.API.Client;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace CombatOverhaul.RangedSystems;

public class ProjectileStuckSystemServer
{
    public ProjectileStuckSystemServer(ICoreServerAPI api)
    {
        _api = api;
        api.Network.RegisterChannel(NetworkChannelId)
            .RegisterMessageType<ProjectileStuckPacketPack>()
            .SetMessageHandler<ProjectileStuckPacketPack>(HandlePositionChange);
    }

    public const string NetworkChannelId = "CombatOverhaul:stuckSystem";

    private readonly ICoreServerAPI _api;
    private readonly Dictionary<long, (ProjectileEntity projectile, Entity target)> _stuckProjectiles = new();

    private void HandlePositionChange(IServerPlayer player, ProjectileStuckPacketPack pack)
    {
        foreach ((ProjectileEntity oldProjectile, Entity oldTarget) in _stuckProjectiles.Values)
        {
            if (!oldProjectile.Alive)
            {
                _stuckProjectiles.Remove(oldProjectile.EntityId);
            }
            else
            {
                if (!oldTarget.Alive)
                {
                    oldProjectile.Stuck = true;
                    oldProjectile.WatchedAttributes.SetBool("stuck", true);
                    _stuckProjectiles.Remove(oldProjectile.EntityId);
                }
            }
        }

        foreach (ProjectileStuckPacket packet in pack.Packets)
        {
            ProcessPacket(packet);
        }
    }

    private void ProcessPacket(ProjectileStuckPacket packet)
    {
        if (!_stuckProjectiles.ContainsKey(packet.ProjectileId))
        {
            if (_api.World.GetEntityById(packet.ProjectileId) is ProjectileEntity newProjectile)
            {
                Entity newTarget = _api.World.GetEntityById(packet.TargetId);
                if (newTarget.Alive && newProjectile.Alive)
                {
                    _stuckProjectiles.Add(packet.ProjectileId, (newProjectile, newTarget));
                }
            }
            else
            {
                return;
            }
        }

        if (!_stuckProjectiles.ContainsKey(packet.ProjectileId)) return;

        (ProjectileEntity projectile, Entity target) = _stuckProjectiles[packet.ProjectileId];

        projectile.ServerPos.SetPos(packet.Position[0], packet.Position[1], packet.Position[2]);
        projectile.ServerPos.SetAngles(packet.Roll, packet.Yaw, packet.Pitch);
        projectile.ServerPos.Motion.Set(target.ServerPos.Motion);
        projectile.Stuck = true;
        projectile.WatchedAttributes.SetBool("stuck", true);
    }
}

public class ProjectileStuckSystemClient
{
    public ProjectileStuckSystemClient(ICoreClientAPI api)
    {
        _api = api;
        _clientChannel = api.Network.RegisterChannel(NetworkChannelId)
            .RegisterMessageType<ProjectileStuckPacketPack>();

        _listener = api.World.RegisterGameTickListener(_ => RecalculateStuckProjectiles(), 30);
    }

    public void Stuck(ProjectileEntity projectile, Entity target, string collider, Vector3 collisionPoint)
    {
        if (_controllers.ContainsKey(projectile.EntityId)) return;

        _controllers.Add(projectile.EntityId, new(projectile, target, collider, collisionPoint));
    }
    
    public void UnregisterTickListener()
    {
        _api.World.UnregisterGameTickListener(_listener);
    }

    public const string NetworkChannelId = "CombatOverhaul:stuckSystem";

    private readonly ICoreClientAPI _api;
    private readonly IClientNetworkChannel _clientChannel;
    private readonly Dictionary<long, StuckController> _controllers = new();
    private readonly long _listener;

    private void RecalculateStuckProjectiles()
    {
        foreach (long id in _controllers.Where(entry => !entry.Value.BothEntitiesAlive()).Select(entry => entry.Key))
        {
            _controllers.Remove(id);
        }

        List<ProjectileStuckPacket> packets = new();
        foreach (StuckController controller in _controllers.Values)
        {
            packets.Add(controller.RecalculatePosition());
        }

        ProjectileStuckPacketPack pack = new() { Packets = packets.ToArray() };
        _clientChannel.SendPacket(pack);
    }
}

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public class ProjectileStuckPacketPack
{
    public ProjectileStuckPacket[] Packets { get; set; } = Array.Empty<ProjectileStuckPacket>();
}

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public class ProjectileStuckPacket
{
    public long ProjectileId { get; set; }
    public long TargetId { get; set; }
    public float[] Position { get; set; } = Array.Empty<float>();
    public float Yaw { get; set; }
    public float Pitch { get; set; }
    public float Roll { get; set; }
}

public readonly struct StuckController
{
    public StuckController(ProjectileEntity projectile, Entity target, string collider, Vector3 position)
    {
        _target = target;
        _projectile = projectile;

        _initialPosition = new(position.X, position.Y, position.Z);
        _initialYaw = projectile.ServerPos.Yaw;
        _initialPitch = projectile.ServerPos.Pitch;
        _initialRoll = projectile.ServerPos.Roll;

        CollidersEntityBehavior colliders = target.GetBehavior<CollidersEntityBehavior>();

        _collider = colliders.Colliders[collider];
        Vector4 vertex0 = _collider.InworldVertices[0];
        _initialColliderVertex0 = new(vertex0.X, vertex0.Y, vertex0.Z);
        Vector4 vertex1 = _collider.InworldVertices[1];
        _initialColliderVertex1 = new(vertex1.X, vertex1.Y, vertex1.Z);
    }

    public ProjectileStuckPacket RecalculatePosition()
    {
        Vector4 vertex0 = _collider.InworldVertices[0];
        Vector3 newVertex0 = new(vertex0.X, vertex0.Y, vertex0.Z);
        Vector4 vertex1 = _collider.InworldVertices[1];
        Vector3 newVertex1 = new(vertex1.X, vertex1.Y, vertex1.Z);

        Matrix4x4 translation = CalculateTranslationMatrix(_initialColliderVertex0, newVertex0);
        Matrix4x4 rotation = CalculateRotationMatrix(_initialColliderVertex1 - _initialColliderVertex0, newVertex1 - newVertex0);

        Matrix4x4 transform = rotation * translation;

        Vector3 newPosition = Vector3.Transform(_initialPosition, translation);

        Matrix4x4 initialRotation = Matrix4x4.CreateFromYawPitchRoll(_initialYaw, _initialPitch, _initialRoll);
        Matrix4x4 entityTransform = initialRotation * rotation;

        ExtractYawPitchRollFromMatrix(entityTransform, out float newYaw, out float newPitch, out float newRoll);



        //ExtractYawPitchRollFromMatrix(CreateMatrixFromYawPitchRoll(_initialYaw, _initialPitch, _initialRoll), out float y, out float p, out float r);
        //Console.WriteLine($"Y: {(y - _initialYaw) * GameMath.RAD2DEG:F2}, P: {(p - _initialPitch) * GameMath.RAD2DEG:F2}, R: {(r - _initialRoll) * GameMath.RAD2DEG:F2}");

        _projectile.ServerPos.SetPos(newPosition.X, newPosition.Y, newPosition.Z);
        _projectile.ServerPos.SetAngles(newRoll, newYaw, newPitch);
        _projectile.ServerPos.Motion.Set(_target.ServerPos.Motion);

        //Console.WriteLine($"new pos: {newPosition}, target pos: {_target.Pos}");
        //Console.WriteLine($"Yaw: {_initialYaw * GameMath.RAD2DEG} -> {newYaw * GameMath.RAD2DEG}");
        //Console.WriteLine($"Pitch: {_initialPitch * GameMath.RAD2DEG} -> {newPitch * GameMath.RAD2DEG}");
        //Console.WriteLine($"Roll: {_initialRoll * GameMath.RAD2DEG} -> {newRoll * GameMath.RAD2DEG}");

        return new()
        {
            ProjectileId = _projectile.EntityId,
            TargetId = _target.EntityId,
            Position = new float[3] { newPosition.X, newPosition.Y, newPosition.Z },
            Yaw = _initialYaw,
            Pitch = _initialPitch,
            Roll = _initialRoll
        };
    }

    public bool BothEntitiesAlive() => _target.Alive && _projectile.Alive;

    private readonly Vector3 _initialPosition;
    private readonly float _initialYaw;
    private readonly float _initialPitch;
    private readonly float _initialRoll;
    private readonly Vector3 _initialColliderVertex0;
    private readonly Vector3 _initialColliderVertex1;

    private readonly Entity _target;
    private readonly ProjectileEntity _projectile;
    private readonly ShapeElementCollider _collider;

    private static Matrix4x4 CalculateTranslationMatrix(Vector3 before, Vector3 after)
    {
        Vector3 translation = after - before;
        return Matrix4x4.CreateTranslation(translation);
    }
    private static Matrix4x4 CalculateRotationMatrix(Vector3 beforeDirection, Vector3 afterDirection)
    {
        beforeDirection = Vector3.Normalize(beforeDirection);
        afterDirection = Vector3.Normalize(afterDirection);

        Vector3 axis = Vector3.Cross(beforeDirection, afterDirection);
        float angle = (float)Math.Acos(Vector3.Dot(beforeDirection, afterDirection));  // Angle between the two vectors

        if (axis.Length() == 0)
        {
            // If vectors are collinear, no rotation is needed
            return Matrix4x4.Identity;
        }

        axis = Vector3.Normalize(axis);
        return Matrix4x4.CreateFromAxisAngle(axis, angle);  // Rotation matrix around axis by the calculated angle
    }
    public static void ExtractEulerAngles(Matrix4x4 matrix, out float yaw, out float pitch, out float roll)
    {
        // Extract rotation part (upper-left 3x3 matrix)
        float m11 = matrix.M11, m12 = matrix.M12, m13 = matrix.M13;
        float m21 = matrix.M21, m22 = matrix.M22, m23 = matrix.M23;
        float m31 = matrix.M31, m32 = matrix.M32, m33 = matrix.M33;

        // Calculate pitch (rotation around Y-axis)
        pitch = (float)Math.Asin(-m32);

        // Check for gimbal lock case (when cos(pitch) is close to 0)
        if (Math.Cos(pitch) > 0.001)
        {
            // Calculate yaw (rotation around Z-axis)
            yaw = (float)Math.Atan2(m21, m11);

            // Calculate roll (rotation around X-axis)
            roll = (float)Math.Atan2(m31, m33);
        }
        else
        {
            // Gimbal lock case: set roll to 0 and calculate yaw differently
            roll = 0;
            yaw = (float)Math.Atan2(-m12, m22);
        }
    }
    public static void ExtractEulerAngles2(Matrix4x4 m, out float yaw, out float pitch, out float roll)
    {
        if (m.M31 != 1 && m.M31 != -1)
        {
            pitch = -MathF.Asin(m.M31); // θ (pitch)
            roll = MathF.Atan2(m.M32 / MathF.Cos(pitch), m.M33 / MathF.Cos(pitch)); // φ (roll)
            yaw = MathF.Atan2(m.M21 / MathF.Cos(pitch), m.M11 / MathF.Cos(pitch)); // ψ (yaw)
        }
        else
        {
            // Gimbal lock case
            yaw = 0; // Assigning yaw to zero
            if (m.M31 == -1)
            {
                pitch = MathF.PI / 2;
                roll = yaw + MathF.Atan2(m.M12, m.M13);
            }
            else
            {
                pitch = -MathF.PI / 2;
                roll = -yaw + MathF.Atan2(-m.M12, -m.M13);
            }
        }
    }

    public static Matrix4x4 CreateMatrixFromYawPitchRoll(float yaw, float pitch, float roll)
    {
        // Compute the individual rotation matrices for each axis
        Matrix4x4 rollMatrix = Matrix4x4.CreateRotationX((float)roll);
        Matrix4x4 pitchMatrix = Matrix4x4.CreateRotationY((float)pitch);
        Matrix4x4 yawMatrix = Matrix4x4.CreateRotationZ((float)yaw);

        // Combine the rotation matrices: Yaw * Pitch * Roll
        Matrix4x4 rotationMatrix = yawMatrix * pitchMatrix * rollMatrix;

        return rotationMatrix;
    }

    // Function to extract yaw, pitch, and roll from a Matrix4x4
    public static void ExtractYawPitchRollFromMatrix(Matrix4x4 m, out float yaw, out float pitch, out float roll)
    {
        // Check for gimbal lock (when M31 is 1 or -1)
        if (m.M31 < 1 && m.M31 > -1)
        {
            // General case
            pitch = MathF.Asin(-m.M31); // θ (pitch)

            // Roll and yaw can be calculated using atan2 for stability
            roll = MathF.Atan2(m.M32, m.M33); // φ (roll)
            yaw = MathF.Atan2(m.M21, m.M11);  // ψ (yaw)
        }
        else
        {
            // Gimbal lock case
            pitch = m.M31 == -1 ? MathF.PI / 2 : -MathF.PI / 2; // Set pitch to ±90 degrees

            // Yaw and roll are related in gimbal lock, so set roll to 0 and compute yaw
            yaw = MathF.Atan2(m.M12, m.M22);
            roll = 0;
        }
    }
}

