using CombatOverhaul.Colliders;
using CombatOverhaul.Integration;
using CombatOverhaul.MeleeSystems;
using CombatOverhaul.Utils;
using ImGuiNET;
using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.Client.NoObf;
using VSImGui;
using VSImGui.API;

namespace CombatOverhaul.Animations;

public sealed class DebugWindowManager
{
    public static bool PlayAnimationsInThirdPerson { get; set; } = false;
    public static bool RenderDebugColliders { get; set; } = false;

    public DebugWindowManager(ICoreClientAPI api, ParticleEffectsManager particleEffectsManager)
    {
#if DEBUG
        api.ModLoader.GetModSystem<ImGuiModSystem>().Draw += DrawEditor;
#endif
        api.Input.RegisterHotKey("combatOverhaul_editor", "Show animation editor", GlKeys.L, ctrlPressed: true);
        api.Input.SetHotKeyHandler("combatOverhaul_editor", keys => _showAnimationEditor = !_showAnimationEditor);
        _instance = this;

        _api = api;
        _particleEffectsManager = particleEffectsManager;
        _colliders.Clear();
    }

    public void Load(ICoreClientAPI api)
    {
        _behavior = api.World.Player.Entity.GetBehavior<FirstPersonAnimationsBehavior>();
    }

    public static void RegisterTransformByCode(ModelTransform transform, string code)
    {
        _instance.RegisterTransform(transform, code);
    }
    public void RegisterTransform(ModelTransform transform, string code)
    {
        _transforms[code] = transform;
    }

