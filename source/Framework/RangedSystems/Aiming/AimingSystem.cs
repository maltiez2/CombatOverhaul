using CombatOverhaul.Animations;
using CombatOverhaul.Integration;
using OpenTK.Mathematics;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

namespace CombatOverhaul.RangedSystems.Aiming;

public enum AimingCursorType
{
    None,
    Vanilla,
    Fixed,
    Moving
}

public class AimingStatsJson
{
    public float AimDifficulty { get; set; } = 1;
    public string CursorType { get; set; } = "Moving";
    public bool InvertMouseYAxis { get; set; } = false;

    public float VerticalAccuracyMultiplier { get; set; } = 1f;
    public float HorizontalAccuracyMultiplier { get; set; } = 1f;
    public float AimDriftFrequency { get; set; } = 0.001f;
    public float AimDrift { get; set; } = 150f;
    public float AimTwitch { get; set; } = 40f;
    public float HorizontalLimit { get; set; } = 0.125f;
    public float VerticalLimit { get; set; } = 0.35f;
    public float VerticalOffset { get; set; } = -0.15f;
    public int AimTwitchDuration { get; set; } = 300;

    public bool AllowSprint { get; set; } = true;
    public float MoveSpeedPenalty { get; set; } = 0f;
    public float AccuracyOvertimeSec { get; set; } = 6;
    public float AccuracyMovePenalty { get; set; } = 1f;

    public string CursorTextureReady { get; set; } = "";
    public string CursorTextureNotReady { get; set; } = "";

    public float AnimationFollowX { get; set; } = 0f;
    public float AnimationFollowY { get; set; } = 0f;
    public float AnimationOffsetX { get; set; } = 0f;
    public float AnimationOffsetY { get; set; } = 0f;

    public AimingStats ToStats()
    {
        return new AimingStats()
        {
            AimDifficulty = AimDifficulty,
            CursorType = Enum.Parse<AimingCursorType>(CursorType),
            InvertMouseYAxis = InvertMouseYAxis,
            VerticalAccuracyMultiplier = VerticalAccuracyMultiplier,
            HorizontalAccuracyMultiplier = HorizontalAccuracyMultiplier,
            AimDriftFrequency = AimDriftFrequency,
            AimDrift = AimDrift,
            AimTwitch = AimTwitch,
            HorizontalLimit = HorizontalLimit,
            VerticalLimit = VerticalLimit,
            VerticalOffset = VerticalOffset,
            AimTwitchDuration = AimTwitchDuration,
            AllowSprint = AllowSprint,
            MoveSpeedPenalty = MoveSpeedPenalty,
            AccuracyOvertime = TimeSpan.FromSeconds(AccuracyOvertimeSec),
            AccuracyMovePenalty = AccuracyMovePenalty,
            CursorTextureReady = CursorTextureReady,
            CursorTextureNotReady = CursorTextureNotReady,
            AnimationFollowX = AnimationFollowX,
            AnimationFollowY = AnimationFollowY,
            AnimationOffsetX = AnimationOffsetX,
            AnimationOffsetY = AnimationOffsetY
        };
    }
}

public class AimingStats
{
    public float AimDifficulty { get; set; } = 1;
    public AimingCursorType CursorType { get; set; } = AimingCursorType.Moving;
    public bool InvertMouseYAxis { get; set; } = false;

    public float VerticalAccuracyMultiplier { get; set; } = 1f;
    public float HorizontalAccuracyMultiplier { get; set; } = 1f;
    public float AimDriftFrequency { get; set; } = 0.001f;
    public float AimDrift { get; set; } = 150f;
    public float AimTwitch { get; set; } = 40f;
    public float HorizontalLimit { get; set; } = 0.125f;
    public float VerticalLimit { get; set; } = 0.35f;
    public float VerticalOffset { get; set; } = -0.15f;
    public int AimTwitchDuration { get; set; } = 300;

    public bool AllowSprint { get; set; } = true;
    public float MoveSpeedPenalty { get; set; } = 0f;
    public TimeSpan AccuracyOvertime { get; set; } = TimeSpan.FromSeconds(6);
    public float AccuracyMovePenalty { get; set; } = 1f;

