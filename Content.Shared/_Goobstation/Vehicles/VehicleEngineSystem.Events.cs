

namespace Content.Shared.Vehicles;
[ByRefEvent]
public record struct CheckCanEngineOnEv(EntityUid? User = null, bool CanTurnOn = false);
[ByRefEvent]
public record struct CheckCanEngineOffEv(EntityUid? User = null, bool CanTurnOff = false);
[ByRefEvent]
public record struct EngineOffEvent;
[ByRefEvent]
public record struct EngineOnEvent;

[ByRefEvent]
public record struct EngineInternalTryOffEvent(VehicleEngineComponent Comp, bool CanTurnOff = false, bool Handled = false);

[ByRefEvent]
public record struct EngineInternalTryOnEvent(VehicleEngineComponent Comp, bool CanTurnOn = false, bool Handled = false);
