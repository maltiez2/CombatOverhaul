using CombatOverhaul.Integration;
using ImGuiNET;
using Newtonsoft.Json.Linq;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
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
                    _behavior.FrameOverride = Animations[codes[_selectedAnimationIndex]].StillFrame(Animations[codes[_selectedAnimationIndex]]._frameIndex);
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

            Animation animation = animationJson.AsObject<AnimationJson>().ToAnimation();

            result.Add($"{domain}:{code}", animation);
        }

        return result;
    }
}