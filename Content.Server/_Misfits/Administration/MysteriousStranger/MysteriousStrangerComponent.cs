// #Misfits Add - admin-controlled "mysterious stranger" that only its target player can see and hear.
namespace Content.Server._Misfits.Administration.MysteriousStranger;

/// <summary>
/// Marks an admin-controlled stranger entity. The entity sits on the
/// <see cref="Content.Shared.Eye.VisibilityFlags.MysteriousStranger"/> visibility layer, so PVS only ever
/// sends it to sessions whose eye mask contains that flag (the target player and admin observers).
/// Chat from this entity is filtered to the same audience in ChatSystem.GetRecipients.
/// </summary>
[RegisterComponent]
public sealed partial class MysteriousStrangerComponent : Component
{
    /// <summary>
    /// The player entity that can see and hear this stranger.
    /// </summary>
    [DataField]
    public EntityUid Target;

    /// <summary>
    /// The admin's body at spawn time (never a ghost - those are deleted on detach), so the Vanish verb
    /// can put them back. Null or deleted means Vanish falls back to spawning an admin observer.
    /// </summary>
    [DataField]
    public EntityUid? ReturnEntity;
}
