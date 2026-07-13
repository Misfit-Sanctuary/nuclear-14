using Content.Shared.Actions;
using Content.Shared.UserInterface;
using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._Misfits.Silicon;

// [Changed by MisfitsCrew/Operator] Defines Station AI command action events for selecting and ordering ZAX NPC units.
public sealed partial class StationAiSelectNpcActionEvent : EntityTargetActionEvent;

public sealed partial class StationAiClearNpcSelectionActionEvent : InstantActionEvent;

public sealed partial class StationAiMoveSelectedNpcsActionEvent : WorldTargetActionEvent;

public sealed partial class StationAiFormationMoveSelectedNpcsActionEvent : WorldTargetActionEvent;

public sealed partial class StationAiMoveAndAttackSelectedNpcsActionEvent : WorldTargetActionEvent;

public sealed partial class StationAiEngageSelectedNpcsActionEvent : EntityTargetActionEvent;

public sealed partial class StationAiHoldSelectedNpcsActionEvent : InstantActionEvent;

/// <summary>
/// [Changed by MisfitsCrew/Operator] Moves the Z.A.X core player's mind into a visible, unoccupied NPC Z.A.X chassis.
/// </summary>
public sealed partial class ZaxShuntActionEvent : EntityTargetActionEvent;

/// <summary>
/// [Changed by MisfitsCrew/Operator] Returns a shunted Z.A.X mind to the brain held by its original core.
/// </summary>
public sealed partial class ZaxReturnToCoreActionEvent : InstantActionEvent;

[Serializable, NetSerializable]
public sealed class StationAiNpcMoveTargetingFinishedEvent : EntityEventArgs;

[Serializable, NetSerializable]
public enum ZaxLinkedUnitsUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public enum ZaxLinkedUnitKind : byte
{
    Npc,
    Player,
    GhostRole,
}

[Serializable, NetSerializable]
public readonly record struct ZaxLinkedUnitEntry(
    NetEntity Entity,
    string Name,
    ZaxLinkedUnitKind Kind,
    string Location,
    NetCoordinates Coordinates);

[Serializable, NetSerializable]
public sealed class ZaxLinkedUnitsBoundUserInterfaceState : BoundUserInterfaceState
{
    public readonly ZaxLinkedUnitEntry[] Units;

    public ZaxLinkedUnitsBoundUserInterfaceState(ZaxLinkedUnitEntry[] units)
    {
        Units = units;
    }
}

[Serializable, NetSerializable]
public sealed class ZaxLinkedUnitsRefreshMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class ZaxLinkedUnitsWarpMessage : BoundUserInterfaceMessage
{
    public readonly NetEntity Target;

    public ZaxLinkedUnitsWarpMessage(NetEntity target)
    {
        Target = target;
    }
}
