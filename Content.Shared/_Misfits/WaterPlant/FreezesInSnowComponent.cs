// #Cythisiax Added - Put this on a well and its water freezes while it is snowing.
// Nobody can draw water out of it until the snow stops.

using Robust.Shared.GameStates;

namespace Content.Shared._Misfits.WaterPlant;

[RegisterComponent, NetworkedComponent]
public sealed partial class FreezesInSnowComponent : Component
{
    [DataField]
    public LocId FrozenMessage = "misfits-well-frozen";
}
