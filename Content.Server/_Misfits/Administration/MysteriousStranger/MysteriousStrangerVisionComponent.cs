// #Misfits Add - tracks which mysterious strangers a player can currently perceive.
namespace Content.Server._Misfits.Administration.MysteriousStranger;

/// <summary>
/// Placed on a player entity targeted by one or more mysterious strangers. While present, the player's eye
/// visibility mask includes <see cref="Content.Shared.Eye.VisibilityFlags.MysteriousStranger"/>; the flag is
/// cleared again when the component shuts down (last stranger vanished).
/// </summary>
[RegisterComponent]
public sealed partial class MysteriousStrangerVisionComponent : Component
{
    /// <summary>
    /// Strangers currently targeting this entity. The component is removed when this empties.
    /// </summary>
    [DataField]
    public HashSet<EntityUid> Strangers = new();
}
