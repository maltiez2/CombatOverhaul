using CombatOverhaul.Integration;
using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Util;

namespace CombatOverhaul.Inputs;

[AttributeUsage(AttributeTargets.Method)]
public class ActionEventHandlerAttribute : Attribute
{
    public ActionEventId Event { get; }

    public ActionEventHandlerAttribute(EnumEntityAction action, ActionState state) => Event = new(action, state);
}

public interface IHasWeaponLogic
{
    IClientWeaponLogic? ClientLogic { get; }
}

public interface ISetsRenderingOffset
{
    bool RenderingOffset { get; }
}

public interface IClientWeaponLogic
{
    int ItemId { get; }
    DirectionsConfiguration DirectionsType { get; }

    void OnSelected(ItemSlot slot, EntityPlayer player, bool mainHand, ref int state);
    void OnDeselected(EntityPlayer player, bool mainHand, ref int state);
    void OnRegistered(ActionsManagerPlayerBehavior behavior, ICoreClientAPI api);
}

public interface IOnGameTick
{
    void OnGameTick(ItemSlot slot, EntityPlayer player, ref int state, bool mainHand);
}

public sealed class ActionsManagerPlayerBehavior : EntityBehavior
{
    public bool SuppressLMB { get; set; } = false;
    public bool SuppressRMB { get; set; } = false;
    public float ManipulationSpeed => Math.Clamp(entity.Stats.GetBlended("manipulationSpeed"), 0.5f, 2.0f);
    public ActionListener ActionListener { get; }

    public delegate bool ActionEventCallbackDelegate(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction);

    public ActionsManagerPlayerBehavior(Entity entity) : base(entity)
    {
        if (entity is not EntityPlayer player)
        {
            throw new ArgumentException($"This behavior should only be applied to player");
        }

        if (entity.Api is not ICoreClientAPI clientApi)
        {
            throw new ArgumentException($"This behavior is client side only");
        }

        _mainPlayer = (entity as EntityPlayer)?.PlayerUID == clientApi.Settings.String["playeruid"];
        _player = player;
        _api = clientApi;

        CombatOverhaulSystem system = _api.ModLoader.GetModSystem<CombatOverhaulSystem>();

        ActionListener = system.ActionListener ?? throw new Exception();
        _directionController = system.DirectionController ?? throw new Exception();
        _directionRenderer = system.DirectionCursorRenderer ?? throw new Exception();
        _statsSystem = system.ClientStatsSystem ?? throw new Exception();

        if (_mainPlayer)
        {
            RegisterWeapons();
            clientApi.Event.RegisterGameTickListener(OnGameFrame, 0);
        }
    }
    public override string PropertyName() => _statCategory;
    public void OnGameFrame(float deltaTime)
    {
        if (!_mainPlayer) return;

        if (!entity.Alive)
        {
            foreach (string stat in _currentMainHandPlayerStats)
            {
                _player.Stats.Remove(_statCategory, stat);
            }

            foreach (string stat in _currentOffHandPlayerStats)
            {
                _player.Stats.Remove(_statCategory, stat);
            }
        }

        bool configurationChanged = SetRenderDirectionCursorForMainHand();
        _directionController.OnGameTick(forceNewDirection: configurationChanged);
        _ = CheckIfItemsInHandsChanged();

        if (_player.RightHandItemSlot.Itemstack?.Item is IOnGameTick mainhandTickListener)
        {
            mainhandTickListener.OnGameTick(_player.RightHandItemSlot, _player, ref _mainHandState, true);
        }

        if (_player.LeftHandItemSlot.Itemstack?.Item is IOnGameTick offhandTickListener)
        {
            offhandTickListener.OnGameTick(_player.LeftHandItemSlot, _player, ref _offHandState, false);
        }

        ActionListener.SuppressLMB = SuppressLMB;
        ActionListener.SuppressRMB = SuppressRMB;
    }

    public int GetState(bool mainHand = true) => mainHand ? _mainHandState : _offHandState;
    public void SetState(int state, bool mainHand = true)
    {
        if (mainHand)
        {
            _mainHandState = state;
        }
        else
        {
            _offHandState = state;
        }
    }
    public void SetStat(string stat, string category, float value = 0)
    {
        if (!_mainPlayer) return;

        _statsSystem.SetStat(stat, category, value);
    }

