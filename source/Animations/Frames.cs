using CombatOverhaul.Utils;
using ImGuiNET;
using OpenTK.Mathematics;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using VSImGui.Debug;

namespace CombatOverhaul.Animations;

public readonly struct PlayerItemFrame
{
    public readonly PlayerFrame Player;
    public readonly ItemFrame? Item;
    public readonly bool DetachedAnchor;
    public readonly bool SwitchArms;

    public PlayerItemFrame(PlayerFrame player, ItemFrame? item)
    {
        Player = player;
        Item = item;
        DetachedAnchor = player.DetachedAnchor;
        SwitchArms = player.SwitchArms;
    }

    public static readonly PlayerItemFrame Zero = new(PlayerFrame.Zero, null);
    public static readonly PlayerItemFrame Empty = new(PlayerFrame.Empty, null);

    public void Apply(ElementPose pose, Vector3 eyePosition, float cameraPitch = 0, bool applyCameraPitch = false, bool overrideTorso = true)
    {
        Player.Apply(pose, eyePosition, cameraPitch, applyCameraPitch, overrideTorso);
        Item?.Apply(pose);
    }

    public static PlayerItemFrame Compose(IEnumerable<(PlayerItemFrame element, float weight)> frames)
    {
        PlayerFrame player = PlayerFrame.Compose(frames.Select(entry => (entry.element.Player, entry.weight)));
        ItemFrame item = ItemFrame.Compose(frames
            .Where(entry => entry.element.Item != null)
            .Select(entry => (entry.element.Item.Value, entry.weight))
            );

        return new(player, item);
    }
}

public readonly struct SoundFrame
{
    public readonly string[] Code;
    public readonly float DurationFraction;
    public readonly bool RandomizePitch;
    public readonly float Range;
    public readonly float Volume;
    public readonly bool Synchronize;

    public SoundFrame(string[] code, float durationFraction, bool randomizePitch = false, float range = 32, float volume = 1, bool synchronize = true)
    {
        Code = code;
        DurationFraction = durationFraction;
        RandomizePitch = randomizePitch;
        Range = range;
        Volume = volume;
        Synchronize = synchronize;
    }

#if DEBUG
    public SoundFrame Edit(string title, TimeSpan totalDuration)
    {
        string code = Code.Aggregate((first, second) => $"{first},{second}") ?? "";
        ImGui.InputText($"Sound code##{title}", ref code, 300);
        string[] codes = code.Split(',');

        float time = DurationFraction * (float)totalDuration.TotalMilliseconds;
        ImGui.InputFloat($"Time##{title}", ref time);

        bool pitch = RandomizePitch;
        ImGui.Checkbox($"Randomize pitch##{title}", ref pitch);

        float range = Range;
        ImGui.InputFloat($"Range##{title}", ref range);

        float volume = Volume;
        ImGui.SliderFloat($"Volume##{title}", ref volume, 0, 1);

        bool sync = Synchronize;
        ImGui.Checkbox($"Randomize pitch##{title}", ref sync);

        return new(codes, time / (float)totalDuration.TotalMilliseconds, pitch, range, volume, sync);
    }
#endif
}

public readonly struct ParticlesFrame
{
    public readonly string Code;
    public readonly float DurationFraction;
    public readonly Vector3 Position;
    public readonly Vector3 Velocity;
    public readonly float Intensity;

    public ParticlesFrame(string code, float durationFraction, Vector3 position, Vector3 velocity, float intensity)
    {
        Code = code;
        DurationFraction = durationFraction;
        Position = position;
        Velocity = velocity;
        Intensity = intensity;
    }

#if DEBUG
    public ParticlesFrame Edit(string title, TimeSpan totalDuration)
    {
        string code = Code;
        ImGui.InputText($"Effect code##{title}", ref code, 300);

        float time = DurationFraction * (float)totalDuration.TotalMilliseconds;
        ImGui.InputFloat($"Time##{title}", ref time);

        var position = Position.toSystem();
        ImGui.DragFloat3($"Position ##{title}", ref position);

        var velocity = Velocity.toSystem();
        ImGui.DragFloat3($"Velocity ##{title}", ref velocity);

        float intensity = Intensity;
        ImGui.InputFloat($"Intensity ##{title}", ref intensity, 0.1f, 1);

        return new(code, time / (float)totalDuration.TotalMilliseconds, position.toOpenTK(), velocity.toOpenTK(), intensity);
    }
#endif
}

public readonly struct CallbackFrame
{
    public readonly string Code;
    public readonly float DurationFraction;

    public CallbackFrame(string code, float durationFraction)
    {
        Code = code;
        DurationFraction = durationFraction;
    }

#if DEBUG
    public CallbackFrame Edit(string title, TimeSpan totalDuration)
    {
        string code = Code;
        ImGui.InputText($"Effect code##{title}", ref code, 300);

        float time = DurationFraction * (float)totalDuration.TotalMilliseconds;
        ImGui.InputFloat($"Time##{title}", ref time);

        return new(code, time / (float)totalDuration.TotalMilliseconds);
    }
#endif
}

