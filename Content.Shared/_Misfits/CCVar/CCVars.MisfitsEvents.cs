using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

public sealed partial class CCVars
{
    /// <summary>
    ///     Master switch for automatic fork events (the MisfitsEventScheduler rule).
    ///     Unlike `misfitevent enable` / `misfitevent disable`, which only lasts for the
    ///     current round, this is archived to the server config and survives restarts.
    ///     Manual events (`misfitevent run` / `misfitevent roll`) still work while this is off,
    ///     so the pool can be tested without turning automatic events on.
    /// </summary>
    /// <remarks>
    ///     Defaults to disabled: the fork event pool spawns hostile mobs around players,
    ///     so a live round should only get them once an admin has opted in.
    /// </remarks>
    public static readonly CVarDef<bool> MisfitsEventsEnabled =
        CVarDef.Create("misfits.events_enabled", false, CVar.ARCHIVE | CVar.SERVERONLY);
}
