// SPDX-License-Identifier: AGPL-3.0-or-later
// Adapted from Goob Station / Trauma Station

using Content.Shared.Construction.Prototypes;

namespace Content.Shared._Misfits.Factory;

[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState]
public sealed partial class ConstructorComponent : Component
{
    /// <summary>
    /// The construction it will try to build when start is invoked.
    /// </summary>
    [DataField, AutoNetworkedField]
    public ProtoId<ConstructionPrototype>? Construction;
}

[Serializable, NetSerializable]
public enum ConstructorUiKey : byte
{
    Key
}

[Serializable, NetSerializable]
public sealed class ConstructorSetProtoMessage(ProtoId<ConstructionPrototype>? id) : BoundUserInterfaceMessage
{
    public ProtoId<ConstructionPrototype>? Id = id;
}