    public string CursorTextureReady { get; set; } = "";
    public string CursorTextureNotReady { get; set; } = "";

    public float AnimationFollowX { get; set; } = 0f;
    public float AnimationFollowY { get; set; } = 0f;
    public float AnimationOffsetX { get; set; } = 0f;
    public float AnimationOffsetY { get; set; } = 0f;
}

public sealed class ClientAimingSystem : IDisposable
{
    public bool Aiming { get; set; } = false;
    public bool ShowVanillaReticle { get; private set; } = true;
    public bool ShowBullseyeReticle { get; private set; } = false;

    public Vector3 TargetVec { get; private set; } = new();
    public Vector2 Aim { get; private set; } = new();
    public Vector2 AimOffset { get; private set; } = new();
    public float DriftMultiplier { get; set; } = 1f;
    public float TwitchMultiplier { get; set; } = 1f;

    public WeaponAimingState AimingState
    {
        get => _reticleRenderer.AimingState;
        set => _reticleRenderer.AimingState = ShowBullseyeReticle ? value : WeaponAimingState.None;
    }

    public event Action? OnAimPointChange;

    public ClientAimingSystem(ICoreClientAPI api, ReticleRenderer renderer)
    {
        _clientApi = api;

        _unprojectionTool = new Unproject();
        ResetAim();

        _reticleRenderer = renderer;
        AimingPatches.UpdateCameraYawPitch += UpdateAimPoint;
    }
    public void StartAiming(AimingStats stats)
    {
        _aimingStats = stats;

        if (_clientApi.World.ElapsedMilliseconds - _lastAimingEndTime > _aimResetTime)
        {
            ResetAim();
        }
        else
        {
            ResetAimOffset();
        }

        if (_aimingStats.CursorType == AimingCursorType.Moving)
        {
            SetFixedAimPoint(_clientApi.Render.FrameWidth, _clientApi.Render.FrameHeight);
        }

        Aiming = true;
        _aimingDt = 0f;
        ShowVanillaReticle = _aimingStats.CursorType == AimingCursorType.Vanilla;
        ShowBullseyeReticle = _aimingStats.CursorType != AimingCursorType.None && _aimingStats.CursorType != AimingCursorType.Vanilla;
        _difficultyMultiplier = ShowBullseyeReticle ? 1 : _noCursorDifficultyMultiplier;

        _clientApi.World.Player.Entity.GetBehavior<AimingAccuracyBehavior>().StartAim(stats);

        _reticleRenderer.SetReticleTextures(stats.CursorTextureNotReady, stats.CursorTextureReady);
    }
    public void StopAiming()
    {
        Aiming = false;

        _lastAimingEndTime = _clientApi.World.ElapsedMilliseconds;

        ShowVanillaReticle = true;

        _reticleRenderer.AimingState = WeaponAimingState.None;

        _clientApi.World.Player.Entity.GetBehavior<AimingAccuracyBehavior>().StopAim();
    }
    public Vector2 GetCurrentAim()
    {
        float offsetMagnitude = Math.Clamp(_aimingStats.AimDifficulty, 0, 2) * _difficultyMultiplier;

        if (_clientApi.World.Player?.Entity != null)
        {
            offsetMagnitude /= GameMath.Max(_clientApi.World.Player.Entity.Stats.GetBlended("rangedWeaponsAcc"), 0.001f);
        }

        float interpolation = GameMath.Sqrt(GameMath.Min(_aimingDt / _aimStartInterpolationTime, 1f));

        _currentAim.X = (Aim.X + AimOffset.X * offsetMagnitude * _aimingStats.HorizontalAccuracyMultiplier) * interpolation;
        _currentAim.Y = (Aim.Y + AimOffset.Y * offsetMagnitude * _aimingStats.VerticalAccuracyMultiplier) * interpolation;

        return _currentAim;
    }
    public void ResetAim()
    {
        Aim = new(0, 0);

        ResetAimOffset();
    }

