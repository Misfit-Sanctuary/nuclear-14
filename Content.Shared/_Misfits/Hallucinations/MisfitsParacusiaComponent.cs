// #Misfits Add - Paracusia perk component. Permanently produces low-grade auditory
// hallucinations. Intensity scales with time-alive, mirroring how prolonged Stealth Boy
// usage degrades the user. Synced into HallucinationsComponent by ParacusiaSystem.
using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._Misfits.Hallucinations;

[RegisterComponent, NetworkedComponent]
public sealed partial class MisfitsParacusiaComponent : Component
{
    /// <summary>
    /// When the perk became active (first time the player joined / was given the perk).
    /// </summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    public TimeSpan StartTime;

    /// <summary>
    /// How many real-time seconds at each intensity tier.
    /// Index 0 unused; 1 = always-on baseline, 2 = 10 min, 3 = 30 min, 4 = 60 min.
    /// </summary>
    [DataField]
    public float[] TierThresholds = { 0f, 0f, 600f, 1800f, 3600f };

    /// <summary>
    /// Cached current contribution. Read by HallucinationsSystem to derive effective intensity.
    /// </summary>
    [DataField]
    public int CurrentLevel;
}
