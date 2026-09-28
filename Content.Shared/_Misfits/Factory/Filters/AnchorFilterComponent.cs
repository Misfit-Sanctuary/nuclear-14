// SPDX-License-Identifier: AGPL-3.0-or-later
// Adapted from Goob Station / Trauma Station

namespace Content.Shared._Misfits.Factory.Filters;

/// <summary>
/// Filter that requires an anchorable entity, and allows it if <c>Anchored == ItemToggleComponent.Activated</c>.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class AnchorFilterComponent : Component;