    // TODO: For a rewrite, consider switching aimX and aimY from pixels to % of screen width/height. That way it's consistent on all resolutions
    // (still will have to account for FoV though).
    public void UpdateAimPoint(ClientMain __instance,
            ref double ___MouseDeltaX, ref double ___MouseDeltaY,
            ref double ___DelayedMouseDeltaX, ref double ___DelayedMouseDeltaY,
            float dt)
    {
        AimingPatches.DrawDefaultReticle = ShowVanillaReticle;

        if (!Aiming) return;

        // Default FOV is 70, and 1920 is the screen width of my dev machine :) 
        _currentFovRatio = (__instance.Width / 1920f) * (GameMath.Tan((70f / 2 * GameMath.DEG2RAD)) / GameMath.Tan((FirstPersonAnimationsBehavior.CurrentFov / 2 * GameMath.DEG2RAD)));
        _aimingDt += dt;

        // Update
        UpdateAimOffsetSimple(__instance, dt);

        if (_aimingStats.CursorType == AimingCursorType.Moving)
        {
            UpdateMouseDelta(__instance, ref ___MouseDeltaX, ref ___MouseDeltaY, ref ___DelayedMouseDeltaX, ref ___DelayedMouseDeltaY);
        }

        SetAim();

        _reticleRenderer.AimingPoint = GetCurrentAim();

        OnAimPointChange?.Invoke();
    }

    public void Dispose()
    {
        AimingPatches.UpdateCameraYawPitch -= UpdateAimPoint;
    }

    public static Vector3 Zeroing(Vector3 direction, float ZeroingAngle)
    {
        Vector3 vertical = new(0, 1, 0);
        Vector3 zeroingAxis = Vector3.Cross(direction, vertical);

        double[] matrix = Mat4d.Create();
        Mat4d.Rotate(matrix, matrix, ZeroingAngle * GameMath.DEG2RAD, new double[] { zeroingAxis.X, zeroingAxis.Y, zeroingAxis.Z });
        double[] matrixVec = new double[] { direction.X, direction.Y, direction.Z, 0 };
        matrixVec = Mat4d.MulWithVec4(matrix, matrixVec);

        return new((float)matrixVec[0], (float)matrixVec[1], (float)matrixVec[2]);
    }


    private readonly NormalizedSimplexNoise _noiseGenerator = NormalizedSimplexNoise.FromDefaultOctaves(4, 1.0, 0.9, 123L);
    private readonly Random _random = new();
    private readonly ICoreClientAPI _clientApi;
    private readonly ReticleRenderer _reticleRenderer;
    private readonly Unproject _unprojectionTool;
    private AimingStats _aimingStats = new();
    private long _twitchLastChangeMilliseconds;
    private long _twitchLastStepMilliseconds;
    private Vector2 _twitch = new();
    private readonly double[] _viewport = new double[4];
    private readonly double[] _rayStart = new double[4];
    private readonly double[] _rayEnd = new double[4];
    private float _aimingDt;
    private long _lastAimingEndTime = 0;
    private const long _aimResetTime = 5000;
    private const float _aimStartInterpolationTime = 0.3f;
    private Vector2 _currentAim = new();
    private float _currentFovRatio;
    private float _difficultyMultiplier = 1;
    private const float _noCursorDifficultyMultiplier = 0.5f;