    public static void RegisterCollider(string item, string type, MeleeDamageType collider)
    {
        if (!_colliders.ContainsKey(item))
        {
            _colliders.Add(item, new());
        }

        _colliders[item].Add(type, (value => collider.RelativeCollider = value, () => collider.RelativeCollider));
    }
    public static void RegisterCollider(string item, string type, Action<LineSegmentCollider> setter, System.Func<LineSegmentCollider> getter)
    {
        if (!_colliders.ContainsKey(item))
        {
            _colliders.Add(item, new());
        }

        _colliders[item].Add(type, (setter, getter));
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
    internal static DebugWindowManager _instance;

    private string _animationsFilter = "";
    private string _filter = "";
    private string _collidersItemsFilter = "";
    private int _transformIndex = 0;
    private int _colliderItemIndex = 0;
    private int _colliderIndex = 0;
    private readonly Dictionary<string, ModelTransform> _transforms = new();
    private static Dictionary<string, Dictionary<string, (Action<LineSegmentCollider> setter, System.Func<LineSegmentCollider> getter)>> _colliders = new();
    internal static LineSegmentCollider? _currentCollider = null;

#if DEBUG
    private CallbackGUIStatus DrawEditor(float deltaSeconds)
    {
        _currentCollider = null;
        if (!_showAnimationEditor) return CallbackGUIStatus.Closed;

        if (ImGui.Begin("Combat Overhaul - Animations editor and debug tools", ref _showAnimationEditor))
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
            if (ImGui.BeginTabItem("Particle effects##tab"))
            {
                _particleEffectsManager.Draw("particle-effects");
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Colliders##tab"))
            {
                bool debugColliders = RenderDebugColliders;
                ImGui.Checkbox("Render weapon colliders", ref debugColliders);
                RenderDebugColliders = debugColliders;

                ImGui.InputText("Items filter##colliders", ref _collidersItemsFilter, 200);
                VSImGui.EditorsUtils.FilterElements(_collidersItemsFilter, _colliders.Keys, out IEnumerable<string> filteredItems, out _);
                if (_colliderItemIndex > filteredItems.Count())
                {
                    _colliderItemIndex = 0;
                }
                if (filteredItems.Count() != 0)
                {
                    ImGui.ListBox("Items##colliders", ref _colliderItemIndex, filteredItems.ToArray(), filteredItems.Count());
                    string selectedItem = filteredItems.ToArray()[_colliderItemIndex];

                    Dictionary<string, (Action<LineSegmentCollider> setter, Func<LineSegmentCollider> getter)> selectedColliders = _colliders[selectedItem];

                    string[] collidersTypes = selectedColliders.Select(entry => entry.Key).ToArray();

                    ImGui.ListBox("Colliders##colliders", ref _colliderIndex, collidersTypes, collidersTypes.Length);

                    if (collidersTypes.Length > 0)
                    {
                        (Action<LineSegmentCollider> setter, Func<LineSegmentCollider> getter) = selectedColliders[collidersTypes[_colliderIndex]];
                        System.Numerics.Vector3 position = getter().Position.toSystem();
                        System.Numerics.Vector3 direction = getter().Direction.toSystem();

                        float sliderSpeed = ImGui.IsKeyPressed(ImGuiKey.LeftShift) ? 0.01f : 0.1f;

                        ImGui.DragFloat3("Position##colliders", ref position, sliderSpeed);
                        ImGui.DragFloat3("Direction##colliders", ref direction, sliderSpeed);

                        _currentCollider = new(position.toOpenTK(), direction.toOpenTK());

                        setter(_currentCollider.Value);

                        System.Numerics.Vector3 head = position + direction;

                        string json = $"[{position.X}, {position.Y}, {position.Z}, {head.X}, {head.Y}, {head.Z}]";
                        if (ImGui.Button("To clipboard##colliders"))
                        {
                            ImGui.SetClipboardText(json);
                        }
                        ImGui.SameLine();
                        ImGui.Text($"JSON: {json}");
                    }
                }

                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Debug##tab"))
            {
                bool collidersRender = CollidersEntityBehavior.RenderColliders;
                ImGui.Checkbox("Render entities colliders", ref collidersRender);
                CollidersEntityBehavior.RenderColliders = collidersRender;



                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Generic Display##tab"))
            {
                GenericDisplayTab();
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
        string[] codes = AnimationsManager._instance.Animations.Keys.ToArray();

        if (ImGui.Button("Save to buffer"))
        {
            _animationBuffer = AnimationJson.FromAnimation(AnimationsManager._instance.Animations[codes[_selectedAnimationIndex]]);
        }
        ImGui.SameLine();

        if (ImGui.Button("Load from buffer"))
        {
            AnimationsManager._instance.Animations[codes[_selectedAnimationIndex]] = _animationBuffer.ToAnimation();
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

        if (ImGui.Button("Render fp model in tp"))
        {
            _api.World.Player.Entity.ActiveHandItemSlot.Itemstack?.Collectible?.GetCollectibleBehavior<AnimatableAttachable>(true)?.SetSwitchModels(_api.World.Player.Entity.EntityId, true);
        }
        ImGui.SameLine();
        if (ImGui.Button("Switch back"))
        {
            _api.World.Player.Entity.ActiveHandItemSlot.Itemstack?.Collectible?.GetCollectibleBehavior<AnimatableAttachable>(true)?.SetSwitchModels(_api.World.Player.Entity.EntityId, false);
        }

        ImGui.InputTextWithHint("Filter##" + "animations", "supports wildcards", ref _animationsFilter, 200);
        EditorsUtils.FilterElements(_animationsFilter, AnimationsManager._instance.Animations.Keys, out IEnumerable<string> filtered, out IEnumerable<int> indexes);

        ImGui.ListBox("transforms", ref _selectedAnimationIndexFiltered, filtered.ToArray(), filtered.Count());

        if (!filtered.Any()) return;

        if (_selectedAnimationIndexFiltered >= filtered.Count()) _selectedAnimationIndexFiltered = 0;

        _selectedAnimationIndex = AnimationsManager._instance.Animations.Keys.ToArray().IndexOf(filtered.ToArray()[_selectedAnimationIndexFiltered]);

        /*if (ImGui.Button("Remove##animations"))
        {
            Animations.Remove(Animations.Keys.ToArray()[_selectedAnimationIndex]);
            _selectedAnimationIndex--;
            if (_selectedAnimationIndex < 0) _selectedAnimationIndex = 0;
        }*/

        codes = AnimationsManager._instance.Animations.Keys.ToArray();

        if (ImGui.CollapsingHeader($"Add animation"))
        {
            CreateAnimationGui();
        }

        if (_selectedAnimationIndex < AnimationsManager._instance.Animations.Count)
        {
            ImGui.SeparatorText("Animation");

            if (ImGui.Button("Play") && AnimationsManager._instance.Animations.Count > 0)
            {
                AnimationRequest request = new(
                    AnimationsManager._instance.Animations[codes[_selectedAnimationIndex]],
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

            if (ImGui.Button("Export to clipboard") && AnimationsManager._instance.Animations.Count > 0)
            {
                ImGui.SetClipboardText(AnimationsManager._instance.Animations[codes[_selectedAnimationIndex]].ToString());
            }
            ImGui.SameLine();

            ImGui.SetNextItemWidth(200);
            ImGui.SliderFloat("Animation speed", ref _animationSpeed, 0.1f, 2);
            ImGui.Checkbox("Overwrite current frame", ref _overwriteFrame);
            AnimationsManager._instance.Animations[codes[_selectedAnimationIndex]].Edit(codes[_selectedAnimationIndex]);
            if (_overwriteFrame)
            {
                if (AnimationsManager._instance.Animations[codes[_selectedAnimationIndex]]._playerFrameEdited)
                {
                    _behavior.FrameOverride = AnimationsManager._instance.Animations[codes[_selectedAnimationIndex]].StillPlayerFrame(AnimationsManager._instance.Animations[codes[_selectedAnimationIndex]]._playerFrameIndex, AnimationsManager._instance.Animations[codes[_selectedAnimationIndex]]._frameProgress);
                }
                else
                {
                    _behavior.FrameOverride = AnimationsManager._instance.Animations[codes[_selectedAnimationIndex]].StillItemFrame(AnimationsManager._instance.Animations[codes[_selectedAnimationIndex]]._itemFrameIndex, AnimationsManager._instance.Animations[codes[_selectedAnimationIndex]]._frameProgress);
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

        System.Numerics.Vector3 translation = new(transform.Translation.X, transform.Translation.Y, transform.Translation.Z);
        System.Numerics.Vector3 origin = new(transform.Origin.X, transform.Origin.Y, transform.Origin.Z);
        System.Numerics.Vector3 rotation = new(transform.Rotation.X, transform.Rotation.Y, transform.Rotation.Z);

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

    private ModelTransform? _currentTransform;
    private GenericDisplayProto? _currentBlock;
    private bool _selected = false;
    private bool _updateMesh = false;
    private void GenericDisplayTab()
    {
        BlockSelection? selection = _api.World.Player.CurrentBlockSelection;
        CollectibleObject? collectible = _api.World.Player.Entity.RightHandItemSlot.Itemstack?.Collectible;

        if (ImGui.Button("Select##GenericDisplayTab") && !_selected && selection?.Block != null && collectible != null)
        {
            _currentBlock = selection.Block.GetBlockEntity<GenericDisplayProto>(selection);
            if (_currentBlock != null)
            {
                _currentTransform = collectible.Attributes?[_currentBlock.AttributeTransformCode].AsObject<ModelTransform>();
                if (_currentTransform != null)
                {
                    _selected = true;
                    _currentBlock.EditedTransforms[collectible.Id] = _currentTransform;
                }
                else
                {
                    _selected = false;
                }
            }
            else
            {
                _selected = false;
            }
        }
        ImGui.SameLine();
        if (ImGui.Button("Redraw##GenericDisplayTab"))
        {
            _currentBlock?.RegenerateMeshes();
            //_currentBlock?.updateMeshes(forceUpdate: true);
            //_currentBlock?.MarkDirty(true);
        }

        if (_currentTransform == null || !_selected) return;

        ModelTransform transform = _currentTransform;

        ImGui.SameLine();
        if (ImGui.Button($"Export to clipboard##GenericDisplayTab"))
        {
            ImGui.SetClipboardText(JsonUtil.ToPrettyString(transform));
        }

        ImGui.SameLine();
        ImGui.Checkbox("Update mesh", ref _updateMesh);

        float speed = ImGui.GetIO().KeysDown[(int)ImGuiKey.LeftShift] ? 0.1f : 1;

        float scale = transform.ScaleXYZ.X;
        ImGui.DragFloat("Scale##GenericDisplayTab", ref scale, speed * 0.1f);
        transform.Scale = scale;

        System.Numerics.Vector3 translation = new(transform.Translation.X, transform.Translation.Y, transform.Translation.Z);
        System.Numerics.Vector3 origin = new(transform.Origin.X, transform.Origin.Y, transform.Origin.Z);
        System.Numerics.Vector3 rotation = new(transform.Rotation.X, transform.Rotation.Y, transform.Rotation.Z);

        ImGui.DragFloat3("Translation##GenericDisplayTab", ref translation, speed * 0.1f);
        ImGui.DragFloat3("Origin##GenericDisplayTab", ref origin, speed * 0.1f);
        ImGui.DragFloat3("Rotation##GenericDisplayTab", ref rotation, speed);

        transform.Translation.X = translation.X;
        transform.Translation.Y = translation.Y;
        transform.Translation.Z = translation.Z;
        transform.Origin.X = origin.X;
        transform.Origin.Y = origin.Y;
        transform.Origin.Z = origin.Z;
        transform.Rotation.X = rotation.X;
        transform.Rotation.Y = rotation.Y;
        transform.Rotation.Z = rotation.Z;

        if (_updateMesh)
        {
            _currentBlock?.RegenerateMeshes();
        }

    }

    private void CreateAnimationGui()
    {
        ImGui.Indent();
        ImGui.SeparatorText("Just player");

        ImGui.InputText("Animation code##playeranimation", ref _playerAnimationKey, 300);

        bool canAddAnimation = !AnimationsManager._instance.Animations.ContainsKey(_playerAnimationKey) && _playerAnimationKey != "";
        if (!canAddAnimation) ImGui.BeginDisabled();
        if (ImGui.Button($"Create##playeranimation"))
        {
            AnimationsManager._instance.Animations.Add(_playerAnimationKey, new Animation(new PLayerKeyFrame[] { PLayerKeyFrame.Zero }));
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

        bool canCreate = !AnimationsManager._instance.Animations.ContainsKey(_animationKey);

        if (!canCreate) ImGui.BeginDisabled();
        if (ImGui.Button("Create##itemanimation"))
        {
            try
            {
                AnimationsManager._instance.Animations.Add(_animationKey, new Animation(new PLayerKeyFrame[] { PLayerKeyFrame.Zero }, _itemAnimation, shape));
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