using CombatOverhaul.Integration;
using ImGuiNET;
using Newtonsoft.Json.Linq;
using System.Numerics;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using VSImGui;
using VSImGui.API;

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
   
    public override void OnGameTick(float deltaTime)
    {
        int mainHandItemId = _player.RightHandItemSlot.Itemstack?.Item?.Id ?? 0;
        int offhandItemId = _player.LeftHandItemSlot.Itemstack?.Item?.Id ?? 0;

        if (_mainHandItemId != mainHandItemId)
        {
            _mainHandItemId = mainHandItemId;
            foreach (string category in _mainHandCategories)
            {
                _composer.Stop(category);
            }
            _mainHandCategories.Clear();
        }

        if (_offHandItemId != offhandItemId)
        {
            _offHandItemId = offhandItemId;
            foreach (string category in _offhandCategories)
            {
                _composer.Stop(category);
            }
            _offhandCategories.Clear();
        }
    }

    public Frame? FrameOverride { get; set; } = null;

    public void Play(AnimationRequest request, bool mainHand = true)
    {
        _composer.Play(request);
        if (mainHand)
        {
            _mainHandCategories.Add(request.Category);
        }
        else
        {
            _offhandCategories.Add(request.Category);
        }
    }

    private readonly Composer _composer = new();
    private readonly EntityPlayer _player;
    private Frame _lastFrame = Frame.Empty;
    private readonly List<string> _offhandCategories = new();
    private readonly List<string> _mainHandCategories = new();
    private int _offHandItemId = 0;
    private int _mainHandItemId = 0;

    private void OnBeforeFrame(Entity entity, float dt)
    {
        if (entity.EntityId != _player.EntityId) return;
        _lastFrame = _composer.Compose(TimeSpan.FromSeconds(dt));
    }

    private void OnFrame(Entity entity, ElementPose pose)
    {
        if (entity.EntityId != _player.EntityId) return;
        if (FrameOverride != null)
        {
            FrameOverride.Value.Apply(pose);
        }
        else
        {
            _lastFrame.Apply(pose);
        }
    }
}

public sealed class AnimationsManager
{
    public Dictionary<string, Animation> Animations { get; private set; }

    public AnimationsManager(ICoreClientAPI api)
    {
        List<IAsset> animations = api.Assets.GetManyInCategory("config", "animations");

        Dictionary<string, Animation> animationsByCode = new();
        foreach (Dictionary<string, Animation> assetAnimations in animations.Select(FromAsset))
        {
            foreach ((string code, Animation animation) in assetAnimations)
            {
                animationsByCode.Add(code, animation);
            }
        }

        Animations = animationsByCode;

        api.ModLoader.GetModSystem<ImGuiModSystem>().Draw += DrawEditor;
        api.Input.RegisterHotKey("combatOverhaul_editor", "Show animation editor", GlKeys.L, ctrlPressed: true);
        api.Input.SetHotKeyHandler("combatOverhaul_editor", keys => _showAnimationEditor = !_showAnimationEditor);

        _behavior = api.World.Player.Entity.GetBehavior<FirstPersonAnimationsBehavior>();
    }

    private bool _showAnimationEditor = false;
    private int _selectedAnimationIndex = 0;
    private int _tempAnimations = 0;
    private bool _overwriteFrame = false;
    private readonly FirstPersonAnimationsBehavior _behavior;

