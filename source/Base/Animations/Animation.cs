using CombatOverhaul.ItemsAnimations;
using ImGuiNET;
using Vintagestory.API.Common;

namespace CombatOverhaul.PlayerAnimations;

public sealed class Animation
{
    public List<PLayerKeyFrame> PlayerKeyFrames { get; private set; } = new();
    public List<ItemKeyFrame> ItemKeyFrames { get; private set; } = new();
    public List<TimeSpan> Durations { get; private set; } = new();
    public TimeSpan TotalDuration { get; private set; }

    public Animation(IEnumerable<PLayerKeyFrame> playerFrames, IEnumerable<ItemKeyFrame> itemFrames)
    {
        if (!playerFrames.Any()) throw new ArgumentException("Frames number should be at least 1");

        PlayerKeyFrames = playerFrames.ToList();
        ItemKeyFrames = itemFrames.ToList();

        CalculateDurations();
    }
    public Animation(IEnumerable<PLayerKeyFrame> playerFrames)
    {
        if (!playerFrames.Any()) throw new ArgumentException("Frames number should be at least 1");

        PlayerKeyFrames = playerFrames.ToList();

        CalculateDurations();
    }
    public Animation(IEnumerable<PLayerKeyFrame> playerFrames, string itemAnimation, Shape itemShape)
    {
        if (!playerFrames.Any()) throw new ArgumentException("Frames number should be at least 1");

        PlayerKeyFrames = playerFrames.ToList();
        ItemKeyFrames = ItemKeyFrame.FromVanillaAnimation(itemAnimation, itemShape);

        CalculateDurations();
    }

    public static readonly Animation Zero = new(new PLayerKeyFrame[] { PLayerKeyFrame.Zero });

    public PlayerItemFrame Interpolate(PlayerItemFrame previousAnimationFrame, TimeSpan currentDuration)
    {
        if (Finished(currentDuration)) return new(PlayerKeyFrames[^1].Frame, ItemKeyFrames.Any() ? ItemKeyFrames[^1].Frame : null);

        ItemFrame? itemFrame = InterpolateItemFrame(previousAnimationFrame, currentDuration);
        PlayerFrame playerFrame = InterpolatePlayerFrame(previousAnimationFrame, currentDuration);

        return new(playerFrame, itemFrame);
    }
    public bool Finished(TimeSpan currentDuration) => currentDuration >= TotalDuration;
    public void Edit(string title)
    {
        ImGui.Text($"Total duration: {(int)TotalDuration.TotalMilliseconds} ms");

        ImGui.BeginTabBar($"##{title}tab");
        if (ImGui.BeginTabItem($"Player animation##{title}"))
        {
            if (_frameIndex >= PlayerKeyFrames.Count) _frameIndex = PlayerKeyFrames.Count - 1;
            if (_frameIndex < 0) _frameIndex = 0;

            if (PlayerKeyFrames.Count > 0)
            {
                if (ImGui.Button($"Remove##{title}"))
                {
                    PlayerKeyFrames.RemoveAt(_frameIndex);
                }
                ImGui.SameLine();
            }

            if (ImGui.Button($"Insert##{title}"))
            {
                PlayerKeyFrames.Insert(_frameIndex, new(PlayerFrame.Zero, TimeSpan.Zero, EasingFunctionType.Linear));
            }

            if (PlayerKeyFrames.Count > 0) ImGui.SliderInt($"Key frame", ref _frameIndex, 0, PlayerKeyFrames.Count - 1);

            if (PlayerKeyFrames.Count > 0)
            {
                PLayerKeyFrame frame = PlayerKeyFrames[_frameIndex].Edit(title);
                PlayerKeyFrames[_frameIndex] = frame;
            }
            
            ImGui.EndTabItem();
        }
        if (ItemKeyFrames.Any() && ImGui.BeginTabItem($"Item animation##{title}"))
        {
            
            
            ImGui.EndTabItem();
        }
        ImGui.EndTabBar();

        CalculateDurations();
    }
    public override string ToString() => AnimationJson.FromAnimation(this).ToString();
    public PlayerItemFrame StillFrame(int playerFrame)
    {
        return Interpolate(PlayerItemFrame.Zero, Durations[playerFrame]);
    }
    public PlayerItemFrame StillFrame(float progress)
    {
        return Interpolate(PlayerItemFrame.Zero, progress * TotalDuration);
    }

