using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace CombatOverhaul.Integration;

internal static class EyeHightController
{
    public static float Amplitude { get; set; } = 1.0f;
    public static float Frequency { get; set; } = 1.0f;
    public static float SprintFrequencyEffect { get; set; } = 1.0f;
    public static float SprintAmplitudeEffect { get; set; } = 1.0f;
    public static float SneakEffect { get; set; } = 1.0f;
    public static float Offset { get; set; } = 1.0f;
    public static float LiquidEffect { get; set; } = 1.0f;

    public static bool UpdateEyeHeight(EntityPlayer __instance, float dt)
    {
        EntityControls? servercontrols = (EntityControls?)_servercontrols?.GetValue(__instance);
        float? secondsDead = (float?)_secondsDead?.GetValue(__instance);

        IPlayer player = __instance.World.PlayerByUid(__instance.PlayerUID);

        if (__instance.World.Side != EnumAppSide.Client) return false;

        __instance.PrevFrameCanStandUp = true;

        if (player?.WorldData?.CurrentGameMode == null || player.WorldData?.CurrentGameMode == EnumGameMode.Spectator) return false;

        EntityControls? controls = __instance.MountedOn != null ? __instance.MountedOn.Controls : servercontrols;

        if (controls == null) return true;

        CalculateCollisionBox(__instance, dt, controls);

        bool moving = (controls.TriesToMove && __instance.SidedPos.Motion.LengthSq() > 0.00001) && !controls.NoClip && !controls.DetachedMode;
        bool walking = moving && __instance.OnGround;

        __instance.LocalEyePos.X = 0;
        __instance.LocalEyePos.Z = 0;

        if (__instance.MountedOn?.LocalEyePos != null)
        {
            __instance.LocalEyePos.Set(__instance.MountedOn.LocalEyePos);
        }

        // Immersive fp mode has its own way of setting the eye pos
        // but we still need to run above non-ifp code for the hitbox
        if (player.ImmersiveFpMode || !__instance.Alive)
        {
            float newSecondsDead = __instance.Alive ? 0 : secondsDead.Value + dt;
            _secondsDead?.SetValue(__instance, newSecondsDead);
            UpdateLocalEyePosImmersiveFpMode(__instance);
        }

        double stepHeight = CalculateStepHeight(__instance, dt, controls, walking);

        ICoreClientAPI? clientApi = __instance.World.Api as ICoreClientAPI;
        if (clientApi?.Settings.Bool["viewBobbing"] == true && clientApi.Render.CameraType == EnumCameraMode.FirstPerson)
        {
            __instance.LocalEyePos.Y += stepHeight / 3f * dt * 60f;
        }

        PlayStepSound(__instance, player, moving, walking, stepHeight);

        _prevStepHeight?.SetValue(__instance, stepHeight);

        return false;
    }

