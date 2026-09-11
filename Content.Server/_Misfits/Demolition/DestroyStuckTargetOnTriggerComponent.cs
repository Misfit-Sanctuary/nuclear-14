namespace Content.Server._Misfits.Demolition;

/// <summary>
/// Makes a triggered sticky explosive destroy the specific destructible structure
/// it was planted on. Structures without a destruction threshold remain immune.
/// </summary>
[RegisterComponent]
public sealed partial class DestroyStuckTargetOnTriggerComponent : Component
{
    /// <summary>
    /// Thresholds at or above this value are treated as mapper-authored pseudo-indestructibility.
    /// The Nuclear 14 exterior blast door, for example, deliberately uses 10,000 damage.
    /// </summary>
    [DataField]
    public float MaximumDestructibleThreshold = 5000f;
}
