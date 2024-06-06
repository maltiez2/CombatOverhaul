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

[AttributeUsage(AttributeTargets.Class)]
public class HasActionEventHandlersAttribute : Attribute
{

}

public interface IHasWeaponLogic
{
    IClientWeaponLogic? ClientLogic { get; }
}

public interface IClientWeaponLogic
{
    int ItemId { get; }
    DirectionsConfiguration DirectionsType { get; }

    void OnSelected(ItemSlot slot, EntityPlayer player, bool mainHand, ref int state);
    void OnDeselected(EntityPlayer player);
    void OnRegistered(ActionsManagerPlayerBehavior behavior, ICoreClientAPI api);
}

public sealed class ActionsManagerPlayerBehavior : EntityBehavior
{
    public bool SuppressLMB { get; set; } = false;
    public bool SuppressRMB { get; set; } = false;

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

        _actionListener = system.ActionListener ?? throw new Exception();
        _directionController = system.DirectionController ?? throw new Exception();

        if (_mainPlayer)
        {
            RegisterWeapons();
        }
    }
    public override string PropertyName() => _statCategory;
    public override void OnGameTick(float deltaTime)
    {
        if (!_mainPlayer) return;

        SetRenderDirectionCursorForMainHand();
        _directionController.OnGameTick();
        _ = CheckIfItemsInHandsChanged();
        _actionListener.SuppressLMB = SuppressLMB;
        _actionListener.SuppressRMB = SuppressRMB;
    }

    private readonly bool _mainPlayer = false;
    private readonly ICoreClientAPI _api;
    private readonly EntityPlayer _player;
    private readonly HashSet<string> _currentMainHandPlayerStats = new();
    private readonly HashSet<string> _currentOffHandPlayerStats = new();
    private const string _statCategory = "melee-weapon-player-behavior";
    private readonly ActionListener _actionListener;
    private readonly DirectionController _directionController;

    private IClientWeaponLogic? _currentMainHandWeapon;
    private IClientWeaponLogic? _currentOffHandWeapon;
    private int _currentMainHandItemId = -1;
    private int _currentOffHandItemId = -1;
    private int _mainHandState = 0;
    private int _offHandState = 0;

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
                _actionListener.Subscribe(eventId, (eventData) => HandleActionEvent(eventData, itemId, callback));
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
    private static IEnumerable<Type> GetClassesWithAttribute<TAttribute>() where TAttribute : Attribute
    {
        Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
        List<Type> typesWithAttribute = new();

        foreach (Assembly assembly in assemblies)
        {
            try
            {
                Type[] types = assembly.GetTypes();
                foreach (Type type in types)
                {
                    if (type.GetCustomAttributes(typeof(TAttribute), true).Length > 0)
                    {
                        typesWithAttribute.Add(type);
                    }
                }
            }
            catch (ReflectionTypeLoadException ex)
            {
                Exception?[] loaderExceptions = ex.LoaderExceptions;
            }
        }

        return typesWithAttribute;
    }
    private bool HandleActionEvent(ActionEventData eventData, int itemId, ActionEventCallbackDelegate callback)
    {
        int mainHandId = _player.ActiveHandItemSlot.Itemstack?.Id ?? -1;
        int offHandId = _player.LeftHandItemSlot.Itemstack?.Id ?? -1;

        if (mainHandId == itemId)
        {
            return callback.Invoke(_player.ActiveHandItemSlot, _player, ref _mainHandState, eventData, true, _directionController.CurrentDirection);
        }

        if (offHandId == itemId)
        {
            return callback.Invoke(_player.LeftHandItemSlot, _player, ref _offHandState, eventData, false, _directionController.CurrentDirection);
        }

        return false;
    }
    private bool CheckIfItemsInHandsChanged()
    {
        int mainHandId = _player.ActiveHandItemSlot.Itemstack?.Id ?? -1;
        int offHandId = _player.LeftHandItemSlot.Itemstack?.Id ?? -1;
        bool anyChanged = mainHandId != _currentMainHandItemId || offHandId != _currentOffHandItemId;

        if (anyChanged)
        {
            SuppressLMB = false;
            SuppressRMB = false;
        }

        if (anyChanged && _currentMainHandItemId != mainHandId)
        {
            _mainHandState = 0;
            ProcessMainHandItemChanged();
            _currentMainHandItemId = mainHandId;
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
        _currentMainHandWeapon?.OnDeselected(_player);
        _currentMainHandWeapon = null;

        foreach (string stat in _currentMainHandPlayerStats)
        {
            _player.Stats.Remove(_statCategory, stat);
        }

        ItemStack? stack = _player.ActiveHandItemSlot.Itemstack;

        if (stack == null || stack.Item is not IHasWeaponLogic weapon)
        {
            PlayerRenderingPatches.ResetOffset();
            return;
        }

        weapon.ClientLogic?.OnSelected(_player.ActiveHandItemSlot, _player, true, ref _mainHandState);
        _currentMainHandWeapon = weapon.ClientLogic;

        if (stack.Item.Attributes?["fpHandsOffset"].Exists == true)
        {
            PlayerRenderingPatches.SetOffset(stack.Item.Attributes["fpHandsOffset"].AsFloat());
        }
    }
    private void ProcessOffHandItemChanged()
    {
        _currentOffHandWeapon?.OnDeselected(_player);
        _currentOffHandWeapon = null;

        foreach (string stat in _currentOffHandPlayerStats)
        {
            _player.Stats.Remove(_statCategory, stat);
        }

        ItemStack? stack = _player.ActiveHandItemSlot.Itemstack;

        if (stack == null || stack.Item is not IClientWeaponLogic weapon) return;

        weapon.OnSelected(_player.LeftHandItemSlot, _player, false, ref _offHandState);
        _currentOffHandWeapon = weapon;
    }
    private void SetRenderDirectionCursorForMainHand()
    {
        ItemStack? stack = _player.ActiveHandItemSlot.Itemstack;

        if (stack == null || stack.Item is not IClientWeaponLogic weapon)
        {
            _directionController.DirectionsConfiguration = DirectionsConfiguration.None;
            return;
        }

        _directionController.DirectionsConfiguration = weapon.DirectionsType;
    }
}