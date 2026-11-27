namespace Content.Shared._Goobstation.Vehicles;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class OverlayComponent : Component
{
    [DataField]
    public EntProtoId OverlayPrototype = "Error";

    [AutoNetworkedField]
    public EntityUid OverlayEntity = EntityUid.Invalid;

    [AutoNetworkedField]
    public bool Equipped = false;

    [AutoNetworkedField]
    public bool Base = true;

}
[Serializable, NetSerializable]
public enum OverlayMaps : byte
{
    Base,
    Equippable
}
[Serializable, NetSerializable]
public enum OverlayLayer : byte
{
    Primary
}
