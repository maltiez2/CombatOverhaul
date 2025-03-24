using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace CombatOverhaul.Inputs;

public enum DirectionsConfiguration
{
    None = 1,
    TopBottom = 2,
    Triangle = 3,
    Square = 4,
    Star = 5,
    Eight = 8
}

public enum AttackDirection
{
    Top,
    TopRight,
    Right,
    BottomRight,
    Bottom,
    BottomLeft,
    Left,
    TopLeft
}

public readonly struct MouseMovementData
{
    public float Pitch { get; }
    public float Yaw { get; }
    public float DeltaPitch { get; }
    public float DeltaYaw { get; }

    public MouseMovementData(float pitch, float yaw, float deltaPitch, float deltaYaw)
    {
        Pitch = pitch;
        Yaw = yaw;
        DeltaPitch = deltaPitch;
        DeltaYaw = deltaYaw;
    }
}

public sealed class DirectionController
{
    public DirectionsConfiguration DirectionsConfiguration { get; set; } = DirectionsConfiguration.Eight;
    public int Depth { get; set; } = 5;
    public float Sensitivity { get; set; } = 1.0f;
    public bool Invert { get; set; } = false;
    public AttackDirection CurrentDirection { get; private set; }
    public AttackDirection CurrentDirectionWithInversion => Invert ? _inversionMapping[CurrentDirection] : CurrentDirection;
    public int CurrentDirectionNormalized { get; private set; }

    public static readonly Dictionary<DirectionsConfiguration, List<int>> Configurations = new()
    {
        { DirectionsConfiguration.TopBottom, new() {0, 4} },
        { DirectionsConfiguration.Triangle, new() {0, 3, 5} },
        { DirectionsConfiguration.Square, new() {0, 2, 4, 6} },
        { DirectionsConfiguration.Star, new() {0, 1, 3, 5, 7} },
        { DirectionsConfiguration.Eight, new() {0, 1, 2, 3, 4, 5, 6, 7} }
    };

    public DirectionController(ICoreClientAPI api, DirectionCursorRenderer renderer)
    {
        _api = api;
        _directionCursorRenderer = renderer;

        for (int count = 0; count < Depth * 2; count++)
        {
            _directionQueue.Enqueue(new(0, 0, 0, 0));
        }

        ConstructInvertedConfigurations();
    }

    public void OnGameTick(bool forceNewDirection = false)
    {
        if (DirectionsConfiguration == 0)
        {
            DirectionsConfiguration = DirectionsConfiguration.None;
        }

        if (DirectionsConfiguration == DirectionsConfiguration.None)
        {
            _directionCursorRenderer.Show = false;
            return;
        }

        _directionCursorRenderer.Show = true;

        float pitch = _api.Input.MousePitch;
        float yaw = _api.Input.MouseYaw;

        _directionQueue.Enqueue(new(pitch, yaw, pitch - _directionQueue.Last().Pitch, yaw - _directionQueue.Last().Yaw));

        MouseMovementData previous = _directionQueue.Dequeue();

        int direction = CalculateDirectionWithInversion(previous.Yaw - yaw, previous.Pitch - pitch, (int)DirectionsConfiguration);

        float delta = _directionQueue.Last().DeltaPitch * _directionQueue.Last().DeltaPitch + _directionQueue.Last().DeltaYaw * _directionQueue.Last().DeltaYaw;

        if (forceNewDirection || delta > _sensitivityFactor / Sensitivity)
        {
            CurrentDirectionNormalized = direction;
            CurrentDirection = (AttackDirection)Configurations[DirectionsConfiguration][CurrentDirectionNormalized];
            _directionCursorRenderer.CurrentDirection = (int)CurrentDirectionWithInversion;
        }
    }


    private const float _sensitivityFactor = 1e-5f;
    private readonly ICoreClientAPI _api;
    private readonly Queue<MouseMovementData> _directionQueue = new();
    private readonly DirectionCursorRenderer _directionCursorRenderer;
    
    private readonly Dictionary<DirectionsConfiguration, List<int>> _invertedConfigurations = new();
    private readonly Dictionary<AttackDirection, AttackDirection> _inversionMapping = new()
    {
        { AttackDirection.Top, AttackDirection.Bottom },
        { AttackDirection.TopRight, AttackDirection.BottomLeft },
        { AttackDirection.Right, AttackDirection.Left },
        { AttackDirection.BottomRight, AttackDirection.TopLeft },
        { AttackDirection.Bottom, AttackDirection.Top },
        { AttackDirection.BottomLeft, AttackDirection.TopRight },
        { AttackDirection.Left, AttackDirection.Right },
        { AttackDirection.TopLeft, AttackDirection.BottomRight }
    };

    private int CalculateDirection(float yaw, float pitch, int directionsCount)
    {
        float angleSegment = 360f / directionsCount;
        float directionOffset = angleSegment / 2f;
        float angle = MathF.Atan2(yaw, pitch) * GameMath.RAD2DEG;
        float angleOffset = angle + directionOffset + 360;
        return (int)(angleOffset / angleSegment) % directionsCount;
    }

    private int CalculateDirectionWithInversion(float yaw, float pitch, int directionsCount) => CalculateDirection(Invert ? -yaw : yaw, Invert ? -pitch : pitch, directionsCount);

    private void ConstructInvertedConfigurations()
    {
        foreach ((DirectionsConfiguration configuration, List<int> directions) in Configurations)
        {
            _invertedConfigurations.Add(configuration, new());
            foreach (int direction in directions)
            {
                _invertedConfigurations[configuration].Add((int)_inversionMapping[(AttackDirection)direction]);
            }
        }
    }
}
