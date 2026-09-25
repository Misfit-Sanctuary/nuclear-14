using Robust.Shared.Audio;

namespace Content.Server._Misfits.LungeMine;

[RegisterComponent]
public sealed partial class LungeMineComponent : Component
{
    [DataField]
    public SoundSpecifier? TriggerSound = new SoundPathSpecifier("/Audio/Items/wirecutter.ogg");

    [DataField]
    public TimeSpan DazeDuration = TimeSpan.FromSeconds(4);

    [DataField]
    public float ArmorDamage;
}
