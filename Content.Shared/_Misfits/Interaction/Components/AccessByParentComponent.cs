namespace Content.Shared._Misfits.Interaction;


[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class AccessByParentComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool DelCompOnRemove = true;

}
