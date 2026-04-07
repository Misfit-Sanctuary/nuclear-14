// #Misfits Add - Server handler for the Staff Panel "Activity Log" button.
// Receives a net message from the client and opens AdminLogsEui pre-filtered to
// significant admin-action log types for the current round (Verb, AdminMessage, etc.)
using Content.Server.Administration.Logs;
using Content.Server.Administration.Managers;
using Content.Server.EUI;
using Content.Shared._Misfits.Administration;
using Content.Shared.Administration;
using Content.Shared.Database;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Network;

namespace Content.Server._Misfits.Administration.Systems;

public sealed class MisfitsAdminActivitySystem : EntitySystem
{
    [Dependency] private readonly IAdminManager _adminMgr = default!;
    [Dependency] private readonly EuiManager _euiMgr = default!;
    [Dependency] private readonly IPlayerManager _playerMgr = default!;

    /// <summary>
    /// Log types that represent significant admin-initiated actions (verbs, spawns, deletes, etc.)
    /// </summary>
    private static readonly HashSet<LogType> AdminActionTypes = new()
    {
        LogType.Verb,           // Godmode, rejuv, freeze, erase, etc.
        LogType.AdminMessage,   // Subtle messages, prayers
        LogType.EntitySpawn,    // Admin-spawned entities
        LogType.EntityDelete,   // Admin-deleted entities
        LogType.Teleport,       // Teleports
        LogType.Mind,           // Ghost, repossess, suicide, objectives
        LogType.EventAnnounced, // Event announcements
        LogType.EventStarted,   // Events started
        LogType.EventRan,       // Events run manually
        LogType.EventStopped,   // Events stopped
    };

    /// <summary>
    /// Only show High/Extreme impact entries to cut through noise.
    /// </summary>
    private static readonly HashSet<LogImpact> AdminActionImpacts = new()
    {
        LogImpact.High,
        LogImpact.Extreme,
    };

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<MisfitsOpenActivityLogMsg>(OnOpenActivityLog);
    }

    private void OnOpenActivityLog(MisfitsOpenActivityLogMsg msg, EntitySessionEventArgs args)
    {
        var session = args.SenderSession;

        // Only admins with log access may open the activity log.
        if (!_adminMgr.HasAdminFlag(session, AdminFlags.Logs))
            return;

        // Open AdminLogsEui with the admin-action pre-filter applied before Opened() fires
        var eui = new AdminLogsEui();
        eui.SetInitialFilter(AdminActionTypes, AdminActionImpacts);
        _euiMgr.OpenEui(eui, session);
    }
}
