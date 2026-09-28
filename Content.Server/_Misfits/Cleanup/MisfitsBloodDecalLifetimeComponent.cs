namespace Content.Server._Misfits.Cleanup;

/// <summary>
/// Opts a random decal spawner into the transient blood-decal lifecycle.
/// Values are seconds so they can be tuned directly in the spawner prototype.
/// </summary>
[RegisterComponent]
public sealed partial class MisfitsBloodDecalLifetimeComponent : Component
{
    /// <summary>Total time a generated blood decal may remain on the map.</summary>
    [DataField]
    public float Lifetime = 600f;

    /// <summary>How long before expiry the decal starts fading out.</summary>
    [DataField]
    public float FadeDuration = 120f;

    /// <summary>How quickly exposed decals finish fading once rain is running.</summary>
    [DataField]
    public float RainFadeDuration = 120f;
}
