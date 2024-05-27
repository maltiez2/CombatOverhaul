using CombatOverhaul.Integration;
using System.Collections.Immutable;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace CombatOverhaul.PlayerAnimations;

public sealed class FirstPersonAnimationsBehavior : EntityBehavior
{
    public FirstPersonAnimationsBehavior(Entity entity) : base(entity)
    {
        if (entity is not EntityPlayer player) throw new ArgumentException("Only for players");
        _player = player;

        AnimatorPatch.OnBeforeFrame += OnBeforeFrame;
        AnimatorPatch.OnFrame += OnFrame;
    }

    public override string PropertyName() => "FirstPersonAnimations";


    private readonly Composer _composer = new();
    private readonly EntityPlayer _player;
    private Frame _lastFrame = Frame.Empty;

    private void OnBeforeFrame(Entity entity, float dt)
    {
        if (entity.EntityId != _player.EntityId) return;
        _lastFrame = _composer.Compose(TimeSpan.FromSeconds(dt));
    }

    private void OnFrame(Entity entity, ElementPose pose)
    {
        if (entity.EntityId != _player.EntityId) return;
        _lastFrame.Apply(pose);
    }
}

internal enum ShapeElements
{
    ItemAnchor,
    LowerArmR,
    UpperArmR,
    ItemAnchorL,
    LowerArmL,
    UpperArmL
}

public enum AnimationType
{
    RightHand,
    LeftHand,
    TwoHanded
}

internal sealed class Composer
{
    public Composer()
    {

    }

    public Frame Compose(TimeSpan delta)
    {
        if (!_requests.Any()) return Frame.Empty;
        
        foreach ((string category, AnimatorWeightState state) in _weightState)
        {
            _currentTimes.Add(category, delta);

            ProcessWeight(category, state);
        }

        Frame result = Frame.Compose(_animators.Select(entry => (entry.Value.Animate(delta), _currentWeight[entry.Key])));

        foreach (string category in _requests.Select(entry => entry.Key))
        {
            if (_animators[category].Finished() && _weightState[category] == AnimatorWeightState.Finished)
            {
                _animators.Remove(category);
                _currentTimes.Remove(category);
                _previousWeight.Remove(category);
                _currentWeight.Remove(category);
                _weightState.Remove(category);
                _requests.Remove(category);
            }
        }

        return result;
    }

    public void Play(AnimationRequest request)
    {
        if (_animators.ContainsKey(request.Category))
        {
            string category = request.Category;
            _animators[category].Play(request.Animation, request.AnimationSpeed);
            _requests[category] = request;
            _previousWeight[category] = _currentWeight[category];
            _weightState[category] = AnimatorWeightState.EaseIn;
            _currentTimes[category] = TimeSpan.Zero;
        }
        else
        {
            string category = request.Category;
            _animators.Add(category, new Animator(request.Animation));
            _requests.Add(category, request);
            _previousWeight[category] = 0;
            _currentWeight[category] = 0;
            _weightState[category] = AnimatorWeightState.EaseIn;
            _currentTimes[category] = TimeSpan.Zero;
        }
    }

    public void Stop(string category)
    {
        _animators.Remove(category);
        _currentTimes.Remove(category);
        _previousWeight.Remove(category);
        _currentWeight.Remove(category);
        _weightState.Remove(category);
        _requests.Remove(category);
    }

    private enum AnimatorWeightState
    {
        EaseIn,
        Stay,
        EaseOut,
        Finished
    }

    private readonly Dictionary<string, Animator> _animators = new();
    private readonly Dictionary<string, AnimationRequest> _requests = new();
    private readonly Dictionary<string, float> _previousWeight = new();
    private readonly Dictionary<string, float> _currentWeight = new();
    private readonly Dictionary<string, AnimatorWeightState> _weightState = new();
    private readonly Dictionary<string, TimeSpan> _currentTimes = new();

