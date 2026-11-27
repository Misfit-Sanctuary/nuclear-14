namespace Content.Shared.Vehicles;

[RegisterComponent, NetworkedComponent]
public sealed partial class DriveOnBuckleComponent :  Component
{
    /// <summary>
    /// Pilots
    /// </summary>
    [ViewVariables]
    public List<EntityUid> Pilots = new();

    [DataField]
    public int MaxPilots = 1;
}
