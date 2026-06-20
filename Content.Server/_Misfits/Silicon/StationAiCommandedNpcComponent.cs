namespace Content.Server._Misfits.Silicon;

/// <summary>
/// [Changed by MisfitsCrew/Operator] Server-side bookkeeping for NPCs temporarily controlled by a Station AI order.
/// </summary>
[RegisterComponent]
public sealed partial class StationAiCommandedNpcComponent : Component
{
    [DataField]
    public EntityUid Commander;

    [DataField]
    public EntityUid CommanderCore;

    [DataField]
    public string OriginalRootTask = string.Empty;

    [DataField]
    public EntityUid? ForcedHostile;

    [DataField]
    public bool HoldingCommand;
}
