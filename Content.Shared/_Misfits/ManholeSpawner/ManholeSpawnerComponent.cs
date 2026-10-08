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
    public uint IntervalSeconds = 20;

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

    /// When to next attempt a spawn, not saved.
    public TimeSpan CheckTime;

    /// Mobs this spawner released that are still counted, not networked or saved.
    public int AliveCount;
}

[Serializable, NetSerializable]
public enum ManholeSpawnerVisuals : byte
{
    /// True if open.
    Open,
}
