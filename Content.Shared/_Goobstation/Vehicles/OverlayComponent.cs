namespace Content.Shared._Goobstation.Vehicles;

[RegisterComponent, NetworkedComponent]
public sealed partial class OverlayComponent : Component
{
    [DataField(required: true)]
    public EntProtoId OverlayPrototype;
}
