using CombatOverhaul.Colliders;
using CombatOverhaul.Integration;
using CombatOverhaul.Utils;
using ImGuiNET;
using Newtonsoft.Json.Linq;
using System.Numerics;
using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.Client.NoObf;
using VSImGui;
using VSImGui.API;

namespace CombatOverhaul.Animations;

public sealed class AnimationsManager
{
    public Dictionary<string, Animation> Animations { get; private set; } = new();
    public static bool PlayAnimationsInThirdPerson { get; set; } = false;
    public static bool RenderDebugColliders { get; set; } = false;

    public AnimationsManager(ICoreClientAPI api, ParticleEffectsManager particleEffectsManager)
    {
#if DEBUG
        api.ModLoader.GetModSystem<ImGuiModSystem>().Draw += DrawEditor;
#endif
        api.Input.RegisterHotKey("combatOverhaul_editor", "Show animation editor", GlKeys.L, ctrlPressed: true);
        api.Input.SetHotKeyHandler("combatOverhaul_editor", keys => _showAnimationEditor = !_showAnimationEditor);
        _instance = this;

        _api = api;
        _particleEffectsManager = particleEffectsManager;
    }
    public void Load()
    {
        List<IAsset> animations = _api.Assets.GetManyInCategory("config", "animations");

        Dictionary<string, Animation> animationsByCode = new();
        foreach (Dictionary<string, Animation> assetAnimations in animations.Select(FromAsset))
        {
            foreach ((string code, Animation animation) in assetAnimations)
            {
                animationsByCode.Add(code, animation);
            }
        }

        Animations = animationsByCode;

        _behavior = _api.World.Player.Entity.GetBehavior<FirstPersonAnimationsBehavior>();
    }

    public static void RegisterTransformByCode(ModelTransform transform, string code)
    {
        _instance.RegisterTransform(transform, code);
    }
    public void RegisterTransform(ModelTransform transform, string code)
    {
        _transforms[code] = transform;
    }

    private bool _showAnimationEditor = false;
    private int _selectedAnimationIndex = 0;
    private int _selectedAnimationIndexFiltered = 0;
    private bool _overwriteFrame = false;
    private FirstPersonAnimationsBehavior? _behavior;
    private readonly ICoreClientAPI _api;
    private string _itemAnimation = "";
    private string _animationKey = "";
    private readonly FieldInfo _mainCameraInfo = typeof(ClientMain).GetField("MainCamera", BindingFlags.NonPublic | BindingFlags.Instance);
    private readonly FieldInfo _cameraFov = typeof(Camera).GetField("Fov", BindingFlags.NonPublic | BindingFlags.Instance);
    private string _playerAnimationKey = "";
    private float _animationSpeed = 1;
    private ParticleEffectsManager _particleEffectsManager;
    private AnimationJson _animationBuffer;
    private static AnimationsManager _instance;

