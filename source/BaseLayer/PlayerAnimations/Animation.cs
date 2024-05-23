namespace CombatOverhaul.PlayerAnimations;

internal enum ShapeElements
{
    ItemAnchor,
    LowerArmR,
    UpperArmR,
    ItemAnchorL,
    LowerArmL,
    UpperArmL
}

internal enum AnimationType
{
    RightHand,
    LeftHand,
    TwoHanded
}

internal class Animation
{
    private List<(IFrame frame, TimeSpan easingTime, EasingFunctionType easingFunction)> _keyFrames = new();
}

internal interface IFrame
{

};

internal readonly struct TwoHandsFrame
{
    public readonly RightHandFrame RightHand;
    public readonly LeftHandFrame LeftHand;

    public TwoHandsFrame(RightHandFrame right, LeftHandFrame left)
    {
        RightHand = right;
        LeftHand = left;
    }

    public static TwoHandsFrame Interpolate(TwoHandsFrame from, TwoHandsFrame to, float progress)
    {
        return new(
            RightHandFrame.Interpolate(from.RightHand, to.RightHand, progress),
            LeftHandFrame.Interpolate(from.LeftHand, to.LeftHand, progress)
            );
    }
}

internal readonly struct RightHandFrame
{
    public readonly AnimationElement ItemAnchor;
    public readonly AnimationElement LowerArmR;
    public readonly AnimationElement UpperArmR;

    public RightHandFrame(AnimationElement anchor, AnimationElement lower, AnimationElement upper)
    {
        ItemAnchor = anchor;
        LowerArmR = lower;
        UpperArmR = upper;
    }

    public static RightHandFrame Interpolate(RightHandFrame from, RightHandFrame to, float progress)
    {
        return new(
            AnimationElement.Interpolate(from.ItemAnchor, to.ItemAnchor, progress),
            AnimationElement.Interpolate(from.LowerArmR, to.LowerArmR, progress),
            AnimationElement.Interpolate(from.UpperArmR, to.UpperArmR, progress)
            );
    }
}

internal readonly struct LeftHandFrame
{
    public readonly AnimationElement ItemAnchorL;
    public readonly AnimationElement LowerArmL;
    public readonly AnimationElement UpperArmL;

    public LeftHandFrame(AnimationElement anchor, AnimationElement lower, AnimationElement upper)
    {
        ItemAnchorL = anchor;
        LowerArmL = lower;
        UpperArmL = upper;
    }

    public static LeftHandFrame Interpolate(LeftHandFrame from, LeftHandFrame to, float progress)
    {
        return new(
            AnimationElement.Interpolate(from.ItemAnchorL, to.ItemAnchorL, progress),
            AnimationElement.Interpolate(from.LowerArmL, to.LowerArmL, progress),
            AnimationElement.Interpolate(from.UpperArmL, to.UpperArmL, progress)
            );
    }
}

internal readonly struct AnimationElement
{
    public readonly float OffsetX;
    public readonly float OffsetY;
    public readonly float OffsetZ;
    public readonly float RotationX;
    public readonly float RotationY;
    public readonly float RotationZ;

    public AnimationElement(float[] values)
    {
        OffsetX = values[0];
        OffsetY = values[1];
        OffsetZ = values[2];
        RotationX = values[3];
        RotationY = values[4];
        RotationZ = values[5];
    }
    public AnimationElement(float offsetX, float offsetY, float offsetZ, float rotationX, float rotationY, float rotationZ)
    {
        OffsetX = offsetX;
        OffsetY = offsetY;
        OffsetZ = offsetZ;
        RotationX = rotationX;
        RotationY = rotationY;
        RotationZ = rotationZ;
    }

    public static AnimationElement Interpolate(AnimationElement from, AnimationElement to, float progress)
    {
        return new(
            from.OffsetX + (to.OffsetX - from.OffsetX) * progress,
            from.OffsetY + (to.OffsetY - from.OffsetY) * progress,
            from.OffsetZ + (to.OffsetZ - from.OffsetZ) * progress,
            from.RotationX + (to.RotationX - from.RotationX) * progress,
            from.RotationY + (to.RotationY - from.RotationY) * progress,
            from.RotationZ + (to.RotationZ - from.RotationZ) * progress
            );
    }
}