    internal int _frameIndex = 0;

    private void CalculateDurations()
    {
        TotalDuration = TimeSpan.Zero;
        Durations.Clear();
        foreach (PLayerKeyFrame frame in PlayerKeyFrames)
        {
            TotalDuration += frame.EasingTime;
            Durations.Add(TotalDuration);
        }
    }
    private ItemFrame? InterpolateItemFrame(PlayerItemFrame previousAnimationFrame, TimeSpan currentDuration)
    {
        if (!ItemKeyFrames.Any()) return null;

        int nextItemKeyFrame;
        float progress = (float)(currentDuration / TotalDuration);
        for (nextItemKeyFrame = 0; nextItemKeyFrame < ItemKeyFrames.Count; nextItemKeyFrame++)
        {
            if (ItemKeyFrames[nextItemKeyFrame].DurationFraction > progress) break;
        }

        float itemFrameProgress;
        if (nextItemKeyFrame == 0)
        {
            itemFrameProgress = progress / ItemKeyFrames[0].DurationFraction;
        }
        else
        {
            float previousFrameProgress = ItemKeyFrames[nextItemKeyFrame - 1].DurationFraction;
            float nextFrameProgress = ItemKeyFrames[nextItemKeyFrame].DurationFraction;
            float progressRange = nextFrameProgress - previousFrameProgress;
            itemFrameProgress = (progress - previousFrameProgress) / progressRange;
        }

        if (nextItemKeyFrame == 0)
        {
            ItemFrame previousFrame = previousAnimationFrame.Item ?? ItemFrame.Empty;
            return ItemKeyFrames[0].Interpolate(previousFrame, itemFrameProgress);
        }
        else
        {
            return ItemKeyFrames[nextItemKeyFrame].Interpolate(ItemKeyFrames[nextItemKeyFrame - 1].Frame, itemFrameProgress);
        }
    }
    private PlayerFrame InterpolatePlayerFrame(PlayerItemFrame previousAnimationFrame, TimeSpan currentDuration)
    {
        int nextPlayerKeyFrame;
        for (nextPlayerKeyFrame = 0; nextPlayerKeyFrame < PlayerKeyFrames.Count; nextPlayerKeyFrame++)
        {
            if (Durations[nextPlayerKeyFrame] > currentDuration) break;
        }

        if (nextPlayerKeyFrame == 0)
        {
            return PlayerKeyFrames[0].Interpolate(previousAnimationFrame.Player, currentDuration);
        }
        else
        {
            return PlayerKeyFrames[nextPlayerKeyFrame].Interpolate(PlayerKeyFrames[nextPlayerKeyFrame - 1].Frame, currentDuration - Durations[nextPlayerKeyFrame - 1]);
        }
    }
}

public sealed class AnimationJson
{
    public PLayerKeyFrameJson[] PlayerKeyFrames { get; set; } = Array.Empty<PLayerKeyFrameJson>();
    public ItemKeyFrameJson[] ItemKeyFrames { get; set; } = Array.Empty<ItemKeyFrameJson>();

    public Animation ToAnimation()
    {
        return new(PlayerKeyFrames.Select(element => element.ToKeyFrame()), ItemKeyFrames.Select(element => element.ToKeyFrame()));
    }

    public static AnimationJson FromAnimation(Animation animation)
    {
        return new()
        {
            PlayerKeyFrames = animation.PlayerKeyFrames.Select(PLayerKeyFrameJson.FromKeyFrame).ToArray(),
            ItemKeyFrames = animation.ItemKeyFrames.Select(ItemKeyFrameJson.FromKeyFrame).ToArray()
        };
    }

