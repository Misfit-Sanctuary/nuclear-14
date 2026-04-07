// #Misfits Add - Component on the puller tracking their current grab stage and grab parameters
using Content.Shared._Misfits.Grab;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Misfits.Grab;

/// <summary>
/// Added to an entity that is actively grabbing a pulled target.
/// Tracks which grab stage they are at and all associated parameters.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class GrabIntentComponent : Component
{
    /// <summary>Current grab stage this puller has on their pulled target.</summary>
    [DataField, AutoNetworkedField]
    public GrabStage GrabStage = GrabStage.No;

    /// <summary>Whether the grab is currently escalating or de-escalating.</summary>
    [DataField, AutoNetworkedField]
    public GrabStageDirection GrabStageDirection = GrabStageDirection.Increase;

    /// <summary>Minimum time between stage changes to prevent instant grab spam.</summary>
    [DataField]
    public TimeSpan StageChangeCooldown = TimeSpan.FromSeconds(1f);

    /// <summary>Game time at which the next stage change is allowed.</summary>
    [DataField, AutoNetworkedField]
    public TimeSpan NextStageChange;

    /// <summary>
    /// Movement speed modifiers applied to the PULLER per grab stage.
    /// Grabbing someone slows you down.
    /// </summary>
    [DataField]
    public Dictionary<GrabStage, float> GrabberSpeedModifiers = new()
    {
        { GrabStage.Soft,       0.9f },
        { GrabStage.Hard,       0.7f },
        { GrabStage.Suffocate,  0.4f },
    };

    /// <summary>
    /// Chance for the grabbed entity to escape on resist, per stage.
    /// 1.0 = 100%. Modified by ContestsSystem mass ratio.
    /// </summary>
    [DataField]
    public Dictionary<GrabStage, float> EscapeChances = new()
    {
        { GrabStage.No,         1f   },
        { GrabStage.Soft,       1f   },
        { GrabStage.Hard,       0.6f },
        { GrabStage.Suffocate,  0.2f },
    };

    /// <summary>Stamina damage dealt per tick while at Suffocate stage.</summary>
    [DataField]
    public float SuffocateGrabStaminaDamage = 10f;

    // ---- Throw parameters ----

    /// <summary>Base blunt damage when the grabbed entity is thrown.</summary>
    [DataField]
    public float GrabThrowDamage = 5f;

    /// <summary>Multiplier applied to throw damage.</summary>
    [DataField]
    public float GrabThrowDamageModifier = 2f;

    /// <summary>Speed the entity is thrown at.</summary>
    [DataField]
    public float GrabThrownSpeed = 7f;

    /// <summary>Maximum distance the entity can be thrown.</summary>
    [DataField]
    public float ThrowingDistance = 4f;

    // ---- Table slam parameters ----

    /// <summary>Minimum grab stage required to slam someone into a table.</summary>
    [DataField]
    public GrabStage TableSlamRequiredStage = GrabStage.Hard;

    /// <summary>Cooldown in seconds between table slams.</summary>
    [DataField]
    public float TableSlamCooldown = 3f;

    /// <summary>Maximum range to scan for tables to slam into.</summary>
    [DataField]
    public float TableSlamRange = 2f;

    /// <summary>Game time when the next table slam is allowed.</summary>
    [DataField, AutoNetworkedField]
    public TimeSpan NextTableSlam;

    // ---- Virtual item tracking ----

    /// <summary>
    /// How many virtual items are spawned in the puller's hands per grab stage.
    /// Occupying hands prevents them from using items freely while maintaining a choke.
    /// </summary>
    [DataField]
    public Dictionary<GrabStage, int> GrabVirtualItemStageCount = new()
    {
        { GrabStage.Suffocate, 1 },
    };

    /// <summary>EntityUids of virtual items currently spawned for the grab.</summary>
    [DataField, AutoNetworkedField]
    public List<EntityUid> SpawnedVirtualItems = new();

    // ---- Sounds ----

    [DataField]
    public SoundSpecifier? GrabSound = new SoundPathSpecifier("/Audio/Effects/thudswoosh.ogg");

    [DataField]
    public SoundSpecifier? SuffocateSound = new SoundPathSpecifier("/Audio/Effects/choke.ogg");
}