    private void ProcessWeight(string category, AnimatorWeightState state)
    {
        switch (state)
        {
            case AnimatorWeightState.EaseIn:
                _currentWeight[category] = (_requests[category].Weight - _previousWeight[category]) * (float)(_currentTimes[category] / _requests[category].EaseInDuration);
                if (_currentWeight[category] > _requests[category].Weight)
                {
                    _currentWeight[category] = _requests[category].Weight;
                    _weightState[category] = AnimatorWeightState.Stay;
                }
                break;
            case AnimatorWeightState.Stay:
                if (_requests[category].EaseOut && _requests[category].Animation.TotalDuration / _requests[category].AnimationSpeed > _currentTimes[category])
                {
                    _weightState[category] = AnimatorWeightState.EaseOut;
                }
                break;
            case AnimatorWeightState.EaseOut:
                _currentWeight[category] = _requests[category].Weight * (float)((_requests[category].EaseOutDuration - (_currentTimes[category] - _requests[category].Animation.TotalDuration / _requests[category].AnimationSpeed)) / _requests[category].EaseOutDuration);
                if (_currentWeight[category] < 0)
                {
                    _currentWeight[category] = 0;
                    _weightState[category] = AnimatorWeightState.Finished;
                }
                break;
        }
    }
}

public readonly struct AnimationRequest
{
    public readonly Animation Animation;
    public readonly float AnimationSpeed;
    public readonly float Weight;
    public readonly string Category;
    public readonly TimeSpan EaseOutDuration;
    public readonly TimeSpan EaseInDuration;
    public readonly bool EaseOut;
    public readonly int ItemId;
    public readonly AnimationType AnimationType;
}

internal struct Animator
{
    public Animator(Animation animation)
    {
        _currentAnimation = animation;
    }

    public void Play(Animation animation, TimeSpan duration) => Play(animation, (float)(animation.TotalDuration / duration));
    public void Play(Animation animation, float animationSpeed)
    {
        _currentAnimation = animation;
        _animationSpeed = animationSpeed;
        _currentDuration = TimeSpan.Zero;
        _previousAnimationFrame = _lastFrame;
    }

    public Frame Animate(TimeSpan delta)
    {
        _currentDuration += delta;
        TimeSpan adjustedDuration = _currentDuration / _animationSpeed;

        _lastFrame = _currentAnimation.Interpolate(_previousAnimationFrame, adjustedDuration);
        return _lastFrame;
    }
    public readonly bool Finished() => _currentAnimation.TotalDuration >= _currentDuration / _animationSpeed;

    private Frame _previousAnimationFrame = new();
    private Frame _lastFrame = new();
    private TimeSpan _currentDuration = TimeSpan.Zero;
    private float _animationSpeed = 1;
    private Animation _currentAnimation;
}

public sealed class Animation
{
    public readonly ImmutableArray<KeyFrame> KeyFrames;
    public readonly ImmutableArray<TimeSpan> Durations;
    public readonly TimeSpan TotalDuration;

    public Animation(IEnumerable<KeyFrame> frames)
    {
        if (!frames.Any()) throw new ArgumentException("Frames number should be at least 1");

        KeyFrames = frames.ToImmutableArray();
        TotalDuration = TimeSpan.Zero;
        List<TimeSpan> durations = new();
        foreach (KeyFrame frame in frames)
        {
            TotalDuration += frame.EasingTime;
            durations.Add(TotalDuration);
        }
        Durations = durations.ToImmutableArray();
    }

    public static readonly Animation Zero = new(new KeyFrame[] { KeyFrame.Zero });

    public Frame Interpolate(Frame previousAnimationFrame, TimeSpan currentDuration)
    {
        if (Finished(currentDuration)) return KeyFrames[^1].Frame;

        int nextKeyFrame;
        for (nextKeyFrame = 0; nextKeyFrame < KeyFrames.Length - 1; nextKeyFrame++)
        {
            if (Durations[nextKeyFrame + 1] > currentDuration) break;
        }

        if (nextKeyFrame == 0) return KeyFrames[0].Interpolate(previousAnimationFrame, currentDuration);

        return KeyFrames[nextKeyFrame].Interpolate(KeyFrames[nextKeyFrame - 1].Frame, currentDuration - Durations[nextKeyFrame - 1]);
    }

