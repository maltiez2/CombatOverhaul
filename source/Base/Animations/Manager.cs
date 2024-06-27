using CombatOverhaul.Colliders;
using CombatOverhaul.Integration;
using CombatOverhaul.Utils;
using ImGuiNET;
using Newtonsoft.Json.Linq;
using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;
using VSImGui;
using VSImGui.API;

namespace CombatOverhaul.Animations;

public sealed class AnimationsManager
{
    public Dictionary<string, Animation> Animations { get; private set; } = new();
    public AnimationsManager(ICoreClientAPI api)
    {
        api.ModLoader.GetModSystem<ImGuiModSystem>().Draw += DrawEditor;
        api.Input.RegisterHotKey("combatOverhaul_editor", "Show animation editor", GlKeys.L, ctrlPressed: true);
        api.Input.SetHotKeyHandler("combatOverhaul_editor", keys => _showAnimationEditor = !_showAnimationEditor);

        _api = api;
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

    private bool _showAnimationEditor = false;
    private int _selectedAnimationIndex = 0;
    private bool _overwriteFrame = false;
    private FirstPersonAnimationsBehavior? _behavior;
    private readonly ICoreClientAPI _api;
    private string _itemAnimation = "";
    private string _animationKey = "";
    private readonly FieldInfo _mainCameraInfo = typeof(ClientMain).GetField("MainCamera", BindingFlags.NonPublic | BindingFlags.Instance);
    private readonly FieldInfo _cameraFov = typeof(Camera).GetField("Fov", BindingFlags.NonPublic | BindingFlags.Instance);
    private string _playerAnimationKey = "";
    private float _animationSpeed = 1;

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
            if (ImGui.BeginTabItem("Debug##tab"))
            {
                bool collidersRender = CollidersEntityBehavior.RenderColliders;
                ImGui.Checkbox("Render entities colliders", ref collidersRender);
                CollidersEntityBehavior.RenderColliders = collidersRender;
            }
            ImGui.EndTabBar();

            ImGui.End();
        }

        return _showAnimationEditor ? CallbackGUIStatus.GrabMouse : CallbackGUIStatus.Closed;
    }
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

        ImGui.ListBox("Animations", ref _selectedAnimationIndex, Animations.Keys.ToArray(), Animations.Count);

        if (ImGui.Button("Remove##animations"))
        {
            Animations.Remove(Animations.Keys.ToArray()[_selectedAnimationIndex]);
            _selectedAnimationIndex--;
            if (_selectedAnimationIndex < 0) _selectedAnimationIndex = 0;
        }

        codes = Animations.Keys.ToArray();

        if (ImGui.CollapsingHeader($"Add animation"))
        {
            CreateAnimationGui();
        }

        ImGui.Separator();

        if (_selectedAnimationIndex < Animations.Count)
        {
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
            ImGui.Checkbox("Overwrite current frame", ref _overwriteFrame);
            ImGui.SeparatorText("Animation");
            Animations[codes[_selectedAnimationIndex]].Edit(codes[_selectedAnimationIndex]);
            if (_overwriteFrame)
            {
                if (Animations[codes[_selectedAnimationIndex]]._playerFrameEdited)
                {
                    _behavior.FrameOverride = Animations[codes[_selectedAnimationIndex]].StillPlayerFrame(Animations[codes[_selectedAnimationIndex]]._playerFrameIndex);
                }
                else
                {
                    _behavior.FrameOverride = Animations[codes[_selectedAnimationIndex]].StillItemFrame(Animations[codes[_selectedAnimationIndex]]._itemFrameIndex);
                }
            }
            else
            {
                _behavior.FrameOverride = null;
            }
        }
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
            Animations.Add(_playerAnimationKey, Animation.Zero);
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
}