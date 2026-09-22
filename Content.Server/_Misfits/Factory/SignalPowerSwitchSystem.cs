// SPDX-License-Identifier: AGPL-3.0-or-later
// Adapted from Goob Station / Trauma Station

using Content.Server.DeviceLinking.Events;
using Content.Server.DeviceLinking.Systems;
using Content.Server.Power.EntitySystems;
using Content.Shared._Misfits.Factory;
using Content.Shared.Power;

namespace Content.Server._Misfits.Factory;

public sealed partial class SignalPowerSwitchSystem : EntitySystem
{
    [Dependency] private DeviceLinkSystem _device = default!;
    [Dependency] private PowerReceiverSystem _power = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SignalPowerSwitchComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<SignalPowerSwitchComponent, SignalReceivedEvent>(OnSignalReceived);
        SubscribeLocalEvent<SignalPowerSwitchComponent, PowerChangedEvent>(OnPowerChanged);
    }

    private void OnMapInit(Entity<SignalPowerSwitchComponent> ent, ref MapInitEvent args)
    {
        var (uid, comp) = ent;
        _device.EnsureSinkPorts(uid, comp.TogglePort, comp.OnPort, comp.OffPort);
        _device.EnsureSourcePorts(uid, comp.PoweredPort);
    }

    private void OnSignalReceived(Entity<SignalPowerSwitchComponent> ent, ref SignalReceivedEvent args)
    {
        var (uid, comp) = ent;
        var toggle = true;
        if (args.Port == comp.OnPort)
            toggle = !_power.IsPowered(uid);
        else if (args.Port == comp.OffPort)
            toggle = _power.IsPowered(uid);
        else if (args.Port != comp.TogglePort)
            return;

        if (toggle)
            _power.TogglePower(uid);
    }

    private void OnPowerChanged(Entity<SignalPowerSwitchComponent> ent, ref PowerChangedEvent args)
    {
        var (uid, comp) = ent;
        _device.SendSignal(uid, comp.PoweredPort, args.Powered);
    }
}
