namespace Content.Shared._Misfits.Interaction;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class OpenSlotComponent : Component
{

    [DataField, AutoNetworkedField]
    public List<string> OpenSlots = new();

}