    private static readonly FieldInfo? _tmpCollBox = typeof(Vintagestory.API.Common.EntityPlayer).GetField("tmpCollBox", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo? _holdPosition = typeof(Vintagestory.API.Common.EntityPlayer).GetField("holdPosition", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo? _secondsDead = typeof(Vintagestory.API.Common.EntityPlayer).GetField("secondsDead", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo? _prevAnimModelMatrix = typeof(Vintagestory.API.Common.EntityPlayer).GetField("prevAnimModelMatrix", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo? _walkCounter = typeof(Vintagestory.API.Common.EntityPlayer).GetField("walkCounter", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo? _direction = typeof(Vintagestory.API.Common.EntityPlayer).GetField("direction", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo? _prevStepHeight = typeof(Vintagestory.API.Common.EntityPlayer).GetField("prevStepHeight", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo? _servercontrols = typeof(Vintagestory.API.Common.EntityAgent).GetField("servercontrols", BindingFlags.NonPublic | BindingFlags.Instance);

    private static bool CanStandUp(EntityPlayer __instance)
    {
        Cuboidf? tmpCollBox = (Cuboidf?)_tmpCollBox?.GetValue(__instance);
        if (tmpCollBox == null) return false;

        tmpCollBox.Set(__instance.SelectionBox);
        bool flag = __instance.World.CollisionTester.IsColliding(__instance.World.BlockAccessor, tmpCollBox, __instance.Pos.XYZ, alsoCheckTouch: false);
        tmpCollBox.Y2 = __instance.Properties.CollisionBoxSize.Y;
        tmpCollBox.Y1 += 1f;
        return !__instance.World.CollisionTester.IsColliding(__instance.World.BlockAccessor, tmpCollBox, __instance.Pos.XYZ, alsoCheckTouch: false) || flag;
    }
    private static void UpdateLocalEyePosImmersiveFpMode(EntityPlayer __instance)
    {
        if (__instance.AnimManager.Animator == null) return;

        bool? holdPosition = (bool?)_holdPosition?.GetValue(__instance);
        float? secondsDead = (float?)_secondsDead?.GetValue(__instance);
        float[]? prevAnimModelMatrix = (float[]?)_prevAnimModelMatrix?.GetValue(__instance);
        float sidewaysSwivelAngle = __instance.sidewaysSwivelAngle;

        AttachmentPointAndPose apap = __instance.AnimManager.Animator.GetAttachmentPointPose("Eyes");
        AttachmentPoint ap = apap.AttachPoint;

        float[] ModelMat = Mat4f.Create();
        Matrixf tmpModelMat = new();

        float bodyYaw = __instance.BodyYaw;
        float rotX = __instance.Properties.Client.Shape != null ? __instance.Properties.Client.Shape.rotateX : 0;
        float rotY = __instance.Properties.Client.Shape != null ? __instance.Properties.Client.Shape.rotateY : 0;
        float rotZ = __instance.Properties.Client.Shape != null ? __instance.Properties.Client.Shape.rotateZ : 0;
        float bodyPitch = __instance.WalkPitch;

        float lookOffset = (__instance.SidedPos.Pitch - GameMath.PI) / 9f;
        if (!__instance.Alive) lookOffset /= secondsDead.Value * 10;

        bool wasHoldPos = holdPosition.Value;
        _holdPosition?.SetValue(__instance, false);

        for (int i = 0; i < __instance.AnimManager.Animator.RunningAnimations.Length; i++)
        {
            RunningAnimation anim = __instance.AnimManager.Animator.RunningAnimations[i];
            if (anim.Running && anim.EasingFactor >= anim.meta.HoldEyePosAfterEasein)
            {
                if (!wasHoldPos)
                {
                    _prevAnimModelMatrix?.SetValue(__instance, (float[])apap.AnimModelMatrix.Clone());
                }
                _holdPosition?.SetValue(__instance, true);
                break;
            }
        }

        tmpModelMat
            .Set(ModelMat)
            .RotateX(__instance.SidedPos.Roll + rotX * GameMath.DEG2RAD)
            .RotateY(bodyYaw + (180 + rotY) * GameMath.DEG2RAD)
            .RotateZ(bodyPitch + rotZ * GameMath.DEG2RAD)
            .Scale(__instance.Properties.Client.Size, __instance.Properties.Client.Size, __instance.Properties.Client.Size)
            .Translate(-0.5f, 0, -0.5f)
            .RotateX(sidewaysSwivelAngle)
            .Translate(ap.PosX / 16f - lookOffset * 1.3f, ap.PosY / 16f, ap.PosZ / 16f)
            .Mul(holdPosition.Value ? prevAnimModelMatrix : apap.AnimModelMatrix)
            .Translate(0.07f, __instance.Alive ? 0.0f : 0.2f * Math.Min(1, secondsDead.Value), 0f)
        ;

        float[] pos = new float[4] { 0, 0, 0, 1 };
        float[] endVec = Mat4f.MulWithVec4(tmpModelMat.Values, pos);

        __instance.LocalEyePos.Set(endVec[0], endVec[1], endVec[2]);
    }
    private static double CalculateStepHeight(EntityPlayer __instance, float dt, EntityControls controls, bool walking)
    {
        double? walkCounter = (double?)_walkCounter?.GetValue(__instance);

        double frequency = Frequency * dt * controls.MovespeedMultiplier * __instance.GetWalkSpeedMultiplier(0.3) * (controls.Sprint ? 0.9 * SprintFrequencyEffect : 1.2) * (controls.Sneak ? 1.2f : 1);

        walkCounter = walking ? walkCounter.Value + frequency : 0;
        walkCounter = walkCounter.Value % GameMath.TWOPI;
        _walkCounter?.SetValue(__instance, walkCounter.Value);

        double sneakDiv = controls.Sneak ? 5 * SneakEffect : 1.8;
        double sprint = controls.Sprint ? 0.07 * SprintAmplitudeEffect : 0;
        double amplitude = (__instance.FeetInLiquid ? 0.8 * LiquidEffect : 1 + sprint) / (3 * sneakDiv) * Amplitude;
        double offset = -0.2 / sneakDiv * Offset;

        double stepHeight = -Math.Max(0, Math.Abs(GameMath.Sin(5.5f * walkCounter.Value) * amplitude) + offset);

        return stepHeight;
    }
    private static void CalculateCollisionBox(EntityPlayer __instance, float dt, EntityControls controls)
    {
        __instance.PrevFrameCanStandUp = !controls.Sneak && CanStandUp(__instance);

        double newEyeheight = __instance.Properties.EyeHeight;
        double newModelHeight = __instance.Properties.CollisionBoxSize.Y;

        if (controls.FloorSitting)
        {
            newEyeheight *= 0.5f;
            newModelHeight *= 0.55f;
        }
        else if ((controls.Sneak || !__instance.PrevFrameCanStandUp) && !controls.IsClimbing && !controls.IsFlying)
        {
            newEyeheight *= 0.8f;
            newModelHeight *= 0.8f;
        }
        else if (!__instance.Alive)
        {
            newEyeheight *= 0.25f;
            newModelHeight *= 0.25f;
        }

        double diff = (newEyeheight - __instance.LocalEyePos.Y) * 5 * dt;
        __instance.LocalEyePos.Y = diff > 0 ? Math.Min(__instance.LocalEyePos.Y + diff, newEyeheight) : Math.Max(__instance.LocalEyePos.Y + diff, newEyeheight);

        diff = (newModelHeight - __instance.OriginSelectionBox.Y2) * 5 * dt;
        __instance.OriginSelectionBox.Y2 = __instance.SelectionBox.Y2 = (float)(diff > 0 ? Math.Min(__instance.SelectionBox.Y2 + diff, newModelHeight) : Math.Max(__instance.SelectionBox.Y2 + diff, newModelHeight));

        diff = (newModelHeight - __instance.OriginCollisionBox.Y2) * 5 * dt;
        __instance.OriginCollisionBox.Y2 = __instance.CollisionBox.Y2 = (float)(diff > 0 ? Math.Min(__instance.CollisionBox.Y2 + diff, newModelHeight) : Math.Max(__instance.CollisionBox.Y2 + diff, newModelHeight));
    }
    private static void PlayStepSound(EntityPlayer __instance, IPlayer player, bool moving, bool walking, double stepHeight)
    {
        if (!walking || !moving) return;

        double? prevStepHeight = (double?)_prevStepHeight?.GetValue(__instance);
        bool movingDown = stepHeight <= prevStepHeight.Value;

        int? direction = (int?)_direction?.GetValue(__instance);

        if (movingDown)
        {
            _direction.SetValue(__instance, 1);
        }
        else if (direction.Value != -1)
        {
            __instance.PlayStepSound(player, __instance.PlayInsideSound(player));
            _direction.SetValue(__instance, -1);
        }
    }
}