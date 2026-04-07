namespace Content.Shared._Misfits.NPC;

/// <summary>
/// Marks an NPC as using proximity-based sleep/wake.
/// The NPC starts asleep on map initialisation and wakes only when a player-controlled
/// entity enters <see cref="WakeRange"/> tiles. It re-sleeps when all players leave
/// <see cref="SleepRange"/> tiles.
///
/// Designed for large open maps (e.g. Wendover at 8000×4190) where running full HTN
/// AI on every creature continuously is too expensive.
/// </summary>
[RegisterComponent]
public sealed partial class ProximityNPCComponent : Component
{
    /// <summary>
    /// Distance (tiles) within which a player wakes this NPC.
    /// Reduced from 40f to 30f to shrink active-NPC radius and lower
    /// per-tick CPU cost when many NPCs are spread across the map.
    /// </summary>
    [DataField]
    public float WakeRange = 30f; // #Misfits Tweak - reduced from 40f for performance

    /// <summary>
    /// Distance (tiles) at which the NPC sleeps if no players remain nearby.
    /// Must be greater than <see cref="WakeRange"/> to create hysteresis and prevent
    /// rapid wake/sleep thrashing at the boundary edge.
    /// Reduced from 60f to 45f to match the tighter wake range.
    /// </summary>
    [DataField]
    public float SleepRange = 45f; // #Misfits Tweak - reduced from 60f for performance

    /// <summary>
    /// If true, overrides the default HTN behaviour of waking on map init and instead
    /// keeps this NPC asleep until a player enters its wake range.
    /// </summary>
    [DataField]
    public bool StartAsleep = true;
}
