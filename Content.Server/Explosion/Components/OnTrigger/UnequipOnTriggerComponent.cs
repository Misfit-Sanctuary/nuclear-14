/// <summary>
/// Made for misfits. Very simple comp trigger whose associated method handler loops through TryUnequip for every
/// slots defined in SlotsToUnequip. Additional fields fill in the parameters of TryUnequip.
/// Handler will check for inventory comp, exiting early without throwing if it cannot find it
/// to prevent conflict with other systems that may want to mess with it
///
/// Example of use in
/// </summary>
[RegisterComponent]
public sealed partial class UnequipOnTriggerComponent : Component
{
    [DataField, ViewVariables]
    public string[] SlotsToUnequip = [];
    /// <summary>
    /// if false, unequipped slot will go to nullspace else itll be dropped 'normally'
    /// aka reparented to new parent on old parent's coords.
    /// So if unequipped will drop on ground, or drop in container.
    /// </summary>
    ///
    /// <remarks>
    /// if this is set to false and you dont plan on using the unequipped ent after,
    /// then dont use this comp and just delete it
    /// </remarks>
    [DataField, ViewVariables]
    public bool DropToNearestParent = true;
    [DataField, ViewVariables]
    public bool Forced = true;

    /// <summary>
    /// if unequip slot has a doafter use it to unequip
    /// </summary>
    [DataField, ViewVariables]
    public bool CheckForDoAfter = false;

    [DataField, ViewVariables]
    public bool IsSilent = false;
}
