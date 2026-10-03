using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Timing;

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

    /// Max living mobs this spawner has released, tracked by marker rather than a range scan.
    [DataField]
    public int MaxAliveNearby = 6;

    // Client needs these for the sprite.
    [DataField]
    public string OpenState = "manhole_open";

    [DataField]
    public string ClosedState = "manhole_closed";

    [DataField]
    public float PryTime = 2f;

    /// Next tick to attempt a spawn on, not saved.
    public GameTick CheckTime;

    /// Mobs this spawner released that are still counted, not networked or saved.
    public int AliveCount;
}

[Serializable, NetSerializable]
public enum ManholeSpawnerVisuals : byte
{
    /// True if open.
    Open,
}
