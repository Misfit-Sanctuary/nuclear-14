using Content.Server._Misfits.ManholeSpawner;

namespace Content.Server._Misfits.ManholeSpawner.Components;

/// Marks a mob as released by a manhole, so the spawner can count it without a prototype lookup.
[RegisterComponent, Access(typeof(ManholeSpawnerSystem))]
public sealed partial class SpawnedByManholeComponent : Component
{
    [ViewVariables]
    public EntityUid Spawner;

    /// Cleared once this mob stops counting, so death and deletion cannot both decrement.
    [ViewVariables]
    public bool Counted = true;
}
