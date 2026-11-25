using Content.Shared.Actions;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Misfits.SuicideVest;

[RegisterComponent, NetworkedComponent]
public sealed partial class SuicideVestComponent : Component
{
    [DataField]
    public float Delay = 4f;

    [DataField]
    public List<float> DelayOptions = new() { 4f, 10f, 15f };

    [DataField]
    public float MinDelay = 4f;

    [DataField]
    public SoundSpecifier? DetonateSound = new SoundPathSpecifier("/Audio/_Misfits/IED/detonate.ogg");

    [DataField]
    public EntProtoId DetonateAction = "MisfitsActionSuicideVestDetonate";

    [DataField]
    public EntityUid? ActionEntity;

    [DataField]
    public EntityUid? Wearer;

    [DataField]
    public bool Triggered;

    [DataField]
    public float? TimeRemaining;

    [DataField]
    public bool DetonateSoundPlayed;
}

public sealed partial class SuicideVestDetonateEvent : InstantActionEvent { }
