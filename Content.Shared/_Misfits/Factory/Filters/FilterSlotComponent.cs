// SPDX-License-Identifier: AGPL-3.0-or-later
// Adapted from Goob Station / Trauma Station

using Content.Shared.Containers.ItemSlots;

namespace Content.Shared._Misfits.Factory.Filters;

/// <summary>
/// Component for machines that have a filter slot.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class FilterSlotComponent : Component
{
    /// <summary>
    /// Item slot that stores a filter.
    /// </summary>
    [DataField]
    public string FilterSlotId = "filter_slot";

    /// <summary>
    /// The filter slot cached on init.
    /// </summary>
    [ViewVariables]
    public ItemSlot FilterSlot = default!;

    /// <summary>
    /// The currently inserted filter.
    /// </summary>
    [ViewVariables]
    public EntityUid? Filter => FilterSlot.Item;
}
