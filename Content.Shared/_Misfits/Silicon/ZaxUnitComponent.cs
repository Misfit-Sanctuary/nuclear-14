namespace Content.Shared._Misfits.Silicon;

/// <summary>
/// [Changed by MisfitsCrew/Operator] Marks NPC silicon units that may receive Station AI field commands.
/// </summary>
[RegisterComponent]
public sealed partial class ZaxUnitComponent : Component;

/// <summary>
/// [Changed by MisfitsCrew/Operator] Marks any Z.A.X-linked chassis that should appear in
/// the Z.A.X Core linked-unit directory.
/// </summary>
[RegisterComponent]
public sealed partial class ZaxLinkedUnitComponent : Component;

/// <summary>
/// Marks a Z.A.X Core mind or chassis as a consumer of the independent Z.A.X
/// tactical-map feed. This intentionally does not reuse Brotherhood map markers.
/// </summary>
[RegisterComponent]
public sealed partial class ZaxTacticalMapComponent : Component;

/// <summary>
/// Identifies a player-selectable Z.A.X chassis family for Foundry population limits.
/// Variants share a family so they consume the same cap.
/// </summary>
[RegisterComponent]
public sealed partial class ZaxPlayerChassisComponent : Component
{
    [DataField(required: true)]
    public ZaxPlayerChassisKind Kind;
}

public enum ZaxPlayerChassisKind : byte
{
    Securitron,
    Assaultron,
    Protectron,
    MrGutsy,
    MrHandy,
}

/// <summary>
/// [Changed by MisfitsCrew/Operator] Marks the Z.A.X machine foundry whose unit recipes are governed by the global
/// active-unit and C-27 limits.
/// </summary>
[RegisterComponent]
public sealed partial class ZaxMachineFoundryComponent : Component
{
    [DataField]
    public int MaxActiveUnits = 10;

    [DataField]
    public int MaxActiveC27s = 1;

    /// <summary>
    /// Player-chassis recipes open a late-join slot instead of leaving an unoccupied chassis.
    /// </summary>
    [DataField]
    public Dictionary<string, string> PlayerJobRecipes = new();

    /// <summary>
    /// Maximum live or pending player chassis for each shared Z.A.X job.
    /// </summary>
    [DataField]
    public Dictionary<string, int> PlayerJobCaps = new();
}

/// <summary>
/// [Changed by MisfitsCrew/Operator] Identifies the physical Z.A.X core. This prevents Station AI cores which share
/// AiHeld from gaining access to Z.A.X-only consciousness shunting.
/// </summary>
[RegisterComponent]
public sealed partial class ZaxCoreComponent : Component;

/// <summary>
/// Marks the occupied Z.A.X Core intelligence as the authoritative leader of the Z.A.X faction.
/// </summary>
[RegisterComponent]
public sealed partial class ZaxFactionLeaderComponent : Component;