    private readonly bool _mainPlayer = false;
    private readonly ICoreClientAPI _api;
    private readonly EntityPlayer _player;
    private readonly HashSet<string> _currentMainHandPlayerStats = new();
    private readonly HashSet<string> _currentOffHandPlayerStats = new();
    private const string _statCategory = "CombatOverhaul:melee-weapon-player-behavior";
    private readonly DirectionController _directionController;
    private readonly DirectionCursorRenderer _directionRenderer;
    private readonly StatsSystemClient _statsSystem;

    private IClientWeaponLogic? _currentMainHandWeapon;
    private IClientWeaponLogic? _currentOffHandWeapon;
    private int _currentMainHandItemId = -1;
    private int _currentOffHandItemId = -1;
    private int _currentMainHandSlotId = -1;
    private int _mainHandState = 0;
    private int _offHandState = 0;
    private bool _mainHandRenderingOffset = true;
    private bool _offHandRenderingOffset = true;

    private void RegisterWeapons()
    {
        _api.World.Items
            .OfType<IHasWeaponLogic>()
            .Select(element => element.ClientLogic)
            .Foreach(RegisterWeapon);
    }
    private void RegisterWeapon(IClientWeaponLogic? weapon)
    {
        if (weapon is null) return;

        Dictionary<ActionEventId, List<ActionEventCallbackDelegate>> handlers = CollectHandlers(weapon);

        int itemId = weapon.ItemId;

        foreach ((ActionEventId eventId, List<ActionEventCallbackDelegate> callbacks) in handlers)
        {
            callbacks.ForEach(callback =>
            {
                ActionListener.Subscribe(eventId, (eventData) => HandleActionEvent(eventData, itemId, callback));
            });
        }

        weapon.OnRegistered(this, _api);
    }
    private static Dictionary<ActionEventId, List<ActionEventCallbackDelegate>> CollectHandlers(object owner)
    {
        IEnumerable<MethodInfo> methods = owner.GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Where(method => method.GetCustomAttributes(typeof(ActionEventHandlerAttribute), true).Any());

        Dictionary<ActionEventId, List<ActionEventCallbackDelegate>> handlers = new();
        foreach (MethodInfo method in methods)
        {
            if (method.GetCustomAttributes(typeof(ActionEventHandlerAttribute), true)[0] is not ActionEventHandlerAttribute attribute) continue;

            if (Delegate.CreateDelegate(typeof(ActionEventCallbackDelegate), owner, method) is not ActionEventCallbackDelegate handler)
            {
                throw new InvalidOperationException($"Handler should have same signature as 'ActionEventCallbackDelegate' delegate.");
            }

            List<ActionEventCallbackDelegate>? callbackDelegates;
            if (handlers.TryGetValue(attribute.Event, out callbackDelegates))
            {
                callbackDelegates.Add(handler);
            }
            else
            {
                callbackDelegates = new()
                {
                    handler
                };

                handlers.Add(attribute.Event, callbackDelegates);
            }

        }

        return handlers;
    }
    private bool HandleActionEvent(ActionEventData eventData, int itemId, ActionEventCallbackDelegate callback)
    {
        int mainHandId = _player.RightHandItemSlot.Itemstack?.Item?.Id ?? -1;
        int offHandId = _player.LeftHandItemSlot.Itemstack?.Item?.Id ?? -1;

        bool mainHandHandled = false;
        bool offHandHandled = false;

        if (mainHandId == itemId)
        {
            mainHandHandled = callback.Invoke(_player.RightHandItemSlot, _player, ref _mainHandState, eventData, true, _directionController.CurrentDirection);
        }

        if (offHandId == itemId)
        {
            offHandHandled = callback.Invoke(_player.LeftHandItemSlot, _player, ref _offHandState, eventData, false, _directionController.CurrentDirection);
        }

        return mainHandHandled || offHandHandled;
    }
    private bool CheckIfItemsInHandsChanged()
    {
        int mainHandId = _player.ActiveHandItemSlot.Itemstack?.Item?.Id ?? -1;
        int mainHandSlotId = _player.ActiveHandItemSlot.Inventory.GetSlotId(_player.ActiveHandItemSlot);
        int offHandId = _player.LeftHandItemSlot.Itemstack?.Item?.Id ?? -1;
        bool anyChanged = mainHandId != _currentMainHandItemId || offHandId != _currentOffHandItemId || mainHandSlotId != _currentMainHandSlotId;

        if (anyChanged)
        {
            SuppressLMB = false;
            SuppressRMB = false;
        }

        if (anyChanged && (_currentMainHandItemId != mainHandId || mainHandSlotId != _currentMainHandSlotId))
        {
            _mainHandState = 0;
            ProcessMainHandItemChanged();
            _currentMainHandItemId = mainHandId;
            _currentMainHandSlotId = mainHandSlotId;
        }

        if (anyChanged && _currentOffHandItemId != offHandId)
        {
            _offHandState = 0;
            ProcessOffHandItemChanged();
            _currentOffHandItemId = offHandId;
        }

        return !anyChanged;
    }
    private void ProcessMainHandItemChanged()
    {
        _currentMainHandWeapon?.OnDeselected(_player, true, ref _mainHandState);
        _currentMainHandWeapon = null;

        foreach (string stat in _currentMainHandPlayerStats)
        {
            _player.Stats.Remove(_statCategory, stat);
        }

        ItemStack? stack = _player.ActiveHandItemSlot.Itemstack;

        if (stack != null && stack.Item is ISetsRenderingOffset offset)
        {
            _mainHandRenderingOffset = offset.RenderingOffset;
        }
        else
        {
            _mainHandRenderingOffset = true;
        }

        if (_mainHandRenderingOffset && _offHandRenderingOffset)
        {
            PlayerRenderingPatches.ResetOffset();
        }
        else
        {
            PlayerRenderingPatches.SetOffset(0);
        }

        if (stack == null || stack.Item is not IHasWeaponLogic weapon) return;

        weapon.ClientLogic?.OnSelected(_player.ActiveHandItemSlot, _player, true, ref _mainHandState);
        _currentMainHandWeapon = weapon.ClientLogic;

        if (stack.Item.Attributes?["fpHandsOffset"].Exists == true)
        {
            PlayerRenderingPatches.SetOffset(stack.Item.Attributes["fpHandsOffset"].AsFloat());
        }
    }
    private void ProcessOffHandItemChanged()
    {
        _currentOffHandWeapon?.OnDeselected(_player, false, ref _offHandState);
        _currentOffHandWeapon = null;

        foreach (string stat in _currentOffHandPlayerStats)
        {
            _player.Stats.Remove(_statCategory, stat);
        }

        ItemStack? stack = _player.LeftHandItemSlot.Itemstack;

        if (stack != null && stack.Item is ISetsRenderingOffset offset)
        {
            _offHandRenderingOffset = offset.RenderingOffset;
        }
        else
        {
            _offHandRenderingOffset = true;
        }

        if (_mainHandRenderingOffset && _offHandRenderingOffset)
        {
            PlayerRenderingPatches.ResetOffset();
        }
        else
        {
            PlayerRenderingPatches.SetOffset(0);
        }

        if (stack == null || stack.Item is not IHasWeaponLogic weapon) return;

        weapon.ClientLogic?.OnSelected(_player.LeftHandItemSlot, _player, false, ref _offHandState);
        _currentOffHandWeapon = weapon.ClientLogic;

        if (stack.Item.Attributes?["fpHandsOffset"].Exists == true)
        {
            PlayerRenderingPatches.SetOffset(stack.Item.Attributes["fpHandsOffset"].AsFloat());
        }
    }
    private bool SetRenderDirectionCursorForMainHand()
    {
        ItemStack? stack = _player.ActiveHandItemSlot.Itemstack;

        DirectionsConfiguration configuration = _directionController.DirectionsConfiguration;

        if (stack == null || stack.Item is not IHasWeaponLogic weapon)
        {
            _directionController.DirectionsConfiguration = DirectionsConfiguration.None;
            _directionRenderer.Show = false;
            return configuration != _directionController.DirectionsConfiguration;
        }

        if (weapon.ClientLogic != null)
        {
            _directionController.DirectionsConfiguration = weapon.ClientLogic.DirectionsType;
            _directionRenderer.Show = weapon.ClientLogic.DirectionsType != DirectionsConfiguration.None;
        }

        return configuration != _directionController.DirectionsConfiguration;
    }
}