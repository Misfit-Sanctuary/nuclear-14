namespace Content.Shared._Goobstation.Vehicles;

/// <summary>
/// Item slots that stop being interactable when its ent is driven. Only accessible to pilots
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class SlotsPilotBlockedComponent : Component
{
    [DataField(required: true)]
    public string[] BlockedSlots = [];
}
