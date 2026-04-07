// #Misfits Add - Component on the pulled entity tracking their grabbed state and escape parameters
using Content.Shared._Misfits.Grab;
using Robust.Shared.GameStates;

namespace Content.Shared._Misfits.Grab;

/// <summary>
/// Added to an entity that is being grabbed (i.e. being pulled and has a grab stage applied).
/// Tracks escape attempt state and table-slam interactions.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class GrabbableComponent : Component
{
    /// <summary>Current grab stage this entity is held at.</summary>
    [DataField, AutoNetworkedField]
    public GrabStage GrabStage = GrabStage.No;

    /// <summary>
    /// Current chance this entity has to escape on resist.
    /// Recalculated from GrabIntentComponent.EscapeChances * ContestsSystem mass ratio each time it's checked.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float GrabEscapeChance = 1f;

    /// <summary>Multiplier applied to escape chance (e.g. martial arts can boost this).</summary>
    [DataField, AutoNetworkedField]
    public float EscapeAttemptModifier = 1f;

    /// <summary>Cooldown between escape attempts in seconds.</summary>
    [DataField]
    public TimeSpan EscapeAttemptCooldown = TimeSpan.FromSeconds(2f);

    /// <summary>Game time when the next escape attempt is allowed.</summary>
    [DataField, AutoNetworkedField]
    public TimeSpan NextEscapeAttempt;

    /// <summary>Whether this entity is currently being table-slammed.</summary>
    [DataField, AutoNetworkedField]
    public bool BeingTabled;

    /// <summary>Stamina damage dealt when slammed into a table.</summary>
    [DataField]
    public float TabledStaminaDamage = 40f;

    /// <summary>Blunt damage dealt when slammed into a table.</summary>
    [DataField]
    public float TabledDamage = 5f;

    /// <summary>Duration in seconds of the post-table-slam slow/daze.</summary>
    [DataField]
    public float PostTabledDuration = 3f;
}
