// #Misfits Add - Action event for cycling through scope zoom levels
using Content.Shared.Actions;

namespace Content.Shared._Misfits.Scope;

/// <summary>
/// Fired when the player presses the "Cycle Zoom Level" action button.
/// Only relevant for scopes with multiple zoom levels configured.
/// </summary>
public sealed partial class ScopeCycleZoomLevelEvent : InstantActionEvent;
