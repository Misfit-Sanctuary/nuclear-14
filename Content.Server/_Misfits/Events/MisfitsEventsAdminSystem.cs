using System.Linq;
using Content.Server.Administration.Managers;
using Content.Shared._Misfits.Events;
using Content.Shared.Administration;
using Content.Shared.CCVar;
using Content.Shared.Ghost;
using Robust.Shared.Configuration;
using Robust.Shared.Player;

namespace Content.Server._Misfits.Events;

// Backs the admin menu Events tab and the ghost [Warp] chat link; every request needs Debug.
public sealed class MisfitsEventsAdminSystem : EntitySystem
{
    private const string SchedulerFailed = "Could not start the event scheduler.";

    [Dependency] private readonly MisfitsEventSystem _events = default!;
    [Dependency] private readonly MisfitsEventSchedulerSystem _scheduler = default!;
    [Dependency] private readonly IAdminManager _admin = default!;
    [Dependency] private readonly IConfigurationManager _config = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<MisfitsEventsStateRequest>(OnStateRequest);
        SubscribeNetworkEvent<MisfitsEventAdminActionRequest>(OnActionRequest);
        SubscribeNetworkEvent<MisfitsEventWarpRequest>(OnWarpRequest);
    }

    private void OnStateRequest(MisfitsEventsStateRequest msg, EntitySessionEventArgs args)
    {
        if (!_admin.HasAdminFlag(args.SenderSession, AdminFlags.Debug))
            return;

        RaiseNetworkEvent(BuildState(), args.SenderSession.Channel);
    }

    private void OnActionRequest(MisfitsEventAdminActionRequest msg, EntitySessionEventArgs args)
    {
        if (!_admin.HasAdminFlag(args.SenderSession, AdminFlags.Debug))
            return;

        string message;
        switch (msg.Action)
        {
            case MisfitsEventAdminAction.Roll:
            {
                var rolled = _events.RunRandomEvent();
                message = rolled == null
                    ? "No event was eligible to roll."
                    : DescribeRun(MetaData(rolled.Value).EntityPrototype?.ID ?? "event", rolled);
                break;
            }

            case MisfitsEventAdminAction.Run:
                message = DescribeRun(msg.EventId, _events.RunEvent(msg.EventId));
                break;

            case MisfitsEventAdminAction.SetInterval:
                message = _scheduler.SetInterval(msg.Min, msg.Max)
                    ? $"Automatic rolls now every {Math.Min(msg.Min, msg.Max)}-{Math.Max(msg.Min, msg.Max)}s."
                    : SchedulerFailed;
                break;

            case MisfitsEventAdminAction.SetNextRoll:
                message = _scheduler.SetNextRoll(msg.Seconds)
                    ? $"Next automatic roll in {Math.Max(0f, msg.Seconds):0}s."
                    : SchedulerFailed;
                break;

            case MisfitsEventAdminAction.SetAutoEnabled:
                message = _scheduler.SetEnabled(msg.Enabled)
                    ? msg.Enabled ? "Scheduler enabled for this round." : "Scheduler disabled for this round."
                    : SchedulerFailed;
                break;

            case MisfitsEventAdminAction.WarpToLastSpawn:
                message = TryWarpToLastSpawn(msg.EventId, args.SenderSession);
                break;

            case MisfitsEventAdminAction.SetDisabled:
                _events.SetEventDisabled(msg.EventId, msg.Disabled);
                message = msg.Disabled
                    ? $"{msg.EventId} will not be rolled automatically."
                    : $"{msg.EventId} can be rolled automatically again.";
                break;

            case MisfitsEventAdminAction.SetCvarEnabled:
                _config.SetCVar(CCVars.MisfitsEventsEnabled, msg.Enabled);
                message = msg.Enabled
                    ? "misfits.events_enabled set. Use saveconfig to keep it across restarts."
                    : "misfits.events_enabled cleared. Automatic rolls are paused.";
                break;

            default:
                message = string.Empty;
                break;
        }

        RaiseNetworkEvent(BuildState(message), args.SenderSession.Channel);
    }

    // Teleports the admin to where the event last spawned.
    private string TryWarpToLastSpawn(string eventId, ICommonSession session)
    {
        if (session.AttachedEntity is not { Valid: true } mob)
            return "Cannot teleport: you are not in a body or ghost.";

        if (!_events.GetLastRuns().TryGetValue(eventId, out var last) || last.At is not { } at)
            return $"No {eventId} spawn recorded this round, so it has not spawned anywhere yet.";

        _transform.SetMapCoordinates(mob, at);
        return last.Count > 0
            ? $"Teleported to the last {eventId} spawn near '{last.Location ?? "unknown"}' at {at.Position} " +
              $"({last.Count} entities)."
            : $"Teleported to where {eventId} last ran - it spawned nothing there.";
    }

    // Reports what a started event did, so a run that spawned nothing is visible.
    private string DescribeRun(string eventId, EntityUid? rule)
    {
        if (rule is not { } ruleUid)
            return $"Could not start {eventId} (unknown id or already running).";

        if (!TryComp<MisfitsEventComponent>(ruleUid, out var component))
            return $"Started {eventId}.";

        if (component.SpawnedAt is not { } at)
        {
            return component.LastFailure is { } why
                ? $"Started {eventId}, but nothing spawned: {why}."
                : $"Started {eventId}, waiting for it to spawn.";
        }

        return component.LastSpawnedCount > 0
            ? $"Started {eventId}: spawned {component.LastSpawnedCount} entities near {at.Position}."
            : $"Started {eventId}, but nothing spawned: {component.LastFailure ?? "unknown reason"}.";
    }

    // Warps a ghost to where the event spawned.
    private void OnWarpRequest(MisfitsEventWarpRequest msg, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { Valid: true } ghost)
            return;

        if (!HasComp<GhostComponent>(ghost))
            return;

        if (!TryGetEntity(msg.Rule, out var rule) || rule is not { } ruleUid)
            return;

        if (!TryComp<MisfitsEventComponent>(ruleUid, out var component) || component.SpawnedAt is not { } coords)
            return;

        _transform.SetMapCoordinates(ghost, coords);
    }

    private MisfitsEventsStateEvent BuildState(string message = "")
    {
        var state = new MisfitsEventsStateEvent { Message = message };
        var available = _events.AvailableEvents();
        var lastRuns = _events.GetLastRuns();

        foreach (var (proto, component) in _events.AllEvents().OrderByDescending(pair => pair.Value.Weight))
        {
            var info = new MisfitsEventInfo
            {
                Id = proto.ID,
                Weight = component.Weight,
                Eligible = available.ContainsKey(proto),
                Disabled = _events.IsEventDisabled(proto.ID),
            };

            if (lastRuns.TryGetValue(proto.ID, out var last))
            {
                if (last.At is { } at)
                {
                    info.HasSpawn = true;
                    info.SpawnX = at.Position.X;
                    info.SpawnY = at.Position.Y;
                }

                info.SpawnedCount = last.Count;
                info.SpawnLocation = last.Location ?? string.Empty;
                info.Failure = last.Failure ?? string.Empty;
            }

            state.Events.Add(info);
        }

        state.CvarEnabled = _scheduler.EventsEnabled;

        if (_scheduler.GetScheduler() is { } scheduler)
        {
            state.SchedulerRunning = true;
            state.SchedulerEnabled = scheduler.Comp.Enabled;
            state.MinInterval = scheduler.Comp.MinInterval;
            state.MaxInterval = scheduler.Comp.MaxInterval;
            state.NextRoll = Math.Max(0f, scheduler.Comp.TimeUntilNextEvent);
        }

        return state;
    }
}
