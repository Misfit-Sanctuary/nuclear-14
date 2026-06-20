using Content.Shared.Actions;

namespace Content.Shared._Misfits.Silicon;

// [Changed by MisfitsCrew/Operator] Defines Station AI command action events for selecting and ordering ZAX NPC units.
public sealed partial class StationAiSelectNpcActionEvent : EntityTargetActionEvent;

public sealed partial class StationAiClearNpcSelectionActionEvent : InstantActionEvent;

public sealed partial class StationAiMoveSelectedNpcsActionEvent : WorldTargetActionEvent;

public sealed partial class StationAiFormationMoveSelectedNpcsActionEvent : WorldTargetActionEvent;

public sealed partial class StationAiEngageSelectedNpcsActionEvent : EntityTargetActionEvent;

public sealed partial class StationAiHoldSelectedNpcsActionEvent : InstantActionEvent;
