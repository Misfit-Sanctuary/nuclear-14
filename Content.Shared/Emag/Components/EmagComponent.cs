using Robust.Shared.Prototypes;
using Content.Shared.Emag.Systems;
using Content.Shared.Tag;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared.Emag.Components;

[Access(typeof(EmagSystem))]
[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState]
public sealed partial class EmagComponent : Component
{
    /// <summary>
    /// The tag that marks an entity as immune to emags
    /// </summary>
    [DataField("emagImmuneTag"), ViewVariables(VVAccess.ReadWrite)]
    [AutoNetworkedField]
    public ProtoId<TagPrototype> EmagImmuneTag = "EmagImmune";
}
