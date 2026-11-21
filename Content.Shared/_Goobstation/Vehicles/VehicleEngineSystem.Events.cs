

namespace Content.Shared.Vehicles;
[ByRefEvent]
public record struct RequestEngineOnEv(EntityUid? User = null, bool CanTurnOn = false, bool Handled = false);
[ByRefEvent]
public record struct RequestEngineOffEv(EntityUid? User = null, bool CanTurnOff = false, bool Handled = false);
[ByRefEvent]
public record struct EngineOffEvent;
[ByRefEvent]
public record struct EngineOnEvent;

[ByRefEvent]
public record struct EngineTryOffEvent(VehicleEngineComponent Comp, bool CanTurnOff = false, bool Handled = false);

[ByRefEvent]
public record struct EngineTryOnEvent(VehicleEngineComponent Comp, bool CanTurnOn = false, bool Handled = false);