    public override string ToString() => JsonUtil.ToPrettyString(this);
}

public sealed class ItemKeyFrameJson
{
    public float DurationFraction { get; set; }
    public Dictionary<string, float[]> Elements { get; set; } = new();

    public ItemKeyFrame ToKeyFrame()
    {
        return new(
                new ItemFrame(Elements.ToDictionary(entry => entry.Key, entry => new AnimationElement(entry.Value))),
                DurationFraction
            );
    }

    public static ItemKeyFrameJson FromKeyFrame(ItemKeyFrame frame)
    {
        ItemKeyFrameJson result = new()
        {
            DurationFraction = frame.DurationFraction,
            Elements = frame.Frame.Elements.ToDictionary(entry => entry.Key, entry => entry.Value.ToArray())
        };

        return result;
    }
}

public sealed class PLayerKeyFrameJson
{
    public float EasingTime { get; set; } = 0;
    public string EasingFunction { get; set; } = "Linear";
    public Dictionary<string, float[]> Elements { get; set; } = new();

    public PLayerKeyFrame ToKeyFrame()
    {
        TimeSpan time = TimeSpan.FromMilliseconds(EasingTime);
        EasingFunctionType function = Enum.Parse<EasingFunctionType>(EasingFunction);

        RightHandFrame? rightHand = null;
        if (Elements.ContainsKey("ItemAnchor") || Elements.ContainsKey("LowerArmR") || Elements.ContainsKey("UpperArmR"))
        {
            rightHand = new(
                Elements.ContainsKey("ItemAnchor") ? new AnimationElement(Elements["ItemAnchor"]) : AnimationElement.Zero,
                Elements.ContainsKey("LowerArmR") ? new AnimationElement(Elements["LowerArmR"]) : AnimationElement.Zero,
                Elements.ContainsKey("UpperArmR") ? new AnimationElement(Elements["UpperArmR"]) : AnimationElement.Zero
                );
        }

        LeftHandFrame? leftHand = null;
        if (Elements.ContainsKey("ItemAnchorL") || Elements.ContainsKey("LowerArmL") || Elements.ContainsKey("UpperArmL"))
        {
            leftHand = new(
                Elements.ContainsKey("ItemAnchorL") ? new AnimationElement(Elements["ItemAnchorL"]) : AnimationElement.Zero,
                Elements.ContainsKey("LowerArmL") ? new AnimationElement(Elements["LowerArmL"]) : AnimationElement.Zero,
                Elements.ContainsKey("UpperArmL") ? new AnimationElement(Elements["UpperArmL"]) : AnimationElement.Zero
                );
        }

        return new(
            new PlayerFrame(rightHand, leftHand),
            time,
            function
            );
    }

    public static PLayerKeyFrameJson FromKeyFrame(PLayerKeyFrame frame)
    {
        PLayerKeyFrameJson result = new()
        {
            EasingTime = (float)frame.EasingTime.TotalMilliseconds,
            EasingFunction = frame.EasingFunction.ToString()
        };

        if (frame.Frame.RightHand != null)
        {
            RightHandFrame rightHand = frame.Frame.RightHand.Value;

            result.Elements.Add("ItemAnchor", rightHand.ItemAnchor.ToArray());
            result.Elements.Add("LowerArmR", rightHand.LowerArmR.ToArray());
            result.Elements.Add("UpperArmR", rightHand.UpperArmR.ToArray());
        }

        if (frame.Frame.LeftHand != null)
        {
            LeftHandFrame leftHand = frame.Frame.LeftHand.Value;

            result.Elements.Add("ItemAnchorL", leftHand.ItemAnchorL.ToArray());
            result.Elements.Add("LowerArmL", leftHand.LowerArmL.ToArray());
            result.Elements.Add("UpperArmL", leftHand.UpperArmL.ToArray());
        }

        return result;
    }
}