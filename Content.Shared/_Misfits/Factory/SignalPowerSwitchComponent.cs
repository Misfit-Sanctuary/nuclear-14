// SPDX-License-Identifier: AGPL-3.0-or-later
// Adapted from Goob Station / Trauma Station

using Content.Shared.DeviceLinking;

namespace Content.Shared._Misfits.Factory;

/// <summary>
/// Adds toggle/on/off sinks and powered source ports.
/// Allows for signal control similar to manual <c>PowerSwitch</c>.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class SignalPowerSwitchComponent : Component
{
    [DataField]
    public ProtoId<SinkPortPrototype> TogglePort = "Toggle";

    [DataField]
    public ProtoId<SinkPortPrototype> OnPort = "On";

    [DataField]
    public ProtoId<SinkPortPrototype> OffPort = "Off";

    [DataField]
    public ProtoId<SourcePortPrototype> PoweredPort = "Powered";
}
