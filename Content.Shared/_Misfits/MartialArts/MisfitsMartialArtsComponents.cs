// #Misfits Add - Components for entities that know a martial art and those that can perform combos
using Content.Shared._Misfits.Grab;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Misfits.MartialArts;

/// <summary>
/// Abstract base: components that extend this can override the starting grab stage
/// when the entity with this component is grabbed. Used by martial arts styles to skip soft grab.
/// </summary>
public abstract partial class GrabStagesOverrideComponent : Component
{
    /// <summary>When this entity initiates a grab, the first stage starts here instead of Soft.</summary>
    [DataField]
    public GrabStage StartingStage = GrabStage.Soft;
}

/// <summary>
/// Added to an entity that has learned a martial arts style.
/// Provides access to the style's combo list and modifies their unarmed capabilities.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class MartialArtsKnowledgeComponent : GrabStagesOverrideComponent
{
    /// <summary>Which martial arts form this entity has trained in.</summary>
    [DataField, AutoNetworkedField]
    public MisfitsMartialArtsForms MartialArtsForm = MisfitsMartialArtsForms.LegionGladiatorial;

    /// <summary>
    /// If true, the martial arts abilities are suppressed (e.g. disarmed of learned techniques).
    /// Set by specific event handlers.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool Blocked;

    /// <summary>The original unarmed damage before this style modified it.</summary>
    [DataField, AutoNetworkedField]
    public float OriginalFistDamage;

    /// <summary>The original unarmed damage type before this style modified it.</summary>
    [DataField, AutoNetworkedField]
    public string OriginalFistDamageType = "Blunt";
}

/// <summary>
/// Added to an entity that can perform martial arts combos.
/// Tracks the rolling attack history for combo detection.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class CanPerformComboComponent : Component
{
    /// <summary>The entity this combatant is currently targeting (resets buffer if target changes).</summary>
    [DataField, AutoNetworkedField]
    public EntityUid? CurrentTarget;

    /// <summary>ID of the combo currently being performed (if any).</summary>
    [DataField, AutoNetworkedField]
    public string BeingPerformed = string.Empty;

    /// <summary>Ring buffer size: maximum number of recent attacks tracked.</summary>
    [DataField]
    public int LastAttacksLimit = 4;

    /// <summary>Rolling history of recent attack types in order.</summary>
    [DataField, AutoNetworkedField]
    public List<MisfitsComboAttackType> LastAttacks = new();

    /// <summary>All combo prototypes this entity is allowed to perform.</summary>
    [DataField]
    public List<ProtoId<MisfitsComboPrototype>> AllowedCombos = new();

    /// <summary>Time after which the combo buffer resets if no new attacks are made.</summary>
    [DataField]
    public TimeSpan ResetTime = TimeSpan.Zero;

    /// <summary>How long (seconds) the combo window stays open after each attack.</summary>
    [DataField]
    public float ComboWindowSeconds = 5f;

    /// <summary>Used by TribalWarrior style: consecutive gnash (hit) counter for scaling damage.</summary>
    [DataField, AutoNetworkedField]
    public int ConsecutiveGnashes;
}
