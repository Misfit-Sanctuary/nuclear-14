// #Misfits Add - Combo attack type enum and event for the martial arts combo engine
using Robust.Shared.Serialization;

namespace Content.Shared._Misfits.MartialArts;

/// <summary>
/// Classifies which type of attack was performed, feeding into the combo input buffer.
/// </summary>
[Serializable, NetSerializable]
public enum MisfitsComboAttackType : byte
{
    Harm,      // Standard light attack
    HarmLight, // Wide/heavy attack swing
    Disarm,    // Disarm shove
    Grab,      // Grab stage escalation click on pulled target
    Hug,       // Empty-hand interact (InteractHandEvent) on target
}

/// <summary>
/// Raised as a directed local event on the performer whenever they perform an attack.
/// The martial arts combo engine listens to this to populate the input buffer.
/// </summary>
public sealed class MisfitsComboAttackPerformedEvent : CancellableEntityEventArgs
{
    public EntityUid Performer { get; }
    public EntityUid Target { get; }
    public EntityUid Weapon { get; }
    public MisfitsComboAttackType Type { get; }

    public MisfitsComboAttackPerformedEvent(EntityUid performer, EntityUid target, EntityUid weapon, MisfitsComboAttackType type)
    {
        Performer = performer;
        Target = target;
        Weapon = weapon;
        Type = type;
    }
}
