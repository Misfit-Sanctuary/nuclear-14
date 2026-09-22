using Content.Shared.DoAfter;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Misfits.ManholeSpawner;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ManholeSpawnerComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool Open = true;

    [DataField]
    public List<EntProtoId> Prototypes = [];

    [DataField]
    public float IntervalSeconds = 20f;

    [DataField]
    public float Chance = 1f;

    [DataField]
    public int MinimumEntitiesSpawned = 1;

    [DataField]
    public int MaximumEntitiesSpawned = 2;

    /// Only spawns while a living player is within this range. 0 = always.
    [DataField]
    public float ActivationRange = 30f;

    /// Local cap: how many of <see cref="Prototypes"/> may live within <see cref="NearbyRange"/>.
    [DataField]
    public int MaxAliveNearby = 6;

    [DataField]
    public float NearbyRange = 25f;

    [DataField]
    public string OpenState = "manhole_open";

    [DataField]
    public string ClosedState = "manhole_closed";

    [DataField]
    public float PryTime = 2f;

    [DataField]
    public float TimeElapsed;
}

[Serializable, NetSerializable]
public enum ManholeSpawnerVisuals : byte
{
    State,
}

[Serializable, NetSerializable]
public enum ManholeSpawnerState : byte
{
    Closed,
    Open,
}

[Serializable, NetSerializable]
public sealed partial class ManholePryDoAfterEvent : SimpleDoAfterEvent;
