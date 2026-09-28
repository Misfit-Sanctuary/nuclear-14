using System.Numerics;
using Robust.Shared.GameStates;

namespace Content.Shared._Misfits.Expeditions;

/// <summary>
/// Replicates the presentation-only scale of a generated expedition boss.
/// The client applies this to the sprite without changing collision, movement,
/// or the entity's physical footprint.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(raiseAfterAutoHandleState: true)]
public sealed partial class ExpeditionBossVisualsComponent : Component
{
    [DataField, AutoNetworkedField]
    public Vector2 ScaleMultiplier = Vector2.One;
}
