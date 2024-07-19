using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace CombatOverhaul.RangedSystems.Aiming;

public sealed class AimingAccuracyBehavior : EntityBehavior
{
    public AimingAccuracyBehavior(Entity entity) : base(entity)
    {
        if (entity is not EntityPlayer player)
        {
            throw new ArgumentException("Entity must be an EntityPlayer");
        }

        _player = player;

        CombatOverhaulSystem system = entity.Api.ModLoader.GetModSystem<CombatOverhaulSystem>();

        if (entity.Api is not ICoreClientAPI clientApi) return;

        _mainPlayer = (entity as EntityPlayer)?.PlayerUID == clientApi.Settings.String["playeruid"];

        if (!_mainPlayer) return;

        _clientAimingSystem = system.AimingSystem ?? throw new Exception();
        clientApi.Input.InWorldAction += InWorldAction;
        _modifiers.Add(new BaseAimingAccuracy(_player, _clientAimingSystem));
        _modifiers.Add(new MovingAimingAccuracy(_player, _clientAimingSystem));
        _modifiers.Add(new MountedAimingAccuracy(_player, _clientAimingSystem));
        _modifiers.Add(new OnHurtAimingAccuracy(_player, _clientAimingSystem));

        _player.Stats.Register("steadyAim", EnumStatBlendType.WeightedSum);
    }

    public override void OnGameTick(float deltaTime)
    {
        if (!_mainPlayer || !_isAiming) return;

        if (!entity.Alive)
        {
            StopAim();
            return;
        }

        if (!_stats.AllowSprint)
        {
            _player.CurrentControls &= ~EnumEntityActivity.SprintMode;
            _player.Controls.Sprint = false;
            _player.ServerControls.Sprint = false;
        }

        for (int i = 0; i < _modifiers.Count; i++)
        {
            _modifiers[i].Update(deltaTime, _stats);
        }

        _clientAimingSystem.DriftMultiplier *= 1 / Math.Clamp(entity.Stats.GetBlended("steadyAim") * entity.Stats.GetBlended("steadyAim"), 0.25f, 4f);
        _clientAimingSystem.TwitchMultiplier *= 1 / Math.Clamp(entity.Stats.GetBlended("steadyAim") * entity.Stats.GetBlended("steadyAim"), 0.25f, 4f);
    }
    public override void OnEntityReceiveDamage(DamageSource damageSource, ref float damage)
    {
        if (!_mainPlayer || damageSource.Type == EnumDamageType.Heal) return;

        for (int i = 0; i < _modifiers.Count; i++)
        {
            _modifiers[i].OnHurt(damage);
        }
    }
    public override string PropertyName() => "CombatOverhaul:aimingAccuracy";

    public void StartAim(AimingStats stats)
    {
        _stats = stats;
        _isAiming = true;

        if (stats.MoveSpeedPenalty != 0)
        {
            entity.Stats.Set("walkspeed", "CombatOverhaul:aiming", -(stats.MoveSpeedPenalty * entity.Stats.GetBlended("walkspeed")));
        }

        for (int i = 0; i < _modifiers.Count; i++)
        {
            _modifiers[i].BeginAim();
        }
    }
    public void StopAim()
    {
        _isAiming = false;

        entity.Stats.Set("walkspeed", "CombatOverhaul:aiming", 0);

        for (int i = 0; i < _modifiers.Count; i++)
        {
            _modifiers[i].EndAim();
        }
    }

    private readonly List<AccuracyModifier> _modifiers = new();
    private readonly EntityAgent _player;
    private readonly bool _mainPlayer = false;
    private ClientAimingSystem _clientAimingSystem;

    private AimingStats _stats = new();
    private bool _isAiming = false;

    private void InWorldAction(EnumEntityAction action, bool on, ref EnumHandling handled)
    {
        if (_isAiming && !_stats.AllowSprint && action == EnumEntityAction.Sprint && on)
        {
            handled = EnumHandling.PreventDefault;
        }
    }
}


internal class AccuracyModifier
{
    protected EntityAgent Entity;
    protected long AimStartMs;
    protected ClientAimingSystem ClientAimingSystem;

    public float SecondsSinceAimStart
    {
        get { return (Entity.World.ElapsedMilliseconds - AimStartMs) / 1000f; }
    }

    public AccuracyModifier(EntityAgent entity, ClientAimingSystem clientAimingSystem)
    {
        Entity = entity;
        ClientAimingSystem = clientAimingSystem;
    }

    public virtual void BeginAim()
    {
        AimStartMs = Entity.World.ElapsedMilliseconds;
    }

    public virtual void EndAim()
    {

    }

    public virtual void OnHurt(float damage) { }

    public virtual void Update(float dt, AimingStats weaponStats)
    {

    }
}

internal class BaseAimingAccuracy : AccuracyModifier
{
    public BaseAimingAccuracy(EntityAgent entity, ClientAimingSystem clientAimingSystem) : base(entity, clientAimingSystem)
    {
    }

