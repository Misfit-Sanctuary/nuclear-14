namespace Content.Shared._Misfits.Special.Components;

/// <summary>
/// Marks a Z.A.X unit whose SPECIAL must remain neutral for NPC and ghost-role use.
/// </summary>
[RegisterComponent]
public sealed partial class ZaxNeutralSpecialComponent : Component;

/// <summary>
/// Marks an admin-spawned Z.A.X player chassis that starts neutral and adopts the controller's saved SPECIAL on attach.
/// </summary>
[RegisterComponent]
public sealed partial class ZaxPlayerSpecialComponent : Component;

/// <summary>
/// Marks a Z.A.X Core or its inserted positronic brain as immune to mutable SPECIAL values.
/// Gameplay queries treat Charisma and Intelligence as 10 and every other stat as neutral.
/// </summary>
[RegisterComponent]
public sealed partial class ZaxCoreSpecialComponent : Component;
