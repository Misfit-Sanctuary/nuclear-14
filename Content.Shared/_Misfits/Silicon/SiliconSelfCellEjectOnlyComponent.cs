namespace Content.Shared._Misfits.Silicon;

/// <summary>
/// [Changed by MisfitsCrew/Operator] Marks player-silicon cell slots that only their owning chassis may eject.
/// External repair interactions remain allowed and are filtered by the slot's power-cell whitelist.
/// </summary>
[RegisterComponent]
public sealed partial class SiliconSelfCellEjectOnlyComponent : Component
{
    [DataField]
    public string SlotId = "cell_slot";
}
