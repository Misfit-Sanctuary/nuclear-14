namespace Content.Shared._Misfits.Interaction;

/// <summary>
/// misfits: made in context of accessing backpacks via stripmenu
/// which do not consider size of parent
/// this overrides reach checks to go by closeness of parent's hard hitxbox/fixture
/// not direct parent, but rather greatest parent up to but not including the map
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class AccessByParentComponent : Component
{
    [DataField]
    public bool DelCompOnRemove = true;

}
