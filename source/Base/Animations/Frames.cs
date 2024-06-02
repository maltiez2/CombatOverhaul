using ImGuiNET;
using System.Collections.Immutable;
using System.Numerics;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace CombatOverhaul.PlayerAnimations;

public readonly struct PlayerItemFrame
{
    public readonly PlayerFrame Player;
    public readonly ItemFrame? Item;

    public PlayerItemFrame(PlayerFrame player, ItemFrame? item)
    {
        Player = player;
        Item = item;
    }

    public static readonly PlayerItemFrame Zero = new(PlayerFrame.Zero, null);
    public static readonly PlayerItemFrame Empty = new(new PlayerFrame(), null);

    public void Apply(ElementPose pose)
    {
        Player.Apply(pose);
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

public readonly struct ItemKeyFrame
{
    public readonly ItemFrame Frame;
    public readonly float DurationFraction;

    public ItemKeyFrame(ItemFrame frame, float durationFraction)
    {
        Frame = frame;
        DurationFraction = durationFraction;
    }

    public static readonly ItemKeyFrame Empty = new(ItemFrame.Empty, 0);

    public ItemFrame Interpolate(ItemFrame frame, float frameProgress)
    {
        return ItemFrame.Interpolate(frame, Frame, frameProgress);
    }

    public bool Reached(float animationProgress) => animationProgress >= DurationFraction;

    public ItemKeyFrame Edit(string title)
    {
        float progress = DurationFraction;
        ImGui.SliderFloat($"Duration fraction", ref progress, 0, 1);

        ImGui.SeparatorText("Key frame");

        ItemFrame frame = Frame.Edit(title);

        return new(frame, progress);
    }

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
            result.Add(new ItemKeyFrame(new ItemFrame(frames.ToDictionary(entry => entry.Key, entry => entry.Value[0])), 0));
        }

        for (int index = missingFirstFrame ? 1 : 0; index < keyFramesCount; index++)
        {
            int frameIndex = missingFirstFrame ? index - 1 : index;
            float durationFraction = (float)vanillaKeyFrames[frameIndex].Frame / (vanillaKeyFrames[^1].Frame == 0 ? 1 : vanillaKeyFrames[^1].Frame);
            
            result.Add(new ItemKeyFrame(new ItemFrame(frames.ToDictionary(entry => entry.Key, entry => entry.Value[index])), durationFraction));
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
    public readonly ImmutableDictionary<string, AnimationElement> Elements;

    public ItemFrame(Dictionary<string, AnimationElement> elements)
    {
        Elements = elements.ToImmutableDictionary();
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
        
        if (from.ElementsHash != to.ElementsHash)
        {
            throw new InvalidOperationException("Trying to interpolate item frames with different sets of elements");
        }

        Dictionary<string, AnimationElement> elements = new();
        foreach ((string key, AnimationElement fromElement) in from.Elements)
        {
            AnimationElement toElement = to.Elements[key];
            elements.Add(key, AnimationElement.Interpolate(fromElement, toElement, progress));
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
    public ItemFrame Edit(string title)
    {
        Dictionary<string, AnimationElement> newElements = new();
        foreach ((string key, AnimationElement element) in Elements)
        {
            ImGui.SeparatorText(key);
            AnimationElement newElement = element.Edit(title);
            newElements.Add(key, newElement);
        }
        return new(newElements);
    }
}

public readonly struct PLayerKeyFrame
{
    public readonly PlayerFrame Frame;
    public readonly TimeSpan EasingTime;
    public readonly EasingFunctionType EasingFunction;

    public PLayerKeyFrame(PlayerFrame frame, TimeSpan easingTime, EasingFunctionType easeFunction)
    {
        Frame = frame;
        EasingTime = easingTime;
        EasingFunction = easeFunction;
    }

    public static readonly PLayerKeyFrame Zero = new(PlayerFrame.Zero, TimeSpan.Zero, EasingFunctionType.Linear);

    public PlayerFrame Interpolate(PlayerFrame frame, TimeSpan currentDuration)
    {
        double progress = EasingTime == TimeSpan.Zero ? 1.0 : currentDuration / EasingTime;
        float interpolatedProgress = EasingFunctions.Get(EasingFunction).Invoke((float)progress);
        return PlayerFrame.Interpolate(frame, Frame, interpolatedProgress);
    }

    public bool Reached(TimeSpan currentDuration) => currentDuration >= EasingTime;

    public PLayerKeyFrame Edit(string title)
    {
        int milliseconds = (int)EasingTime.TotalMilliseconds;
        ImGui.DragInt($"Easing time##{title}", ref milliseconds);

        EasingFunctionType function = VSImGui.EnumEditor<EasingFunctionType>.Combo($"Easing function##{title}", EasingFunction);

        ImGui.SeparatorText("Key frame");

        PlayerFrame frame = Frame.Edit(title);

        return new(frame, TimeSpan.FromMilliseconds(milliseconds), function);
    }
}

public readonly struct PlayerFrame
{
    public readonly RightHandFrame? RightHand;
    public readonly LeftHandFrame? LeftHand;

    public PlayerFrame(RightHandFrame? rightHand = null, LeftHandFrame? leftHand = null)
    {
        RightHand = rightHand;
        LeftHand = leftHand;
    }

    public static readonly PlayerFrame Zero = new(RightHandFrame.Zero, LeftHandFrame.Zero);

    public void Apply(ElementPose pose)
    {
        RightHand?.Apply(pose);
        LeftHand?.Apply(pose);
    }

    public PlayerFrame Edit(string title)
    {
        bool rightHand = RightHand != null;
        bool leftHand = LeftHand != null;

        ImGui.Checkbox($"Right hand frame##{title}", ref rightHand); ImGui.SameLine();
        ImGui.Checkbox($"Left hand frame##{title}", ref leftHand);

        RightHandFrame? right = RightHand?.Edit(title);
        LeftHandFrame? left = LeftHand?.Edit(title);

        if (RightHand == null && rightHand) right = RightHandFrame.Zero;
        if (RightHand != null && !rightHand) right = null;
        if (LeftHand == null && leftHand) left = LeftHandFrame.Zero;
        if (LeftHand != null && !leftHand) left = null;

        return new(right, left);
    }

    public static PlayerFrame Interpolate(PlayerFrame from, PlayerFrame to, float progress)
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
    public static PlayerFrame Compose(IEnumerable<(PlayerFrame element, float weight)> frames)
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
        pose.translateX = OffsetX / 16;
        pose.translateY = OffsetY / 16;
        pose.translateZ = OffsetZ / 16;
        pose.degX = RotationX;
        pose.degY = RotationY;
        pose.degZ = RotationZ;
    }

    public static readonly AnimationElement Zero = new(0, 0, 0, 0, 0, 0);

    public AnimationElement Edit(string title)
    {
        Vector3 translation = new(OffsetX, OffsetY, OffsetZ);
        ImGui.DragFloat3($"Translation##{title}", ref translation);

        Vector3 rotation = new(RotationX, RotationY, RotationZ);
        ImGui.DragFloat3($"Rotation##{title}", ref rotation);

        return new(
            translation.X,
            translation.Y,
            translation.Z,
            rotation.X,
            rotation.Y,
            rotation.Z
            );
    }

    public float[] ToArray() => new float[]
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
            offsetX += element.OffsetX * weight;
            offsetY += element.OffsetY * weight;
            offsetZ += element.OffsetZ * weight;
            rotationX += element.RotationX * weight;
            rotationY += element.RotationY * weight;
            rotationZ += element.RotationZ * weight;
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
}