    public bool Finished(TimeSpan currentDuration) => currentDuration >= TotalDuration;
}

public readonly struct KeyFrame
{
    public readonly Frame Frame;
    public readonly TimeSpan EasingTime;
    public readonly EasingFunctionType EasingFunction;

    public KeyFrame(Frame frame, TimeSpan easingTime, EasingFunctionType easeFunction)
    {
        Frame = frame;
        EasingTime = easingTime;
        EasingFunction = easeFunction;
    }

    public static readonly KeyFrame Zero = new(Frame.Zero, TimeSpan.Zero, EasingFunctionType.Linear);

    public Frame Interpolate(Frame frame, TimeSpan currentDuration)
    {
        double progress = EasingTime == TimeSpan.Zero ? 1.0 : currentDuration / EasingTime;
        float interpolatedProgress = EasingFunctions.Get(EasingFunction).Invoke((float)progress);
        return Frame.Interpolate(frame, Frame, interpolatedProgress);
    }

    public bool Reached(TimeSpan currentDuration) => currentDuration >= EasingTime;
}

public readonly struct Frame
{
    public readonly RightHandFrame? RightHand;
    public readonly LeftHandFrame? LeftHand;

    public Frame(RightHandFrame? rightHand = null, LeftHandFrame? leftHand = null)
    {
        RightHand = rightHand;
        LeftHand = leftHand;
    }

    public static readonly Frame Zero = new(RightHandFrame.Zero, LeftHandFrame.Zero);
    public static readonly Frame Empty = new(null, null);

    public void Apply(ElementPose pose)
    {
        RightHand?.Apply(pose);
        LeftHand?.Apply(pose);
    }

    public static Frame Interpolate(Frame from, Frame to, float progress)
    {
        RightHandFrame? righthand = null;
        if (from.RightHand == null && to.RightHand != null)
        {
            righthand = to.RightHand;
        }
        else if (from.RightHand != null && to.RightHand == null)
        {
            righthand = from.RightHand;
        }
        else if (from.RightHand != null && to.RightHand != null)
        {
            righthand = RightHandFrame.Interpolate(from.RightHand.Value, to.RightHand.Value, progress);
        }

        LeftHandFrame? leftHand = null;
        if (from.LeftHand == null && to.LeftHand != null)
        {
            leftHand = to.LeftHand;
        }
        else if (from.LeftHand != null && to.LeftHand == null)
        {
            leftHand = from.LeftHand;
        }
        else if (from.LeftHand != null && to.LeftHand != null)
        {
            leftHand = LeftHandFrame.Interpolate(from.LeftHand.Value, to.LeftHand.Value, progress);
        }

        return new(righthand, leftHand);
    }
    public static Frame Compose(IEnumerable<(Frame element, float weight)> frames)
    {
#pragma warning disable CS8629 // Nullable value type may be null.
        return new(
            RightHandFrame.Compose(frames.Where(entry => entry.element.RightHand != null).Select(entry => (entry.element.RightHand.Value, entry.weight))),
            LeftHandFrame.Compose(frames.Where(entry => entry.element.LeftHand != null).Select(entry => (entry.element.LeftHand.Value, entry.weight)))
            );
#pragma warning restore CS8629 // Nullable value type may be null.
    }
}

