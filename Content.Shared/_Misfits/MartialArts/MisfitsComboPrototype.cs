// #Misfits Add - Prototype definition for a single martial arts combo (attack sequence → outcome)
using Content.Shared._Misfits.Grab;
using Robust.Shared.Prototypes;

namespace Content.Shared._Misfits.MartialArts;

/// <summary>
/// Defines a single martial arts combo: an ordered sequence of attack inputs that,
/// when matched against the tail of a combatant's LastAttacks buffer, triggers a specific effect.
/// </summary>
[Prototype("misfitsCombo")]
public sealed partial class MisfitsComboPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>Which martial arts form this combo belongs to.</summary>
    [DataField(required: true)]
    public MisfitsMartialArtsForms MartialArtsForm;

    /// <summary>
    /// The ordered input sequence. The tail of CanPerformComboComponent.LastAttacks must match this exactly.
    /// Example: [Harm, Disarm, Grab] — a slam combo.
    /// </summary>
    [DataField("attacks", required: true)]
    public List<MisfitsComboAttackType> AttackTypes = new();

    // ---- Outcome parameters ----

    /// <summary>Additional flat damage added on top of normal weapon damage.</summary>
    [DataField]
    public float ExtraDamage;

    /// <summary>Damage type for the extra damage (default: Blunt).</summary>
    [DataField]
    public string DamageType = "Blunt";

    /// <summary>Seconds the target is stunned/paralyzed after this combo fires.</summary>
    [DataField]
    public float ParalyzeTime;

    /// <summary>Stamina damage dealt by this combo.</summary>
    [DataField]
    public float StaminaDamage;

    /// <summary>Whether items are knocked out of the target's hands when the combo connects.</summary>
    [DataField]
    public bool DropItems;

    /// <summary>Speed at which the target is thrown if this combo includes a throw.</summary>
    [DataField]
    public float ThrownSpeed = 7f;

    /// <summary>Whether this combo can be executed while the performer is prone.</summary>
    [DataField]
    public bool CanDoWhileProne = true;

    /// <summary>Whether the combo can be performed with the performer as their own target.</summary>
    [DataField]
    public bool PerformOnSelf;

    /// <summary>Display name shown in feedback popups.</summary>
    [DataField]
    public string Name = string.Empty;

    /// <summary>Optional: minimum grab stage required on target for this combo to fire.</summary>
    [DataField]
    public GrabStage? RequiredGrabStage;
}

/// <summary>
/// A named list of combo prototype IDs, assigned to a martial art as its moveset.
/// </summary>
[Prototype("misfitsComboList")]
public sealed partial class MisfitsComboListPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public List<ProtoId<MisfitsComboPrototype>> Combos = new();
}
