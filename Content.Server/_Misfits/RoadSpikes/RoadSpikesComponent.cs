using Content.Shared.Damage;
using Robust.Shared.Audio;
using Robust.Shared.GameObjects;

namespace Content.Server._Misfits.RoadSpikes;

/// <summary>
/// Makes a step-trigger hazard damage motorbikes and throw their riders.
/// The normal step-trigger and shard components continue to handle pedestrians.
/// </summary>
[RegisterComponent]
public sealed partial class RoadSpikesComponent : Component
{
    [DataField]
    public TimeSpan RiderKnockdownTime = TimeSpan.FromSeconds(3);

    [DataField]
    public DamageSpecifier BikeDamage = new();

    [DataField]
    public SoundSpecifier ImpactSound = new SoundCollectionSpecifier("MetalBreak");

    [DataField]
    public string RiderWarning = "road-spikes-rider-warning";

    [ViewVariables]
    // Prevents two collision events from applying the effect before the spikes are deleted.
    public bool Triggered;
}
