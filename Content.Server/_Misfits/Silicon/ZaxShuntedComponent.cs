namespace Content.Server._Misfits.Silicon;

/// <summary>
/// [Changed by MisfitsCrew/Operator] Stores server-only state on a Z.A.X NPC chassis while the core mind is
/// visiting it. The original brain remains physically secured inside the core.
/// </summary>
[RegisterComponent]
public sealed partial class ZaxShuntedComponent : Component
{
    public EntityUid Brain;
    public EntityUid Core;
    public readonly List<EntityUid> GrantedActions = new();
    public bool RestoreGhostTakeover;
    public bool Returning;

    [DataField]
    public float CommandRange = 12f;
}