public readonly struct ItemKeyFrame
{
    public readonly ItemFrame Frame;
    public readonly float DurationFraction;
    public readonly EasingFunctionType EasingFunction;

    public ItemKeyFrame(ItemFrame frame, float durationFraction, EasingFunctionType easeFunction)
    {
        Frame = frame;
        DurationFraction = durationFraction;
        EasingFunction = easeFunction;
    }

    public static readonly ItemKeyFrame Empty = new(ItemFrame.Empty, 0, EasingFunctionType.Linear);

    public ItemFrame Interpolate(ItemFrame frame, float frameProgress)
    {
        float interpolatedProgress = EasingFunctions.Get(EasingFunction).Invoke((float)frameProgress);
        return ItemFrame.Interpolate(frame, Frame, interpolatedProgress);
    }

    public bool Reached(float animationProgress) => animationProgress >= DurationFraction;

#if DEBUG
    public ItemKeyFrame Edit(string title, TimeSpan totalDuration, TimeSpan startDuration)
    {
        float total = (float)totalDuration.TotalMilliseconds;

        if (total < 1E-8) return this;

        float progress = (float)startDuration.TotalMilliseconds + DurationFraction * total;
        ImGui.InputFloat($"Time", ref progress, 0, 1);

        EasingFunctionType function = VSImGui.EnumEditor<EasingFunctionType>.Combo($"Easing function##{title}", EasingFunction);

        ItemFrame frame = Frame.Edit(title);

        return new(frame, Math.Clamp((progress - (float)startDuration.TotalMilliseconds) / total, 0, 1), function);
    }
#endif

    public static List<ItemKeyFrame> FromVanillaAnimation(string code, Shape shape)
    {
        AnimationKeyFrame[] vanillaKeyFrames = GetVanillaKeyFrames(code, shape);
        bool missingFirstFrame = vanillaKeyFrames[0].Frame != 0;
        int keyFramesCount = !missingFirstFrame ? vanillaKeyFrames.Length : vanillaKeyFrames.Length + 1;

        HashSet<string> elements = new();
        foreach (AnimationKeyFrame keyFrame in vanillaKeyFrames)
        {
            foreach ((string element, _) in keyFrame.Elements)
            {
                elements.Add(element);
            }
        }

        Dictionary<string, List<AnimationElement>> frames = new();
        foreach (string element in elements)
        {
            frames.Add(element, GetElementFrames(element, vanillaKeyFrames));
        }

        List<ItemKeyFrame> result = new();
        if (missingFirstFrame)
        {
            result.Add(new ItemKeyFrame(new ItemFrame(frames.ToDictionary(entry => entry.Key, entry => entry.Value[0])), 0, EasingFunctionType.Linear));
        }

        for (int index = missingFirstFrame ? 1 : 0; index < keyFramesCount; index++)
        {
            int frameIndex = missingFirstFrame ? index - 1 : index;
            float durationFraction = (float)vanillaKeyFrames[frameIndex].Frame / (vanillaKeyFrames[^1].Frame == 0 ? 1 : vanillaKeyFrames[^1].Frame);

            result.Add(new ItemKeyFrame(new ItemFrame(frames.ToDictionary(entry => entry.Key, entry => entry.Value[index])), durationFraction, EasingFunctionType.Linear));
        }

        return result;
    }

    private static uint ToCrc32(string value) => GameMath.Crc32(value.ToLowerInvariant()) & int.MaxValue;

    private static AnimationKeyFrame[] GetVanillaKeyFrames(string code, Shape shape)
    {
        Dictionary<uint, Vintagestory.API.Common.Animation> animations = shape.AnimationsByCrc32;
        uint crc32 = ToCrc32(code);
        if (!animations.TryGetValue(crc32, out Vintagestory.API.Common.Animation? value))
        {
            throw new InvalidOperationException($"Animation '{code}' not found");
        }

        return value.KeyFrames;
    }

    private static List<AnimationElement> GetElementFrames(string elementName, AnimationKeyFrame[] vanillaFrames)
    {
        IEnumerable<AnimationKeyFrameElement> vanillaElementFrames = vanillaFrames.Where(element => element.Elements.ContainsKey(elementName)).Select(element => element.Elements[elementName]);

        AnimationElement firstFrame = AnimationElement.FromVanilla(vanillaElementFrames.First());
        AnimationElement lastFrame = AnimationElement.FromVanilla(vanillaElementFrames.Last());

        List<AnimationElement> result = new();
        int previousFrameFrame = 0;
        AnimationElement previousFoundElement = firstFrame;

        int startingIndex = 0;
        if (vanillaFrames[0].Frame == 0)
        {
            startingIndex = 1;
        }
        result.Add(firstFrame);

        bool reachedLast = false;

        for (int index = startingIndex; index < vanillaFrames.Length; index++)
        {
            if (reachedLast)
            {
                result.Add(lastFrame);
                continue;
            }

            if (vanillaFrames[index].Elements.ContainsKey(elementName))
            {
                AnimationElement frame = AnimationElement.FromVanilla(vanillaFrames[index].Elements[elementName]);
                result.Add(frame);
                previousFoundElement = frame;
                previousFrameFrame = vanillaFrames[index].Frame;
            }
            else
            {
                int nextFrameFrame = 0;
                AnimationElement nextFrameElement = previousFoundElement;
                bool found = false;

                for (int nextIndex = index + 1; nextIndex < vanillaFrames.Length; nextIndex++)
                {
                    if (vanillaFrames[nextIndex].Elements.ContainsKey(elementName))
                    {
                        nextFrameFrame = vanillaFrames[nextIndex].Frame;
                        nextFrameElement = AnimationElement.FromVanilla(vanillaFrames[nextIndex].Elements[elementName]);
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    reachedLast = true;
                    result.Add(lastFrame);
                    continue;
                }

                int currentFrameFrame = vanillaFrames[index].Frame;
                float interpolationProgress = (currentFrameFrame - previousFrameFrame) / (float)(nextFrameFrame - previousFrameFrame);
                result.Add(AnimationElement.Interpolate(previousFoundElement, nextFrameElement, interpolationProgress));
            }
        }

        return result;
    }
}

public readonly struct ItemFrame
{
    public readonly int ElementsHash = 0;
    public readonly Dictionary<string, AnimationElement> Elements = new();

    public ItemFrame(Dictionary<string, AnimationElement> elements)
    {
        foreach ((string code, AnimationElement value) in elements)
        {
            Elements.Add(code, value);
        }

        if (elements.Any())
        {
            ElementsHash = Elements.Select(entry => entry.Key.GetHashCode()).Aggregate((first, second) => HashCode.Combine(first, second));
        }
    }

    public static readonly ItemFrame Empty = new(new Dictionary<string, AnimationElement>());

    public void Apply(ElementPose pose)
    {
        if (Elements.TryGetValue(pose.ForElement.Name, out AnimationElement element))
        {
            element.Apply(pose);
        }
    }
    public static ItemFrame Interpolate(ItemFrame from, ItemFrame to, float progress)
    {
        if (!from.Elements.Any()) return to;

        Dictionary<string, AnimationElement> elements = new();
        foreach ((string key, AnimationElement toElement) in to.Elements)
        {
            if (from.Elements.TryGetValue(key, out AnimationElement fromElement))
            {
                elements.Add(key, AnimationElement.Interpolate(fromElement, toElement, progress));
            }
            else
            {
                elements.Add(key, toElement);
            }
        }

        return new(elements);
    }
    public static ItemFrame Compose(IEnumerable<(ItemFrame element, float weight)> frames)
    {
        if (!frames.Any()) return Empty;

        HashSet<string> keys = frames
            .Select(entry => entry.element.Elements.Keys)
            .SelectMany(entry => entry)
            .ToHashSet();


        Dictionary<string, AnimationElement> elements = new();
        foreach (string key in keys)
        {
            elements.Add(key,
                AnimationElement.Compose(
                    frames
                        .Where(entry => entry.element.Elements.ContainsKey(key))
                        .Select(entry => (entry.element.Elements[key], entry.weight))
                    )
                );
        }

        return new(elements);
    }

#if DEBUG
    public ItemFrame Edit(string title)
    {
        Dictionary<string, AnimationElement> newElements = new();
        foreach ((string key, AnimationElement element) in Elements)
        {
            ImGui.SeparatorText(key);
            AnimationElement newElement = element.Edit(title + key, 1);
            newElements.Add(key, newElement);
        }
        return new(newElements);
    }
#endif
}

public readonly struct PLayerKeyFrame
{
    public readonly PlayerFrame Frame;
    public readonly TimeSpan Time;
    public readonly EasingFunctionType EasingFunction;

    public PLayerKeyFrame(PlayerFrame frame, TimeSpan easingTime, EasingFunctionType easeFunction)
    {
        Frame = frame;
        Time = easingTime;
        EasingFunction = easeFunction;
    }

    public static readonly PLayerKeyFrame Zero = new(PlayerFrame.Zero, TimeSpan.Zero, EasingFunctionType.Linear);

    public PlayerFrame Interpolate(PlayerFrame frame, float frameProgress)
    {
        float interpolatedProgress = EasingFunctions.Get(EasingFunction).Invoke((float)frameProgress);
        return PlayerFrame.Interpolate(frame, Frame, interpolatedProgress);
    }

    public bool Reached(TimeSpan currentDuration) => currentDuration >= Time;

#if DEBUG
    public PLayerKeyFrame Edit(string title)
    {
        int milliseconds = (int)Time.TotalMilliseconds;
        ImGui.DragInt($"Easing time##{title}", ref milliseconds);

        EasingFunctionType function = VSImGui.EnumEditor<EasingFunctionType>.Combo($"Easing function##{title}", EasingFunction);

        ImGui.SeparatorText("Key frame");

        PlayerFrame frame = Frame.Edit(title);

        return new(frame, TimeSpan.FromMilliseconds(milliseconds), function);
    }
#endif
}

public readonly struct PlayerFrame
{
    public readonly RightHandFrame? RightHand;
    public readonly LeftHandFrame? LeftHand;
    public readonly OtherPartsFrame? OtherParts = null;
    public readonly AnimationElement? UpperTorso = null;
    public readonly AnimationElement? DetachedAnchorFrame = null;
    public readonly AnimationElement? LowerTorso = null;
    public readonly bool DetachedAnchor = false;
    public readonly bool SwitchArms = false;
    public readonly float PitchFollow = DefaultPitchFollow;
    public readonly float FovMultiplier = 1;
    public readonly float BobbingAmplitude = 1;
    public readonly float DetachedAnchorFollow = 1;

    public const float DefaultPitchFollow = 0.8f;
    public const float PerfectPitchFollow = 1.0f;
    public const float Epsilon = 1E-6f;
    public const float DefaultEyesHeight = 1.7f;
    public const float EyeHeightToAnimationDistanceMultiplier = 16.1f;
    public const float PitchAngleMin = -45;
    public const float PitchAngleMax = 75;

    public PlayerFrame(
        RightHandFrame? rightHand = null,
        LeftHandFrame? leftHand = null,
        OtherPartsFrame? otherParts = null,
        AnimationElement? upperTorso = null,
        AnimationElement? detachedAnchorFrame = null,
        bool detachedAnchor = false,
        bool switchArms = false,
        float pitchFollow = DefaultPitchFollow,
        float fovMultiplier = 1.0f,
        float bobbingAmplitude = 1.0f,
        float? detachedAnchorFollow = null,
        AnimationElement? lowerTorso = null)
    {
        RightHand = rightHand;
        LeftHand = leftHand;
        OtherParts = otherParts;
        UpperTorso = upperTorso;
        DetachedAnchorFrame = detachedAnchorFrame;
        DetachedAnchor = detachedAnchor;
        SwitchArms = switchArms;
        PitchFollow = pitchFollow;
        FovMultiplier = fovMultiplier;
        BobbingAmplitude = bobbingAmplitude;
        LowerTorso = lowerTorso;
        DetachedAnchorFollow = detachedAnchorFollow ?? (detachedAnchor ? 0 : 1);
    }

    public static readonly PlayerFrame Zero = new(RightHandFrame.Zero, LeftHandFrame.Zero, OtherPartsFrame.Zero);
    public static readonly PlayerFrame Empty = new();

    public void Apply(ElementPose pose, Vector3 eyePosition, float cameraPitch, bool applyCameraPitch, bool overrideTorso)
    {
        switch (pose.ForElement.Name)
        {
            case "DetachedAnchor":
                DetachedAnchorFrame?.Apply(pose);
                break;
            case "UpperTorso":
                UpperTorso?.Apply(pose);
                if (applyCameraPitch)
                {
                    pose.degZ += GameMath.Clamp(cameraPitch * GameMath.RAD2DEG * PitchFollow * DetachedAnchorFollow, PitchAngleMin, PitchAngleMax);
                }
                break;
            case "Neck":
                OtherParts?.Apply(pose);
                if (applyCameraPitch)
                {
                    pose.degZ = -GameMath.Clamp(cameraPitch * GameMath.RAD2DEG * PitchFollow * DetachedAnchorFollow, PitchAngleMin, PitchAngleMax) / 2;
                }
                break;
            case "LowerTorso":
                if (overrideTorso)
                {
                    if (LowerTorso != null)
                    {
                        AnimationElement torso = new(LowerTorso?.OffsetX, (eyePosition.Y - DefaultEyesHeight) * EyeHeightToAnimationDistanceMultiplier, LowerTorso?.OffsetZ, LowerTorso?.RotationX, LowerTorso?.RotationY, LowerTorso?.RotationZ);
                        torso.Apply(pose);
                    }
                    else
                    {
                        AnimationElement torso = new(0, (eyePosition.Y - DefaultEyesHeight) * EyeHeightToAnimationDistanceMultiplier, 0, 0, 0, 0);
                        torso.Apply(pose);
                    }
                }
                else
                {
                    LowerTorso?.Apply(pose);
                    pose.translateY = (eyePosition.Y - DefaultEyesHeight) * EyeHeightToAnimationDistanceMultiplier / 16;
                }
                break;
            default:
                OtherParts?.Apply(pose);
                RightHand?.Apply(pose);
                LeftHand?.Apply(pose);
                break;
        }
    }

#if DEBUG
    public PlayerFrame Edit(string title)
    {
        bool detachedAnchor = DetachedAnchor;
        ImGui.Checkbox($"Detached anchor##{title}", ref detachedAnchor);

        bool switchArms = SwitchArms;
        ImGui.Checkbox($"Switch arms##{title}", ref switchArms);

        bool pitchFollow = Math.Abs(PitchFollow - PerfectPitchFollow) < Epsilon;
        ImGui.Checkbox($"Pitch follow##{title}", ref pitchFollow);

        bool pitchDontFollow = Math.Abs(PitchFollow - 0) < Epsilon && !pitchFollow;
        ImGui.Checkbox($"Pitch dont follow##{title}", ref pitchDontFollow);

        float fov = FovMultiplier;
        ImGui.SliderFloat($"FOV multiplier##{title}", ref fov, 0.1f, 2.0f);

        float bobbing = BobbingAmplitude;
        ImGui.SliderFloat($"Bobbing amplitude##{title}", ref bobbing, 0.1f, 2.0f);

        bool rightHand = RightHand != null;
        bool leftHand = LeftHand != null;
        bool upperTorso = UpperTorso != null;
        bool lowerTorso = LowerTorso != null;
        bool otherParts = OtherParts != null;
        bool detachedAnchorFrame = DetachedAnchorFrame != null;

        ImGui.Checkbox($"Right hand frame##{title}", ref rightHand); ImGui.SameLine();
        ImGui.Checkbox($"Left hand frame##{title}", ref leftHand); ImGui.SameLine();
        ImGui.Checkbox($"Upper torso frame##{title}", ref upperTorso);
        ImGui.Checkbox($"Lower torso frame##{title}", ref lowerTorso); ImGui.SameLine();
        ImGui.Checkbox($"Other parts frame##{title}", ref otherParts); ImGui.SameLine();
        ImGui.Checkbox($"Detached anchor frame##{title}", ref detachedAnchorFrame);

        RightHandFrame? right = RightHand?.Edit(title);
        LeftHandFrame? left = LeftHand?.Edit(title);

        if (upperTorso) ImGui.SeparatorText($"UpperTorso");
        AnimationElement? torso = UpperTorso?.Edit($"{title}##UpperTorso");

        if (lowerTorso) ImGui.SeparatorText($"LowerTorso");
        AnimationElement? torsoLower = LowerTorso?.Edit($"{title}##LowerTorso");

        if (detachedAnchorFrame) ImGui.SeparatorText($"DetachedAnchor");
        AnimationElement? anchor = DetachedAnchorFrame?.Edit($"{title}##DetachedAnchor");

        OtherPartsFrame? other = OtherParts?.Edit($"{title}##OtherParts");

        if (RightHand == null && rightHand) right = RightHandFrame.Zero;
        if (RightHand != null && !rightHand) right = null;
        if (LeftHand == null && leftHand) left = LeftHandFrame.Zero;
        if (LeftHand != null && !leftHand) left = null;
        if (OtherParts == null && otherParts) other = OtherPartsFrame.Zero;
        if (OtherParts != null && !otherParts) other = null;
        if (UpperTorso == null && upperTorso) torso = AnimationElement.Zero;
        if (UpperTorso != null && !upperTorso) torso = null;
        if (LowerTorso == null && lowerTorso) torsoLower = AnimationElement.Zero;
        if (LowerTorso != null && !lowerTorso) torsoLower = null;
        if (DetachedAnchorFrame == null && detachedAnchorFrame) anchor = AnimationElement.Zero;
        if (DetachedAnchorFrame != null && !detachedAnchorFrame) anchor = null;

        float pitch = DefaultPitchFollow;
        if (pitchFollow) pitch = PerfectPitchFollow;
        if (pitchDontFollow) pitch = 0;

        return new(right, left, other, torso, anchor, detachedAnchor, switchArms, pitch, fov, bobbing, lowerTorso: torsoLower);
    }
#endif

    public static PlayerFrame Interpolate(PlayerFrame from, PlayerFrame to, float progress)
    {
        RightHandFrame? righthand = null;
        if (from.RightHand == null && to.RightHand != null)
        {
            righthand = null;//to.RightHand;
        }
        else if (from.RightHand != null && to.RightHand == null)
        {
            righthand = null;//from.RightHand;
        }
        else if (from.RightHand != null && to.RightHand != null)
        {
            righthand = RightHandFrame.Interpolate(from.RightHand.Value, to.RightHand.Value, progress);
        }

        LeftHandFrame? leftHand = null;
        if (from.LeftHand == null && to.LeftHand != null)
        {
            leftHand = null;//to.LeftHand;
        }
        else if (from.LeftHand != null && to.LeftHand == null)
        {
            leftHand = null;//from.LeftHand;
        }
        else if (from.LeftHand != null && to.LeftHand != null)
        {
            leftHand = LeftHandFrame.Interpolate(from.LeftHand.Value, to.LeftHand.Value, progress);
        }

        OtherPartsFrame? otherParts = null;
        if (from.OtherParts == null && to.OtherParts != null)
        {
            otherParts = null;//to.LeftHand;
        }
        else if (from.OtherParts != null && to.OtherParts == null)
        {
            otherParts = null;//from.LeftHand;
        }
        else if (from.OtherParts != null && to.OtherParts != null)
        {
            otherParts = OtherPartsFrame.Interpolate(from.OtherParts.Value, to.OtherParts.Value, progress);
        }

        AnimationElement? anchor = AnimationElement.Interpolate(from.DetachedAnchorFrame ?? AnimationElement.Zero, to.DetachedAnchorFrame ?? AnimationElement.Zero, progress);
        AnimationElement? torso = AnimationElement.Interpolate(from.UpperTorso ?? AnimationElement.Zero, to.UpperTorso ?? AnimationElement.Zero, progress);
        AnimationElement? lowerTorso = AnimationElement.Interpolate(from.LowerTorso ?? AnimationElement.Zero, to.LowerTorso ?? AnimationElement.Zero, progress);

        if (from.DetachedAnchorFrame == null && to.DetachedAnchorFrame == null) anchor = null;
        if (from.UpperTorso == null && to.UpperTorso == null) torso = null;

        float pitchFollow = from.PitchFollow + (to.PitchFollow - from.PitchFollow) * progress;
        float fov = from.FovMultiplier + (to.FovMultiplier - from.FovMultiplier) * progress;
        float bobbing = from.BobbingAmplitude + (to.BobbingAmplitude - from.BobbingAmplitude) * progress;
        float detachedAnchorFollow = from.DetachedAnchorFollow + (to.DetachedAnchorFollow - from.DetachedAnchorFollow) * progress;

        return new(righthand, leftHand, otherParts, torso, anchor, to.DetachedAnchor, to.SwitchArms, pitchFollow, fov, bobbing, detachedAnchorFollow, lowerTorso);
    }
    public static PlayerFrame Compose(IEnumerable<(PlayerFrame element, float weight)> frames)
    {
        RightHandFrame rightHand = RightHandFrame.Compose(frames.Where(entry => entry.element.RightHand != null).Select(entry => (entry.element.RightHand.Value, entry.weight)));
        LeftHandFrame leftHand = LeftHandFrame.Compose(frames.Where(entry => entry.element.LeftHand != null).Select(entry => (entry.element.LeftHand.Value, entry.weight)));
        OtherPartsFrame otherParts = OtherPartsFrame.Compose(frames.Where(entry => entry.element.OtherParts != null).Select(entry => (entry.element.OtherParts.Value, entry.weight)));

        bool haveRightHandFrame = frames.Any(entry => entry.element.RightHand != null);
        bool haveLeftHandFrame = frames.Any(entry => entry.element.LeftHand != null);
        bool haveOtherPartsFrame = frames.Any(entry => entry.element.OtherParts != null);

        return new(
            haveRightHandFrame ? rightHand : null,
            haveLeftHandFrame ? leftHand : null,
            haveOtherPartsFrame ? otherParts : null,
            AnimationElement.Compose(frames.Where(entry => entry.element.UpperTorso != null).Select(entry => (entry.element.UpperTorso.Value, entry.weight))),
            AnimationElement.Compose(frames.Where(entry => entry.element.DetachedAnchorFrame != null).Select(entry => (entry.element.DetachedAnchorFrame.Value, entry.weight))),
            frames.Select(entry => entry.element.DetachedAnchor).Aggregate((first, second) => first || second),
            frames.Select(entry => entry.element.SwitchArms).Aggregate((first, second) => first || second),
            frames.Select(entry => entry.element.PitchFollow).Where(value => Math.Abs(value - DefaultPitchFollow) > 1E-6f).FirstOrDefault(DefaultPitchFollow),
            frames.Select(entry => entry.element.FovMultiplier).Min(),
            frames.Select(entry => entry.element.BobbingAmplitude).Min(),
            frames.Select(entry => entry.element.DetachedAnchorFollow).Min(),
            AnimationElement.Compose(frames.Where(entry => entry.element.LowerTorso != null).Select(entry => (entry.element.LowerTorso.Value, entry.weight)))
            );
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

#if DEBUG
    public RightHandFrame Edit(string title)
    {
        ImGui.SeparatorText($"ItemAnchor");
        AnimationElement anchor = ItemAnchor.Edit($"{title}##ItemAnchor");
        ImGui.SeparatorText($"LowerArmR");
        AnimationElement lower = LowerArmR.Edit($"{title}##LowerArmR");
        ImGui.SeparatorText($"UpperArmR");
        AnimationElement upper = UpperArmR.Edit($"{title}##UpperArmR");

        return new(anchor, lower, upper);
    }
#endif

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

#if DEBUG
    public LeftHandFrame Edit(string title)
    {
        ImGui.SeparatorText($"ItemAnchorL");
        AnimationElement anchor = ItemAnchorL.Edit($"{title}##ItemAnchorL");
        ImGui.SeparatorText($"LowerArmL");
        AnimationElement lower = LowerArmL.Edit($"{title}##LowerArmL");
        ImGui.SeparatorText($"UpperArmL");
        AnimationElement upper = UpperArmL.Edit($"{title}##UpperArmL");

        return new(anchor, lower, upper);
    }
#endif

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

public readonly struct OtherPartsFrame
{
    public readonly AnimationElement Neck;
    public readonly AnimationElement Head;
    public readonly AnimationElement UpperFootR;
    public readonly AnimationElement UpperFootL;
    public readonly AnimationElement LowerFootR;
    public readonly AnimationElement LowerFootL;

    public OtherPartsFrame(
        AnimationElement neck,
        AnimationElement head,
        AnimationElement upperFootR,
        AnimationElement upperFootL,
        AnimationElement lowerFootR,
        AnimationElement lowerFootL)
    {
        Neck = neck;
        Head = head;
        UpperFootR = upperFootR;
        UpperFootL = upperFootL;
        LowerFootR = lowerFootR;
        LowerFootL = lowerFootL;
    }

    public void Apply(ElementPose pose)
    {
        switch (pose.ForElement.Name)
        {
            case "Neck":
                Neck.Apply(pose);
                break;
            case "Head":
                Head.Apply(pose);
                break;
            case "UpperFootR":
                UpperFootR.Apply(pose);
                break;
            case "UpperFootL":
                UpperFootL.Apply(pose);
                break;
            case "LowerFootR":
                LowerFootR.Apply(pose);
                break;
            case "LowerFootL":
                LowerFootL.Apply(pose);
                break;
        }
    }

    public static readonly OtherPartsFrame Zero = new(AnimationElement.Zero, AnimationElement.Zero, AnimationElement.Zero, AnimationElement.Zero, AnimationElement.Zero, AnimationElement.Zero);

#if DEBUG
    public OtherPartsFrame Edit(string title)
    {
        ImGui.SeparatorText($"Neck");
        AnimationElement neck = Neck.Edit($"{title}##Neck");
        ImGui.SeparatorText($"Head");
        AnimationElement head = Head.Edit($"{title}##Head");
        ImGui.SeparatorText($"UpperFootR");
        AnimationElement upperFootR = UpperFootR.Edit($"{title}##UpperFootR");
        ImGui.SeparatorText($"UpperFootL");
        AnimationElement upperFootL = UpperFootL.Edit($"{title}##UpperFootL");
        ImGui.SeparatorText($"LowerFootR");
        AnimationElement lowerFootR = LowerFootR.Edit($"{title}##LowerFootR");
        ImGui.SeparatorText($"LowerFootL");
        AnimationElement lowerFootL = LowerFootL.Edit($"{title}##LowerFootL");

        return new(neck, head, upperFootR, upperFootL, lowerFootR, lowerFootL);
    }
#endif

    public static OtherPartsFrame Interpolate(OtherPartsFrame from, OtherPartsFrame to, float progress)
    {
        return new(
            AnimationElement.Interpolate(from.Neck, to.Neck, progress),
            AnimationElement.Interpolate(from.Head, to.Head, progress),
            AnimationElement.Interpolate(from.UpperFootR, to.UpperFootR, progress),
            AnimationElement.Interpolate(from.UpperFootL, to.UpperFootL, progress),
            AnimationElement.Interpolate(from.LowerFootR, to.LowerFootR, progress),
            AnimationElement.Interpolate(from.LowerFootL, to.LowerFootL, progress)
            );
    }

    public static OtherPartsFrame Compose(IEnumerable<(OtherPartsFrame element, float weight)> frames)
    {
        return new(
            AnimationElement.Compose(frames.Select(entry => (entry.element.Neck, entry.weight))),
            AnimationElement.Compose(frames.Select(entry => (entry.element.Head, entry.weight))),
            AnimationElement.Compose(frames.Select(entry => (entry.element.UpperFootR, entry.weight))),
            AnimationElement.Compose(frames.Select(entry => (entry.element.UpperFootL, entry.weight))),
            AnimationElement.Compose(frames.Select(entry => (entry.element.LowerFootR, entry.weight))),
            AnimationElement.Compose(frames.Select(entry => (entry.element.LowerFootL, entry.weight)))
            );
    }
}

public readonly struct AnimationElement
{
    public readonly float? OffsetX;
    public readonly float? OffsetY;
    public readonly float? OffsetZ;
    public readonly float? RotationX;
    public readonly float? RotationY;
    public readonly float? RotationZ;

    public AnimationElement(float?[] values)
    {
        OffsetX = values[0];
        OffsetY = values[1];
        OffsetZ = values[2];
        RotationX = values[3];
        RotationY = values[4];
        RotationZ = values[5];
    }
    public AnimationElement(float? offsetX, float? offsetY, float? offsetZ, float? rotationX, float? rotationY, float? rotationZ)
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
        pose.translateX = OffsetX / 16 ?? 0;
        pose.translateY = OffsetY / 16 ?? 0;
        pose.translateZ = OffsetZ / 16 ?? 0;
        pose.degX = RotationX ?? 0;
        pose.degY = RotationY ?? 0;
        pose.degZ = RotationZ ?? 0;
    }

    public static readonly AnimationElement Zero = new(0, 0, 0, 0, 0, 0);

#if DEBUG
    public AnimationElement Edit(string title, float multiplier = 10)
    {
        float speed = ImGui.GetIO().KeysDown[(int)ImGuiKey.LeftShift] ? 0.1f : 1;

        float? offsetX = EditValue(OffsetX, multiplier, speed, $"X##translation{title}"); ImGui.SameLine();
        float? offsetY = EditValue(OffsetY, multiplier, speed, $"Y##translation{title}"); ImGui.SameLine();
        float? offsetZ = EditValue(OffsetZ, multiplier, speed, $"Z##translation{title}"); ImGui.SameLine();
        ImGui.Text("Translation"); ImGui.SameLine();

        ImGui.SameLine();
        if (ImGui.Button($"Copy##{title}"))
        {
            _buffer = this;
        }

        float? rotationX = EditValue(RotationX, 1, speed, $"X##rotation{title}"); ImGui.SameLine();
        float? rotationY = EditValue(RotationY, 1, speed, $"Y##rotation{title}"); ImGui.SameLine();
        float? rotationZ = EditValue(RotationZ, 1, speed, $"Z##rotation{title}"); ImGui.SameLine();
        ImGui.Text("Rotation     "); ImGui.SameLine();

        ImGui.SameLine();
        if (ImGui.Button($"Paste##{title}"))
        {
            return _buffer;
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
#endif

    public float?[] ToArray() => new float?[]
            {
                OffsetX,
                OffsetY,
                OffsetZ,
                RotationX,
                RotationY,
                RotationZ
            };

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
            offsetX += element.OffsetX * weight ?? 0;
            offsetY += element.OffsetY * weight ?? 0;
            offsetZ += element.OffsetZ * weight ?? 0;
            rotationX += element.RotationX * weight ?? 0;
            rotationY += element.RotationY * weight ?? 0;
            rotationZ += element.RotationZ * weight ?? 0;
        }

        if (totalWeight != 0)
        {
            offsetX /= totalWeight;
            offsetY /= totalWeight;
            offsetZ /= totalWeight;
            rotationX /= totalWeight;
            rotationY /= totalWeight;
            rotationZ /= totalWeight;
        }

        foreach ((AnimationElement element, _) in elements.Where(entry => entry.weight <= 0))
        {
            offsetX += element.OffsetX ?? 0;
            offsetY += element.OffsetY ?? 0;
            offsetZ += element.OffsetZ ?? 0;
            rotationX += element.RotationX ?? 0;
            rotationY += element.RotationY ?? 0;
            rotationZ += element.RotationZ ?? 0;
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
    public static AnimationElement FromVanilla(AnimationKeyFrameElement frame)
    {
        return new(
            (float?)frame.OffsetX ?? 0,
            (float?)frame.OffsetY ?? 0,
            (float?)frame.OffsetZ ?? 0,
            (float?)frame.RotationX ?? 0,
            (float?)frame.RotationY ?? 0,
            (float?)frame.RotationZ ?? 0);
    }

#if DEBUG
    private static float? EditValue(float? value, float multiplier, float speed, string title)
    {
        bool enabled = value != null;
        if (enabled)
        {
            float valueValue = value.Value * multiplier;
            ImGui.SetNextItemWidth(90);
            ImGui.DragFloat($"##{title}value", ref valueValue, speed); ImGui.SameLine();

            ImGui.Checkbox($"##{title}checkbox", ref enabled);
            value = enabled ? valueValue / multiplier : null;
        }
        else
        {
            ImGui.Checkbox($"{title}##checkbox", ref enabled);
            value = enabled ? 0 : value;
        }
        return value;
    }
    private static AnimationElement _buffer = AnimationElement.Zero;
#endif
}