    private void ResetAimOffset()
    {
        AimOffset = new(0, 0);
        _twitch = new(0, 0);
        ShowVanillaReticle = true;
    }
    private void UpdateAimOffsetSimple(ClientMain __instance, float dt)
    {
        UpdateAimOffsetSimpleDrift(__instance, dt);
        UpdateAimOffsetSimpleTwitch(__instance, dt);
    }
    private void UpdateAimOffsetSimpleDrift(ClientMain __instance, float dt)
    {
        const float driftMaxRatio = 1.1f;

        float xNoise = ((float)_noiseGenerator.Noise(__instance.ElapsedMilliseconds * _aimingStats.AimDriftFrequency, 1000f) - 0.5f);
        float yNoise = ((float)_noiseGenerator.Noise(-1000f, __instance.ElapsedMilliseconds * _aimingStats.AimDriftFrequency) - 0.5f);

        float maxDrift = GameMath.Max(_aimingStats.AimDrift * driftMaxRatio * DriftMultiplier, 1f) * _currentFovRatio;

        float aimOffsetX = AimOffset.X + ((xNoise - AimOffset.X / maxDrift) * _aimingStats.AimDrift * DriftMultiplier * dt * _currentFovRatio);
        float aimOffsetY = AimOffset.Y + ((yNoise - AimOffset.Y / maxDrift) * _aimingStats.AimDrift * DriftMultiplier * dt * _currentFovRatio);

        AimOffset = new(aimOffsetX, aimOffsetY);
    }
    private void UpdateAimOffsetSimpleTwitch(ClientMain __instance, float dt)
    {
        // Don't ask me why aimOffset needs to be multiplied by fovRatio here, but not in the Drift function
        // Frankly the whole thing is up for a full rework anyway, but I don't want to get into that until I get started on crossbows and stuff
        float fovModAimOffsetX = AimOffset.X * _currentFovRatio;
        float fovModAimOffsetY = AimOffset.Y * _currentFovRatio;

        const float twitchMaxRatio = 1 / 7f;

        if (__instance.Api.World.ElapsedMilliseconds > _twitchLastChangeMilliseconds + _aimingStats.AimTwitchDuration)
        {
            _twitchLastChangeMilliseconds = __instance.Api.World.ElapsedMilliseconds;
            _twitchLastStepMilliseconds = __instance.Api.World.ElapsedMilliseconds;

            float twitchMax = GameMath.Max(_aimingStats.AimTwitch * twitchMaxRatio * TwitchMultiplier, 1f) * _currentFovRatio;

            _twitch.X = (((float)_random.NextDouble() - 0.5f) * 2f) * twitchMax - fovModAimOffsetX / twitchMax;
            _twitch.Y = (((float)_random.NextDouble() - 0.5f) * 2f) * twitchMax - fovModAimOffsetY / twitchMax;

            float twitchLength = GameMath.Max(GameMath.Sqrt(_twitch.X * _twitch.X + _twitch.Y * _twitch.Y), 1f);

            _twitch.X = _twitch.X / twitchLength;
            _twitch.Y = _twitch.Y / twitchLength;
        }

        float lastStep = (_twitchLastStepMilliseconds - _twitchLastChangeMilliseconds) / (float)_aimingStats.AimTwitchDuration;
        float currentStep = (__instance.Api.World.ElapsedMilliseconds - _twitchLastChangeMilliseconds) / (float)_aimingStats.AimTwitchDuration;

        float stepSize = ((1f - lastStep) * (1f - lastStep)) - ((1f - currentStep) * (1f - currentStep));

        float aimOffsetX = AimOffset.X + (_twitch.X * stepSize * (_aimingStats.AimTwitch * TwitchMultiplier * dt) * (_aimingStats.AimTwitchDuration / 20) * _currentFovRatio);
        float aimOffsetY = AimOffset.Y + (_twitch.Y * stepSize * (_aimingStats.AimTwitch * TwitchMultiplier * dt) * (_aimingStats.AimTwitchDuration / 20) * _currentFovRatio);

        AimOffset = new(aimOffsetX, aimOffsetY);

        _twitchLastStepMilliseconds = __instance.Api.World.ElapsedMilliseconds;
    }
    private void UpdateMouseDelta(ClientMain __instance,
            ref double ___MouseDeltaX, ref double ___MouseDeltaY,
            ref double ___DelayedMouseDeltaX, ref double ___DelayedMouseDeltaY)
    {
        float horizontalAimLimit = (__instance.Width / 2f) * _aimingStats.HorizontalLimit;
        float verticalAimLimit = (__instance.Height / 2f) * _aimingStats.VerticalLimit;
        float verticalAimOffset = (__instance.Height / 2f) * _aimingStats.VerticalOffset;

        float yInversionFactor = _aimingStats.InvertMouseYAxis ? -1 : 1;

        float deltaX = (float)(___MouseDeltaX - ___DelayedMouseDeltaX);
        float deltaY = (float)(___MouseDeltaY - ___DelayedMouseDeltaY) * yInversionFactor;

        float aimX = Aim.X;
        float aimY = Aim.Y;

        if (Math.Abs(aimX + deltaX) > horizontalAimLimit)
        {
            aimX = aimX > 0 ? horizontalAimLimit : -horizontalAimLimit;
        }
        else
        {
            aimX += deltaX;
            ___DelayedMouseDeltaX = ___MouseDeltaX;
        }

        if (Math.Abs(aimY + deltaY - verticalAimOffset) > verticalAimLimit)
        {
            aimY = (aimY > 0 ? verticalAimLimit : -verticalAimLimit) + verticalAimOffset;
        }
        else
        {
            aimY += deltaY;
            ___DelayedMouseDeltaY = ___MouseDeltaY;
        }

        Aim = new(aimX, aimY);
    }
    private void SetFixedAimPoint(int screenWidth, int screenHeight)
    {
        float difficultyModifier = GameMath.Clamp(_aimingStats.AimDifficulty, 0, 2) * _difficultyMultiplier;

        float horizontalLimit = GameMath.Min(_aimingStats.HorizontalLimit, 0.25f);
        float verticalLimit = GameMath.Min(_aimingStats.VerticalLimit, 0.25f);
        float verticalOffset = GameMath.Clamp(_aimingStats.VerticalOffset, -0.05f, 0.1f);

        float horizontalAimLimit = (screenWidth / 2f) * horizontalLimit;
        float verticalAimLimit = (screenHeight / 2f) * verticalLimit;
        float verticalAimOffset = (screenHeight / 2f) * verticalOffset;

        float maxHorizontalShift = horizontalAimLimit / 2.25f;
        float maxVerticalShift = verticalAimLimit / 2.25f;

        maxHorizontalShift = GameMath.Max(maxHorizontalShift, GameMath.Min(maxVerticalShift, horizontalAimLimit));
        maxVerticalShift = GameMath.Max(maxVerticalShift, GameMath.Min(maxHorizontalShift, verticalAimLimit));

        float horizontalCenter = GameMath.Clamp(Aim.X, -maxHorizontalShift, maxHorizontalShift);
        float verticalCenter = GameMath.Clamp(Aim.Y, -maxVerticalShift, maxVerticalShift);

        float aimX = (horizontalCenter + (-maxHorizontalShift + ((float)_random.NextDouble() * maxHorizontalShift * 2f))) * difficultyModifier;
        float aimY = (verticalCenter + (-maxVerticalShift + ((float)_random.NextDouble() * maxVerticalShift * 2f)) + verticalAimOffset) * difficultyModifier;

        Aim = new(aimX, aimY);
    }
    private void SetAim()
    {
        Vector2 currentAim = GetCurrentAim();

        int mouseCurrentX = (int)currentAim.X + _clientApi.Render.FrameWidth / 2;
        int mouseCurrentY = (int)currentAim.Y + _clientApi.Render.FrameHeight / 2;
        _viewport[0] = 0.0;
        _viewport[1] = 0.0;
        _viewport[2] = _clientApi.Render.FrameWidth;
        _viewport[3] = _clientApi.Render.FrameHeight;

        bool unprojectPassed = true;
        unprojectPassed |= _unprojectionTool.UnProject(mouseCurrentX, _clientApi.Render.FrameHeight - mouseCurrentY, 1, _clientApi.Render.MvMatrix.Top, _clientApi.Render.PMatrix.Top, _viewport, _rayEnd);
        unprojectPassed |= _unprojectionTool.UnProject(mouseCurrentX, _clientApi.Render.FrameHeight - mouseCurrentY, 0, _clientApi.Render.MvMatrix.Top, _clientApi.Render.PMatrix.Top, _viewport, _rayStart);

        // If unproject fails, well, not much we can do really. Try not to crash
        if (!unprojectPassed) return;

        double offsetX = _rayEnd[0] - _rayStart[0];
        double offsetY = _rayEnd[1] - _rayStart[1];
        double offsetZ = _rayEnd[2] - _rayStart[2];
        float length = (float)Math.Sqrt(offsetX * offsetX + offsetY * offsetY + offsetZ * offsetZ);

        // If length is *somehow* zero, just abort not to crash. The start and end of the ray are in the same place, what to even do in that situation?
        if (length == 0) return;

        offsetX /= length;
        offsetY /= length;
        offsetZ /= length;

        TargetVec = new((float)offsetX, (float)offsetY, (float)offsetZ);
    }
}