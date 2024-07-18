using CombatOverhaul.Integration;
using ProtoBuf;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;

namespace CombatOverhaul.Framework;

/*public sealed class PlayerStatsBehavior : EntityBehavior
{
    public StatsPlayerBehavior(Entity entity) : base(entity)
    {
        if (entity is not EntityPlayer player)
        {
            throw new ArgumentException($"This behavior should only be applied to player");
        }

        if (entity.Api is not ICoreServerAPI serverApi)
        {
            throw new ArgumentException($"This behavior is server side");
        }

        _player = player;
        _api = serverApi;

        CombatOverhaulSystem system = _api.ModLoader.GetModSystem<CombatOverhaulSystem>();
    }
    public override string PropertyName() => _statCategory;
    public override void OnGameTick(float deltaTime)
    {
        if (!_mainPlayer) return;

        _ = CheckIfItemsInHandsChanged();
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

    private readonly ICoreServerAPI _api;
    private readonly EntityPlayer _player;
    private readonly HashSet<string> _currentMainHandPlayerStats = new();
    private readonly HashSet<string> _currentOffHandPlayerStats = new();
    private const string _statCategory = "stats-player-behavior";

    private int _currentMainHandItemId = -1;
    private int _currentOffHandItemId = -1;
    private int _mainHandState = 0;
    private int _offHandState = 0;
    private bool _mainHandRenderingOffset = true;
    private bool _offHandRenderingOffset = true;


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
        _currentOffHandWeapon?.OnDeselected(_player);
        _currentOffHandWeapon = null;

        foreach (string stat in _currentOffHandPlayerStats)
        {
            _player.Stats.Remove(_statCategory, stat);
        }

        ItemStack? stack = _player.ActiveHandItemSlot.Itemstack;

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

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public sealed class StatsPacket
{
    public string Code { get; set; } = "";
    public string Category { get; set; } = "";
    public float Value { get; set; }
}

public sealed class PlayerStatsServer
{

}

public sealed class PlayerStatsClient
{

}*/