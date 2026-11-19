// #Misfits Add - a sentient ghoul's shout: sends the feral pack around it at the nearest prey.
using Content.Shared.NPC.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server._Misfits.Ghoul;

[RegisterComponent]
public sealed partial class MisfitsGhoulRallyComponent : Component
{
    // The action granted while this is on the ghoul.
    [DataField]
    public EntProtoId Action = "ActionGhoulRally";

    [ViewVariables]
    public EntityUid? ActionEntity;

    // How far the shout carries to other ghouls.
    [DataField]
    public float Range = 12f;

    // How far each rallied ghoul looks for something to charge.
    [DataField]
    public float TargetRange = 20f;

    // Only mobs of this faction answer; the horde's feral ghouls are Feral.
    [DataField]
    public ProtoId<NpcFactionPrototype> Faction = "Feral";
}