    public override void Update(float dt, AimingStats weaponStats)
    {
        /*float modspeed = Entity.Stats.GetBlended("rangedWeaponsSpeed");

        // Linear inaccuracy from starting to aim - kept in reserve
        //float bullseyeAccuracy = GameMath.Max((weaponStats.accuracyStartTime - SecondsSinceAimStart * modspeed) / weaponStats.accuracyStartTime, 0f) * weaponStats.accuracyStart; // Linear inaccuracy from starting to aim

        // Squared inaccuracy when starting to aim
        float accMod = GameMath.Clamp((SecondsSinceAimStart * modspeed) / weaponStats.AccuracyStartTime, 0f, 1f);
        accMod = 1f - (accMod * accMod);
        float bullseyeAccuracy = accMod * weaponStats.AccuracyStart;

        // Linear loss of accuracy from holding too long
        float bullseyeAccuracy = GameMath.Clamp((SecondsSinceAimStart - weaponStats.AccuracyOvertimeStart - weaponStats.AccuracyStartTime) / weaponStats.AccuracyOvertimeTime, 0f, 1f) * weaponStats.AccuracyOvertime;*/

        if (weaponStats.AccuracyOvertime == TimeSpan.Zero)
        {
            ClientAimingSystem.DriftMultiplier = 1;
            ClientAimingSystem.TwitchMultiplier = 1;
            return;
        }

        float bullseyeAccuracy = SecondsSinceAimStart > weaponStats.AccuracyOvertime.TotalSeconds ? 1 : 0;

        if (SecondsSinceAimStart > weaponStats.AccuracyOvertime.TotalSeconds)
        {
            ClientAimingSystem.AimingState = WeaponAimingState.PartCharge;
        }

        ClientAimingSystem.DriftMultiplier = 1 + bullseyeAccuracy;
        ClientAimingSystem.TwitchMultiplier = 1 + (bullseyeAccuracy * 3f);
    }
}

internal sealed class MovingAimingAccuracy : AccuracyModifier
{
    private float _walkAccuracyPenalty;
    private float _sprintAccuracyPenalty;

    private const float _walkMaxPenaltyMod = 1f;
    private const float _sprintMaxPenaltyMod = 1.5f;

    private const float _penaltyRiseRate = 6.5f;
    private const float _penaltyDropRate = 2.5f;

    private const float _driftMod = 0.8f;
    private const float _twitchMod = 0.6f;

    public MovingAimingAccuracy(EntityAgent entity, ClientAimingSystem clientAimingSystem) : base(entity, clientAimingSystem)
    {
    }

    public override void BeginAim()
    {
        base.BeginAim();

        _walkAccuracyPenalty = 0f;
        _sprintAccuracyPenalty = 0f;
    }

    public override void Update(float dt, AimingStats weaponStats)
    {
        _walkAccuracyPenalty = GameMath.Clamp(Entity.Controls.TriesToMove ? _walkAccuracyPenalty + dt * _penaltyRiseRate : _walkAccuracyPenalty - dt * _penaltyDropRate, 0, _walkMaxPenaltyMod);
        _sprintAccuracyPenalty = GameMath.Clamp(Entity.Controls.TriesToMove && Entity.Controls.Sprint ? _sprintAccuracyPenalty + dt * _penaltyRiseRate : _sprintAccuracyPenalty - dt * _penaltyDropRate, 0, _sprintMaxPenaltyMod);

        if (ClientAimingSystem != null)
        {
            ClientAimingSystem.DriftMultiplier += ((_walkAccuracyPenalty + _sprintAccuracyPenalty) * _driftMod * weaponStats.AccuracyMovePenalty);
            ClientAimingSystem.TwitchMultiplier += ((_walkAccuracyPenalty + _sprintAccuracyPenalty) * _twitchMod * weaponStats.AccuracyMovePenalty);
        }
    }
}

internal class MountedAimingAccuracy : AccuracyModifier
{
    private float _walkAccuracyPenalty;
    private float _sprintAccuracyPenalty;

    private const float _walkMaxPenaltyMod = 0.8f;
    private const float _sprintMaxPenaltyMod = 1.5f;

    private const float _penaltyRiseRate = 6.5f;
    private const float _penaltyDropRate = 2f;

    private const float _driftMod = 0.8f;
    private const float _twitchMod = 0.6f;

    public MountedAimingAccuracy(EntityAgent entity, ClientAimingSystem clientAimingSystem) : base(entity, clientAimingSystem)
    {
    }

    public override void BeginAim()
    {
        base.BeginAim();

        _walkAccuracyPenalty = 0f;
        _sprintAccuracyPenalty = 0f;
    }

    public override void Update(float dt, AimingStats weaponStats)
    {
        bool mountTriesToMove = Entity.MountedOn?.Controls != null && Entity.MountedOn.Controls.TriesToMove;
        bool mountTriesToSprint = mountTriesToMove && (Entity.MountedOn?.Controls.Sprint ?? false);

        _walkAccuracyPenalty = GameMath.Clamp(mountTriesToMove ? _walkAccuracyPenalty + dt * _penaltyRiseRate : _walkAccuracyPenalty - dt * _penaltyDropRate, 0, _walkMaxPenaltyMod);
        _sprintAccuracyPenalty = GameMath.Clamp(mountTriesToSprint ? _sprintAccuracyPenalty + dt * _penaltyRiseRate : _sprintAccuracyPenalty - dt * _penaltyDropRate, 0, _sprintMaxPenaltyMod);

        if (ClientAimingSystem != null)
        {
            ClientAimingSystem.DriftMultiplier += (_walkAccuracyPenalty + _sprintAccuracyPenalty) * _driftMod * weaponStats.AccuracyMovePenalty;
            ClientAimingSystem.TwitchMultiplier += (_walkAccuracyPenalty + _sprintAccuracyPenalty) * _twitchMod * weaponStats.AccuracyMovePenalty;
        }
    }
}

internal class OnHurtAimingAccuracy : AccuracyModifier
{
    private float _accuracyPenalty;

    public OnHurtAimingAccuracy(EntityAgent entity, ClientAimingSystem clientAimingSystem) : base(entity, clientAimingSystem)
    {
    }

    public override void Update(float dt, AimingStats weaponStats)
    {
        _accuracyPenalty = GameMath.Clamp(_accuracyPenalty - dt / 3, 0, 0.4f);
    }

    public override void OnHurt(float damage)
    {
        if (damage > 3)
        {
            _accuracyPenalty = -0.4f;
        }
    }
}
