using Robust.Shared.Audio;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._Misfits.Harpoon;

[RegisterComponent, Access(typeof(HarpoonSystem))]
public sealed partial class HarpoonComponent : Component
{
    [DataField]
    public EntityUid? Thrower;

    [DataField]
    public EntityUid? Hooked;

    [DataField]
    public EntProtoId ReelAction = "MisfitsActionHarpoonReel";

    [DataField]
    public EntityUid? ReelActionEntity;

    [DataField]
    public float ReelSpeed = 3f;

    [DataField]
    public float StruggleModifier = 0.4f;

    [DataField]
    public float MinDistance = 1f;

    [DataField]
    public float MaxRopeLength = 7f;

    [DataField]
    public float SnapLength = 10f;

    [DataField]
    public SpriteSpecifier RopeSprite =
        new SpriteSpecifier.Rsi(new ResPath("Objects/Weapons/Guns/Launchers/grappling_gun.rsi"), "rope");

    [DataField]
    public SoundSpecifier? ReelSound = new SoundPathSpecifier("/Audio/Weapons/reel.ogg")
    {
        Params = AudioParams.Default.WithLoop(true)
    };

    [DataField]
    public SoundSpecifier? SnapSound = new SoundPathSpecifier("/Audio/Effects/snap.ogg");
}
