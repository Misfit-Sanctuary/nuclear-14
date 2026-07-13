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
}

/// <summary>
/// [Changed by MisfitsCrew/Operator] Identifies the physical Z.A.X core. This prevents Station AI cores which share
/// AiHeld from gaining access to Z.A.X-only consciousness shunting.
/// </summary>
[RegisterComponent]
public sealed partial class ZaxCoreComponent : Component;
