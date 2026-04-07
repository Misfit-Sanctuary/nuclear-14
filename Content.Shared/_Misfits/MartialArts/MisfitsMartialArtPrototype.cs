// #Misfits Add - Prototype definition for a complete martial arts style
using Content.Shared._Misfits.Grab;
using Robust.Shared.Prototypes;

namespace Content.Shared._Misfits.MartialArts;

/// <summary>
/// Defines a complete martial arts style: its damage modifier, combo list, and grab overrides.
/// Referenced by GrantMartialArtKnowledgeComponent to configure the learner.
/// </summary>
[Prototype("misfitsMartialArt")]
public sealed partial class MisfitsMartialArtPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>Which form enum value this prototype represents.</summary>
    [DataField]
    public MisfitsMartialArtsForms MartialArtsForm = MisfitsMartialArtsForms.LegionGladiatorial;

    /// <summary>Flat damage added to unarmed strikes from this style.</summary>
    [DataField]
    public float BaseDamageModifier;

    /// <summary>Damage type for the base damage modifier.</summary>
    [DataField]
    public string DamageModifierType = "Blunt";

    /// <summary>If true, base damage modifier is randomised between Min and Max.</summary>
    [DataField]
    public bool RandomDamageModifier;

    [DataField]
    public int MinRandomDamageModifier;

    [DataField]
    public int MaxRandomDamageModifier = 5;

    /// <summary>The combo list loaded when the style is first granted.</summary>
    [DataField]
    public ProtoId<MisfitsComboListPrototype> RoundstartCombos;

    /// <summary>Override: when an entity with this style grabs, their grab starts at this stage.</summary>
    [DataField]
    public GrabStage StartingGrabStage = GrabStage.Soft;
}
