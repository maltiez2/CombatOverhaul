using CombatOverhaul.Integration;
using CombatOverhaul.ItemsAnimations;
using ImGuiNET;
using Newtonsoft.Json.Linq;
using System.Reflection;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;
using VSImGui;
using VSImGui.API;

namespace CombatOverhaul.PlayerAnimations;

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
        _api = api;
    }

    private bool _showAnimationEditor = false;
    private int _selectedAnimationIndex = 0;
    private int _tempAnimations = 0;
    private bool _overwriteFrame = false;
    private readonly FirstPersonAnimationsBehavior _behavior;
    private readonly ICoreClientAPI _api;
    private string _itemAnimation = "";
    private string _animationKey = "";
    private readonly FieldInfo _mainCameraInfo = typeof(ClientMain).GetField("MainCamera", BindingFlags.NonPublic | BindingFlags.Instance);
    private readonly FieldInfo _cameraFov = typeof(Camera).GetField("Fov", BindingFlags.NonPublic | BindingFlags.Instance);

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
                float Amplitude = EyeHightController.Amplitude;
                ImGui.SliderFloat("Amplitude##effects", ref Amplitude, 0, 2);
                EyeHightController.Amplitude = Amplitude;

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

                float Offset = EyeHightController.Offset;
                ImGui.SliderFloat("Offset##effects", ref Offset, 0, 2);
                EyeHightController.Offset = Offset;

                float LiquidEffect = EyeHightController.LiquidEffect;
                ImGui.SliderFloat("LiquidEffect##effects", ref LiquidEffect, 0, 2);
                EyeHightController.LiquidEffect = LiquidEffect;

                EditFov();

                ImGui.EndTabItem();
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
        JsonObject json = JsonObject.FromJson(Encoding.UTF8.GetString(asset.Data));
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

        CreateFromItemAnimation();
        ImGui.Separator();

        if (ImGui.Button("Play") && Animations.Count > 0)
        {
            AnimationRequest request = new(
                Animations[codes[_selectedAnimationIndex]],
                1,
                1,
                "test",
                TimeSpan.FromSeconds(0.6),
                TimeSpan.FromSeconds(0.6),
                true
                );

            _behavior.Play(request);
        }
        ImGui.SameLine();

        if (ImGui.Button("Export to clipboard") && Animations.Count > 0)
        {
            ImGui.SetClipboardText(Animations[codes[_selectedAnimationIndex]].ToString());
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
                if (PlayerRenderingPatches.FpHandsOffset != PlayerRenderingPatches.DefaultFpHandsOffset)
                {
                    PlayerRenderingPatches.FpHandsOffset = PlayerRenderingPatches.DefaultFpHandsOffset;
                }
                else
                {
                    PlayerRenderingPatches.FpHandsOffset = 0;
                }
            }
            ImGui.Checkbox("Overwrite current frame", ref _overwriteFrame);
            Animations[codes[_selectedAnimationIndex]].Edit(codes[_selectedAnimationIndex]);
            if (_overwriteFrame)
            {
                _behavior.FrameOverride = Animations[codes[_selectedAnimationIndex]].StillFrame(Animations[codes[_selectedAnimationIndex]]._frameIndex);
            }
            else
            {
                _behavior.FrameOverride = null;
            }
        }
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

        bool canCreate = !Animations.ContainsKey(_animationKey);// && shape.AnimationsByCrc32.ContainsKey(GameMath.Crc32(_animationKey.ToLowerInvariant()));

        if (!canCreate) ImGui.BeginDisabled();
        if (ImGui.Button("Create##itemanimation"))
        {
            Animations.Add(_animationKey, new Animation(new PLayerKeyFrame[] { PLayerKeyFrame.Zero }, _itemAnimation, shape));
        }
        if (!canCreate) ImGui.EndDisabled();
    }


}