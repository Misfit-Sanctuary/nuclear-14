namespace Content.Server._Misfits.SpecialStats.Components;

/// <summary>
/// Present while a charge lunge is in flight. Grants damage resistance and
/// staggers mobs the user collides with.
/// </summary>
[RegisterComponent]
public sealed partial class SpecialChargingComponent : Component
{
    /// <summary>
    /// Safety expiry in case the throw never reports stopping.
    /// </summary>
    public TimeSpan EndTime;

    public TimeSpan StaggerTime;
    public float DamageMultiplier;
}