    private string _animationsFilter = "";
    private string _filter = "";
    private int _transformIndex = 0;
    private readonly Dictionary<string, ModelTransform> _transforms = new();
    private static Dictionary<string, Animation> FromAsset(IAsset asset)
    {
        Dictionary<string, Animation> result = new();

        string domain = asset.Location.Domain;
        JsonObject json = JsonObject.FromJson(asset.ToText());
        foreach (KeyValuePair<string, JToken?> entry in json.Token as JObject)
        {
            string code = entry.Key;
            JsonObject animationJson = new(entry.Value);

            Animation animation = animationJson.AsObject<AnimationJson>().ToAnimation();

            result.Add($"{domain}:{code}", animation);
        }

        return result;
    }

#if DEBUG
    private CallbackGUIStatus DrawEditor(float deltaSeconds)
    {
        if (!_showAnimationEditor) return CallbackGUIStatus.Closed;

        if (ImGui.Begin("Combat Overhaul - Animations editor", ref _showAnimationEditor))
        {
            ImGui.BeginTabBar($"##main_tab_bar");
            if (ImGui.BeginTabItem($"Animations"))
            {
                AnimationsTab();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem($"Transforms"))
            {
                TransformEditorTab();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem($"Camera movement effects"))
            {
                /*float Amplitude = EyeHightController.Amplitude;
                ImGui.SliderFloat("Amplitude##effects", ref Amplitude, 0, 2);
                EyeHightController.Amplitude = Amplitude;*/

                float Frequency = EyeHightController.Frequency;
                ImGui.SliderFloat("Frequency##effects", ref Frequency, 0, 2);
                EyeHightController.Frequency = Frequency;

                float SprintFrequencyEffect = EyeHightController.SprintFrequencyEffect;
                ImGui.SliderFloat("SprintFrequencyEffect##effects", ref SprintFrequencyEffect, 0, 2);
                EyeHightController.SprintFrequencyEffect = SprintFrequencyEffect;

                float SprintAmplitudeEffect = EyeHightController.SprintAmplitudeEffect;
                ImGui.SliderFloat("SprintAmplitudeEffect##effects", ref SprintAmplitudeEffect, 0, 2);
                EyeHightController.SprintAmplitudeEffect = SprintAmplitudeEffect;

                float SneakEffect = EyeHightController.SneakEffect;
                ImGui.SliderFloat("SneakEffect##effects", ref SneakEffect, 0.5f, 2);
                EyeHightController.SneakEffect = SneakEffect;

                float Offset = EyeHightController.OffsetMultiplier;
                ImGui.SliderFloat("Offset##effects", ref Offset, 0, 2);
                EyeHightController.OffsetMultiplier = Offset;

                float LiquidEffect = EyeHightController.LiquidEffect;
                ImGui.SliderFloat("LiquidEffect##effects", ref LiquidEffect, 0, 2);
                EyeHightController.LiquidEffect = LiquidEffect;

                //EditFov();

                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Particle effects##tab"))
            {
                _particleEffectsManager.Draw("particle-effects");
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Debug##tab"))
            {
                bool collidersRender = CollidersEntityBehavior.RenderColliders;
                ImGui.Checkbox("Render entities colliders", ref collidersRender);
                CollidersEntityBehavior.RenderColliders = collidersRender;

                bool debugColliders = RenderDebugColliders;
                ImGui.Checkbox("Render debug colliders", ref debugColliders);
                RenderDebugColliders = debugColliders;

                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();

            ImGui.End();
        }

        return _showAnimationEditor ? CallbackGUIStatus.GrabMouse : CallbackGUIStatus.Closed;
    }
    
    private void EditFov()
    {
        ClientMain? client = _api.World as ClientMain;
        if (client == null) return;

        PlayerCamera? camera = (PlayerCamera?)_mainCameraInfo.GetValue(client);
        if (camera == null) return;

        float? fovField = (float?)_cameraFov.GetValue(camera);
        if (fovField == null) return;

        float fovMultiplier = PlayerRenderingPatches.HandsFovMultiplier;
        ImGui.SliderFloat("FOV", ref fovMultiplier, 0.5f, 1.5f);
        PlayerRenderingPatches.HandsFovMultiplier = fovMultiplier;
        _cameraFov.SetValue(camera, ClientSettings.FieldOfView * GameMath.DEG2RAD * fovMultiplier);

        ImGui.Text($"FOV: {ClientSettings.FieldOfView * fovMultiplier}");
    }

    private void AnimationsTab()
    {
        string[] codes = Animations.Keys.ToArray();

        if (ImGui.Button("Save to buffer"))
        {
            _animationBuffer = AnimationJson.FromAnimation(Animations[codes[_selectedAnimationIndex]]);
        }
        ImGui.SameLine();

        if (ImGui.Button("Load from buffer"))
        {
            Animations[codes[_selectedAnimationIndex]] = _animationBuffer.ToAnimation();
        }

        if (ImGui.Button("Save buffer to file"))
        {
            _api.StoreModConfig(_animationBuffer, "co-animation-export.json");
        }
        ImGui.SameLine();
        if (ImGui.Button("Load buffer from file"))
        {
            _animationBuffer = _api.LoadModConfig<AnimationJson>("co-animation-export.json");
        }

        if (ImGui.Button("Toggle rendering offset"))
        {
            if (PlayerRenderingPatches.FpHandsOffset != PlayerRenderingPatches.DefaultFpHandsOffset)
            {
                PlayerRenderingPatches.FpHandsOffset = PlayerRenderingPatches.DefaultFpHandsOffset;
            }
            else
            {
                PlayerRenderingPatches.FpHandsOffset = 0;
            }
        }
        ImGui.SameLine();

        bool tpAnimations = PlayAnimationsInThirdPerson;
        ImGui.Checkbox("Third person animations", ref tpAnimations);
        PlayAnimationsInThirdPerson = tpAnimations;

        ImGui.InputTextWithHint("Filter##" + "animations", "supports wildcards", ref _animationsFilter, 200);
        EditorsUtils.FilterElements(_animationsFilter, Animations.Keys, out IEnumerable<string> filtered, out IEnumerable<int> indexes);

        ImGui.ListBox("transforms", ref _selectedAnimationIndexFiltered, filtered.ToArray(), filtered.Count());

        if (!filtered.Any()) return;

        if (_selectedAnimationIndexFiltered > filtered.Count()) _selectedAnimationIndexFiltered = 0;

        _selectedAnimationIndex = Animations.Keys.ToArray().IndexOf(filtered.ToArray()[_selectedAnimationIndexFiltered]);

        /*if (ImGui.Button("Remove##animations"))
        {
            Animations.Remove(Animations.Keys.ToArray()[_selectedAnimationIndex]);
            _selectedAnimationIndex--;
            if (_selectedAnimationIndex < 0) _selectedAnimationIndex = 0;
        }*/

        codes = Animations.Keys.ToArray();

        if (ImGui.CollapsingHeader($"Add animation"))
        {
            CreateAnimationGui();
        }

        if (_selectedAnimationIndex < Animations.Count)
        {
            ImGui.SeparatorText("Animation");

            if (ImGui.Button("Play") && Animations.Count > 0)
            {
                AnimationRequest request = new(
                    Animations[codes[_selectedAnimationIndex]],
                    _animationSpeed,
                    1,
                    "main",
                    TimeSpan.FromSeconds(0.6),
                    TimeSpan.FromSeconds(0.6),
                    true
                    );

                _behavior?.Play(request);
            }
            ImGui.SameLine();

            if (ImGui.Button("Export to clipboard") && Animations.Count > 0)
            {
                ImGui.SetClipboardText(Animations[codes[_selectedAnimationIndex]].ToString());
            }
            ImGui.SameLine();

            ImGui.SetNextItemWidth(200);
            ImGui.SliderFloat("Animation speed", ref _animationSpeed, 0.1f, 2);
            ImGui.Checkbox("Overwrite current frame", ref _overwriteFrame);
            Animations[codes[_selectedAnimationIndex]].Edit(codes[_selectedAnimationIndex]);
            if (_overwriteFrame)
            {
                if (Animations[codes[_selectedAnimationIndex]]._playerFrameEdited)
                {
                    _behavior.FrameOverride = Animations[codes[_selectedAnimationIndex]].StillPlayerFrame(Animations[codes[_selectedAnimationIndex]]._playerFrameIndex, Animations[codes[_selectedAnimationIndex]]._frameProgress);
                }
                else
                {
                    _behavior.FrameOverride = Animations[codes[_selectedAnimationIndex]].StillItemFrame(Animations[codes[_selectedAnimationIndex]]._itemFrameIndex, Animations[codes[_selectedAnimationIndex]]._frameProgress);
                }
            }
            else
            {
                _behavior.FrameOverride = null;
            }
        }
    }

    private void TransformEditorTab()
    {
        ImGui.InputTextWithHint("Filter##" + "transforms", "supports wildcards", ref _filter, 200);
        EditorsUtils.FilterElements(_filter, _transforms.Keys, out IEnumerable<string> filtered, out IEnumerable<int> indexes);

        ImGui.ListBox("transforms", ref _transformIndex, filtered.ToArray(), filtered.Count());

        if (!filtered.Any()) return;

        string currentTransform = filtered.ElementAt(_transformIndex);

        if (!_transforms.ContainsKey(currentTransform)) return;

        ModelTransform transform = _transforms[currentTransform];

        if (ImGui.Button($"Export to clipboard"))
        {
            ImGui.SetClipboardText(JsonUtil.ToPrettyString(transform));
        }

        float speed = ImGui.GetIO().KeysDown[(int)ImGuiKey.LeftShift] ? 0.1f : 1;

        float scale = transform.ScaleXYZ.X;
        ImGui.DragFloat("Scale##transform", ref scale);
        transform.Scale = scale;

        Vector3 translation = new(transform.Translation.X, transform.Translation.Y, transform.Translation.Z);
        Vector3 origin = new(transform.Origin.X, transform.Origin.Y, transform.Origin.Z);
        Vector3 rotation = new(transform.Rotation.X, transform.Rotation.Y, transform.Rotation.Z);

        ImGui.DragFloat3("Translation##transform", ref translation, speed);
        ImGui.DragFloat3("Origin##transform", ref origin, speed);
        ImGui.DragFloat3("Rotation##transform", ref rotation, speed);

        transform.Translation.X = translation.X;
        transform.Translation.Y = translation.Y;
        transform.Translation.Z = translation.Z;
        transform.Origin.X = origin.X;
        transform.Origin.Y = origin.Y;
        transform.Origin.Z = origin.Z;
        transform.Rotation.X = rotation.X;
        transform.Rotation.Y = rotation.Y;
        transform.Rotation.Z = rotation.Z;
    }

    private void CreateAnimationGui()
    {
        ImGui.Indent();
        ImGui.SeparatorText("Just player");

        ImGui.InputText("Animation code##playeranimation", ref _playerAnimationKey, 300);

        bool canAddAnimation = !Animations.ContainsKey(_playerAnimationKey) && _playerAnimationKey != "";
        if (!canAddAnimation) ImGui.BeginDisabled();
        if (ImGui.Button($"Create##playeranimation"))
        {
            Animations.Add(_playerAnimationKey, new Animation(new PLayerKeyFrame[] { PLayerKeyFrame.Zero }));
        }
        if (!canAddAnimation) ImGui.EndDisabled();

        ImGui.SeparatorText("Item + Player");
        CreateFromItemAnimation();
        ImGui.Unindent();
    }
    private void CreateFromItemAnimation()
    {
        Item? item = _api.World.Player.Entity.RightHandItemSlot.Itemstack?.Item;
        if (item == null)
        {
            ImGui.Text("Take item in right hand");
            return;
        }

        Animatable? behavior = item.GetCollectibleBehavior(typeof(Animatable), true) as Animatable;
        if (behavior == null)
        {
            ImGui.Text("Take item with animatable behavior in right hand");
            return;
        }

        Shape? shape = behavior.CurrentShape;
        if (shape == null)
        {
            ImGui.Text("Take item with animatable behavior in right hand");
            return;
        }

        ImGui.InputText($"Item animation code", ref _itemAnimation, 300);
        ImGui.InputText($"New animation code", ref _animationKey, 300);

        bool canCreate = !Animations.ContainsKey(_animationKey);

        if (!canCreate) ImGui.BeginDisabled();
        if (ImGui.Button("Create##itemanimation"))
        {
            try
            {
                Animations.Add(_animationKey, new Animation(new PLayerKeyFrame[] { PLayerKeyFrame.Zero }, _itemAnimation, shape));
            }
            catch (Exception exception)
            {
                LoggerUtil.Warn(_api, this, $"Error on creating animation: {exception}");
            }
        }
        if (!canCreate) ImGui.EndDisabled();
    }
#endif
}