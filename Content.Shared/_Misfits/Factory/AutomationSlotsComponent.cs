// SPDX-License-Identifier: AGPL-3.0-or-later
// Adapted from Goob Station / Trauma Station

using Content.Shared._Misfits.Factory.Slots;

namespace Content.Shared._Misfits.Factory;

[RegisterComponent, NetworkedComponent]
public sealed partial class AutomationSlotsComponent : Component
{
    [DataField(required: true)]
    public List<AutomationSlot> Slots = new();
}
