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

    /// 0 means always.
    [DataField]
    public float ActivationRange = 30f;

    /// Max living mobs inside NearbyRange.
    [DataField]
    public int MaxAliveNearby = 6;

    [DataField]
    public float NearbyRange = 25f;

    // Client needs these for the sprite.
    [DataField, AutoNetworkedField]
    public string OpenState = "manhole_open";

    [DataField, AutoNetworkedField]
    public string ClosedState = "manhole_closed";

    [DataField]
    public float PryTime = 2f;

    /// Spawn timer, not saved.
    public float TimeElapsed;
}

[Serializable, NetSerializable]
public enum ManholeSpawnerVisuals : byte
{
    /// True if open.
    Open,
}
