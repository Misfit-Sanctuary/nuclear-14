// SPDX-License-Identifier: AGPL-3.0-or-later
// Adapted from Goob Station / Trauma Station

using Content.Shared._Misfits.Factory.Slots;
using Content.Shared.DeviceLinking;

namespace Content.Shared._Misfits.Factory;

public partial interface IExclusiveSlotComponent : IComponent
{
    /// <summary>
    /// Port on this machine that other machines link to.
    /// </summary>
    string PortId { get; }

    /// <summary>
    /// Machine linked to <see cref="Port"/>.
    /// </summary>
    EntityUid? LinkedMachine { get; set; }

    /// <summary>
    /// The source or sink port of the linked machine.
    /// </summary>
    /// <remarks>
    /// Not using protoid as it can be either a sink or source, and prototypes don't set it anyway.
    /// </remarks>
    string? LinkedPort { get; set; }

    /// <summary>
    /// The resolved automation slot of the linked machine.
    /// Updated by <c>UpdateSlot</c> as this is not directly networked.
    /// </summary>
    AutomationSlot? LinkedSlot { get; set; }

    bool IsInput { get; }
}

[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState(true)]
public sealed partial class ExclusiveInputSlotComponent : Component, IExclusiveSlotComponent
{
    [DataField(required: true)]
    public ProtoId<SinkPortPrototype> Port;
    public string PortId => Port;

    [DataField, AutoNetworkedField]
    public EntityUid? LinkedMachine { get; set; }

    [DataField, AutoNetworkedField]
    public string? LinkedPort { get; set; }

    [ViewVariables]
    public AutomationSlot? LinkedSlot { get; set; }

    public bool IsInput => true;
}

[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState(true)]
public sealed partial class ExclusiveOutputSlotComponent : Component, IExclusiveSlotComponent
{
    [DataField(required: true)]
    public ProtoId<SourcePortPrototype> Port;
    public string PortId => Port;

    [DataField, AutoNetworkedField]
    public EntityUid? LinkedMachine { get; set; }

    [DataField, AutoNetworkedField]
    public string? LinkedPort { get; set; }

    [ViewVariables]
    public AutomationSlot? LinkedSlot { get; set; }

    public bool IsInput => false;
}
