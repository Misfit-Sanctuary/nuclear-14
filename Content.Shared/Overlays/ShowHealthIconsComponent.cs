using Robust.Shared.Prototypes;
using Content.Shared.Damage.Prototypes;
using Robust.Shared.GameStates;

namespace Content.Shared.Overlays;

/// <summary>
/// This component allows you to see health status icons above damageable mobs.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(raiseAfterAutoHandleState: true)]
public sealed partial class ShowHealthIconsComponent : Component
{
    /// <summary>
    /// Displays health status icons of the damage containers.
    /// </summary>
    [DataField("damageContainers"), AutoNetworkedField]
    public List<ProtoId<DamageContainerPrototype>> DamageContainers = new();
}
