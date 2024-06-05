using Vintagestory.API.Client;

namespace CombatOverhaul.RangedSystems.Aiming;

public enum AimingType
{
    Vanilla = 0,
    WithCursor,
    DownSights,
    MovingReticle
}

public class AimingSystem
{
    public AimingSystem(ICoreClientAPI api)
    {
        _api = api;
    }

    private readonly ICoreClientAPI _api;
}