public readonly struct RightHandFrame
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

    public void Apply(ElementPose pose)
    {
        switch (pose.ForElement.Name)
        {
            case "ItemAnchor":
                ItemAnchor.Apply(pose);
                break;
            case "LowerArmR":
                LowerArmR.Apply(pose);
                break;
            case "UpperArmR":
                UpperArmR.Apply(pose);
                break;
        }
    }

    public static readonly RightHandFrame Zero = new(AnimationElement.Zero, AnimationElement.Zero, AnimationElement.Zero);

    public static RightHandFrame Interpolate(RightHandFrame from, RightHandFrame to, float progress)
    {
        return new(
            AnimationElement.Interpolate(from.ItemAnchor, to.ItemAnchor, progress),
            AnimationElement.Interpolate(from.LowerArmR, to.LowerArmR, progress),
            AnimationElement.Interpolate(from.UpperArmR, to.UpperArmR, progress)
            );
    }

    public static RightHandFrame Compose(IEnumerable<(RightHandFrame element, float weight)> frames)
    {
        return new(
            AnimationElement.Compose(frames.Select(entry => (entry.element.ItemAnchor, entry.weight))),
            AnimationElement.Compose(frames.Select(entry => (entry.element.LowerArmR, entry.weight))),
            AnimationElement.Compose(frames.Select(entry => (entry.element.UpperArmR, entry.weight)))
            );
    }
}

public readonly struct LeftHandFrame
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

    public void Apply(ElementPose pose)
    {
        switch (pose.ForElement.Name)
        {
            case "ItemAnchorL":
                ItemAnchorL.Apply(pose);
                break;
            case "LowerArmL":
                LowerArmL.Apply(pose);
                break;
            case "UpperArmL":
                UpperArmL.Apply(pose);
                break;
        }
    }

    public static readonly LeftHandFrame Zero = new(AnimationElement.Zero, AnimationElement.Zero, AnimationElement.Zero);

    public static LeftHandFrame Interpolate(LeftHandFrame from, LeftHandFrame to, float progress)
    {
        return new(
            AnimationElement.Interpolate(from.ItemAnchorL, to.ItemAnchorL, progress),
            AnimationElement.Interpolate(from.LowerArmL, to.LowerArmL, progress),
            AnimationElement.Interpolate(from.UpperArmL, to.UpperArmL, progress)
            );
    }

    public static LeftHandFrame Compose(IEnumerable<(LeftHandFrame element, float weight)> frames)
    {
        return new(
            AnimationElement.Compose(frames.Select(entry => (entry.element.ItemAnchorL, entry.weight))),
            AnimationElement.Compose(frames.Select(entry => (entry.element.LowerArmL, entry.weight))),
            AnimationElement.Compose(frames.Select(entry => (entry.element.UpperArmL, entry.weight)))
            );
    }
}

public readonly struct AnimationElement
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

    public void Apply(ElementPose pose)
    {
        pose.translateX = OffsetX;
        pose.translateY = OffsetY;
        pose.translateZ = OffsetZ;
        pose.degX = RotationX;
        pose.degY = RotationY;
        pose.degZ = RotationZ;
    }

    public static readonly AnimationElement Zero = new(0, 0, 0, 0, 0, 0);

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
    public static AnimationElement Compose(IEnumerable<(AnimationElement element, float weight)> elements)
    {
        float totalWeight = 0;
        float offsetX = 0;
        float offsetY = 0;
        float offsetZ = 0;
        float rotationX = 0;
        float rotationY = 0;
        float rotationZ = 0;

        foreach ((AnimationElement element, float weight) in elements.Where(entry => entry.weight > 0))
        {
            totalWeight += weight;
            offsetX += element.OffsetX * weight;
            offsetY += element.OffsetY * weight;
            offsetZ += element.OffsetZ * weight;
            rotationX += element.RotationX * weight;
            rotationY += element.RotationY * weight;
            rotationZ += element.RotationZ * weight;
        }

        offsetX /= totalWeight;
        offsetY /= totalWeight;
        offsetZ /= totalWeight;
        rotationX /= totalWeight;
        rotationY /= totalWeight;
        rotationZ /= totalWeight;

        foreach ((AnimationElement element, _) in elements.Where(entry => entry.weight <= 0))
        {
            offsetX += element.OffsetX;
            offsetY += element.OffsetY;
            offsetZ += element.OffsetZ;
            rotationX += element.RotationX;
            rotationY += element.RotationY;
            rotationZ += element.RotationZ;
        }

        return new(
            offsetX,
            offsetY,
            offsetZ,
            rotationX,
            rotationY,
            rotationZ
            );
    }
}