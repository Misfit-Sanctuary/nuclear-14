// SPDX-License-Identifier: AGPL-3.0-or-later
// Adapted from Goob Station / Trauma Station

using Content.Shared.Chemistry.Reagent;

namespace Content.Shared._Misfits.Factory.Plumbing;

/// <summary>
/// Adds a liquid filter which can be changed via BUI.
/// Does nothing on its own, other systems have to check for it in their logic.
/// </summary>
[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState]
public sealed partial class PlumbingFilterComponent : Component
{
    /// <summary>
    /// The reagent configured to be filtered.
    /// If this is set other reagents should be blocked by the machine.
    /// </summary>
    [DataField, AutoNetworkedField]
    public ProtoId<ReagentPrototype>? Filter;
}

[Serializable, NetSerializable]
public enum PlumbingFilterUiKey : byte
{
    Key
}

[Serializable, NetSerializable]
public sealed class PlumbingFilterChangeMessage(ProtoId<ReagentPrototype>? filter) : BoundUserInterfaceMessage
{
    public readonly ProtoId<ReagentPrototype>? Filter = filter;
}
