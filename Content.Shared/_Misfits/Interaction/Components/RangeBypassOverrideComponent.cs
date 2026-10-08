namespace Content.Shared._Misfits.Interaction;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class RangeBypassOverrideComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool Override = true;

    [DataField, AutoNetworkedField]
    public bool DelCompOnRemove = true;
}