    private CallbackGUIStatus DrawEditor(float deltaSeconds)
    {
        if (!_showAnimationEditor) return CallbackGUIStatus.Closed;

        if (ImGui.Begin("Combat Overhaul - Animations editor", ref _showAnimationEditor))
        {
            string[] codes = Animations.Keys.ToArray();

            if (ImGui.Button("Play") && Animations.Count > 0)
            {
                AnimationRequest request = new(
                    Animations[codes[_selectedAnimationIndex]],
                    1,
                    1,
                    "test",
                    TimeSpan.FromSeconds(0.2),
                    TimeSpan.FromSeconds(0.5),
                    true
                    );

                _behavior.Play(request);
            }
            ImGui.SameLine();

            if (ImGui.Button("Export to clipboard") && Animations.Count > 0)
            {
                ImGui.SetClipboardText(Animations[codes[_selectedAnimationIndex]].ToJsonString());
            }
            ImGui.SameLine();

            VSImGui.ListEditor.Edit(
                "Animations",
                codes,
                ref _selectedAnimationIndex,
                onRemove: (value, index) => Animations.Remove(value),
                onAdd: key =>
                {
                    Animations.Add($"temp_{++_tempAnimations}", Animation.Zero);
                    return $"temp_{_tempAnimations}";
                }
                );

            codes = Animations.Keys.ToArray();

            ImGui.Separator();

            if (_selectedAnimationIndex < Animations.Count)
            {
                if (ImGui.Button("Toggle rendering offset"))
                {
                    if (RenderingOffset.FpHandsOffset != RenderingOffset.DefaultFpHandsOffset)
                    {
                        RenderingOffset.FpHandsOffset = RenderingOffset.DefaultFpHandsOffset;
                    }
                    else
                    {
                        RenderingOffset.FpHandsOffset = 0;
                    }
                }
                ImGui.Checkbox("Overwrite current frame", ref _overwriteFrame);
                Animations[codes[_selectedAnimationIndex]].Edit(codes[_selectedAnimationIndex]);
                if (_overwriteFrame)
                {
                    _behavior.FrameOverride = Animations[codes[_selectedAnimationIndex]].KeyFrames[Animations[codes[_selectedAnimationIndex]]._frameIndex].Frame;
                }
                else
                {
                    _behavior.FrameOverride = null;
                }
            }

            ImGui.End();
        }

        return _showAnimationEditor ? CallbackGUIStatus.GrabMouse : CallbackGUIStatus.Closed;
    }
    private static Dictionary<string, Animation> FromAsset(IAsset asset)
    {
        Dictionary<string, Animation> result = new();

        string domain = asset.Location.Domain;
        JsonObject json = JsonObject.FromJson(Encoding.UTF8.GetString(asset.Data));
        foreach (KeyValuePair<string, JToken?> entry in json.Token as JObject)
        {
            string code = entry.Key;
            JsonObject animationJson = new(entry.Value);

            Animation animation = Animation.FromJson(animationJson.AsArray());

            result.Add($"{domain}:{code}", animation);
        }

        return result;
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

    public AnimationRequest(Animation animation, float animationSpeed, float weight, string category, TimeSpan easeOutDuration, TimeSpan easeInDuration, bool easeOut)
    {
        Animation = animation;
        AnimationSpeed = animationSpeed;
        Weight = weight;
        Category = category;
        EaseOutDuration = easeOutDuration;
        EaseInDuration = easeInDuration;
        EaseOut = easeOut;
    }
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
    public List<KeyFrame> KeyFrames { get; private set; }
    public List<TimeSpan> Durations { get; private set; } = new();
    public TimeSpan TotalDuration { get; private set; }

    public Animation(IEnumerable<KeyFrame> frames)
    {
        if (!frames.Any()) throw new ArgumentException("Frames number should be at least 1");

        KeyFrames = frames.ToList();

        CalculateDurations();
    }

    public static readonly Animation Zero = new(new KeyFrame[] { KeyFrame.Zero });

    public Frame Interpolate(Frame previousAnimationFrame, TimeSpan currentDuration)
    {
        if (Finished(currentDuration)) return KeyFrames[^1].Frame;

        int nextKeyFrame;
        for (nextKeyFrame = 0; nextKeyFrame < KeyFrames.Count - 1; nextKeyFrame++)
        {
            if (Durations[nextKeyFrame + 1] > currentDuration) break;
        }

        if (nextKeyFrame == 0) return KeyFrames[0].Interpolate(previousAnimationFrame, currentDuration);

        return KeyFrames[nextKeyFrame].Interpolate(KeyFrames[nextKeyFrame - 1].Frame, currentDuration - Durations[nextKeyFrame - 1]);
    }

    public bool Finished(TimeSpan currentDuration) => currentDuration >= TotalDuration;

    public void Edit(string title)
    {
        ImGui.Text($"Total duration: {(int)TotalDuration.TotalMilliseconds} ms");

        if (_frameIndex >= KeyFrames.Count) _frameIndex = KeyFrames.Count - 1;
        if (_frameIndex < 0) _frameIndex = 0;

        if (KeyFrames.Count > 0)
        {
            if (ImGui.Button($"Remove##{title}"))
            {
                KeyFrames.RemoveAt(_frameIndex);
            }
            ImGui.SameLine();
        }

        if (ImGui.Button($"Insert##{title}"))
        {
            KeyFrames.Insert(_frameIndex, new(Frame.Zero, TimeSpan.Zero, EasingFunctionType.Linear));
        }

        if (KeyFrames.Count > 0) ImGui.SliderInt($"Key frame", ref _frameIndex, 0, KeyFrames.Count - 1);

        if (KeyFrames.Count > 0)
        {
            KeyFrame frame = KeyFrames[_frameIndex].Edit(title);
            KeyFrames[_frameIndex] = frame;
        }
    }

    public JsonObject[] ToJson()
    {
        return KeyFrames
            .Select(KeyFrameJson.FromKeyFrame)
            .Select(JsonUtil.ToPrettyString)
            .Select(JsonObject.FromJson)
            .ToArray();
    }

    public string ToJsonString()
    {
        KeyFrameJson[] keyFrames = KeyFrames
            .Select(KeyFrameJson.FromKeyFrame)
            .ToArray();

        AnimationJson toJson = new()
        {
            KeyFrames = keyFrames
        };

        return JsonUtil.ToPrettyString(toJson);
    }

    public static Animation FromJson(JsonObject[] json)
    {
        return new(json.Select(frame => frame.AsObject<KeyFrameJson>().ToKeyFrame()));
    }

    internal int _frameIndex = 0;

    private void CalculateDurations()
    {
        TotalDuration = TimeSpan.Zero;
        Durations.Clear();
        foreach (KeyFrame frame in KeyFrames)
        {
            TotalDuration += frame.EasingTime;
            Durations.Add(TotalDuration);
        }
    }
}

public sealed class AnimationJson
{
    public KeyFrameJson[] KeyFrames { get; set; } = Array.Empty<KeyFrameJson>();
}

public sealed class KeyFrameJson
{
    public float EasingTime { get; set; } = 0;
    public string EasingFunction { get; set; } = "Linear";
    public Dictionary<string, float[]> Elements { get; set; } = new();

    public KeyFrame ToKeyFrame()
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
            new Frame(rightHand, leftHand),
            time,
            function
            );
    }

    public static KeyFrameJson FromKeyFrame(KeyFrame frame)
    {
        KeyFrameJson result = new()
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

    public KeyFrame Edit(string title)
    {
        int milliseconds = (int)EasingTime.TotalMilliseconds;
        ImGui.DragInt($"Easing time##{title}", ref milliseconds);

        EasingFunctionType function = VSImGui.EnumEditor<EasingFunctionType>.Combo($"Easing function##{title}", EasingFunction);

        ImGui.SeparatorText("Key frame");

        Frame frame = Frame.Edit(title);

        return new(frame, TimeSpan.FromMilliseconds(milliseconds), function);
    }
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

    public Frame Edit(string title)
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
        pose.translateX = OffsetX;
        pose.translateY = OffsetY;
        pose.translateZ = OffsetZ;
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