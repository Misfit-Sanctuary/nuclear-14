namespace Content.Shared._Misfits.Expeditions;

/// <summary>
/// Placed on the rope ladder inside an expedition map. Interacting with it
/// returns a session member, plus anything handled by normal ladder warping,
/// to the exact surface entrance that launched the expedition.
/// </summary>
[RegisterComponent]
public sealed partial class N14ExpeditionExitComponent : Component
{
    /// <summary>
    /// The expedition map entity this exit belongs to.
    /// Used to look up the return coordinates.
    /// </summary>
    [DataField]
    public EntityUid ExpeditionMap;

}
