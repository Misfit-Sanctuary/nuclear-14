using Content.Server.GameTicking;
using Content.Server.Players.PlayTimeTracking;
using Content.Shared._Misfits.NCRRankPin;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Roles;
using Robust.Shared.Prototypes;

namespace Content.Server._Misfits.NCRRankPin;

public sealed class NCRRankPinSpawnSystem : EntitySystem
{
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly PlayTimeTrackingManager _playTime = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;

    public override void Initialize() =>
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawnComplete);

    private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent args)
    {
        if (args.JobId == null)
            return;

        NCRRankPinProgressionPrototype? progression = null;
        foreach (var proto in _proto.EnumeratePrototypes<NCRRankPinProgressionPrototype>())
        {
            foreach (var protoJob in proto.Jobs)
            {
                if (protoJob.Id != args.JobId)
                    continue;
                progression = proto;
                break;
            }
            if (progression != null)
                break;
        }

        if (progression == null || !progression.SpawnOnRoundstart)
            return;

        if (!_playTime.TryGetTrackerTimes(args.Player, out var times))
            return;

        var totalSeconds = GetTrackerSeconds(times, progression);

        EntProtoId? pinEntity = null;
        var bestMin = -1;
        foreach (var threshold in progression.Thresholds)
        {
            if (totalSeconds >= threshold.Min && threshold.Min >= bestMin)
            {
                bestMin = threshold.Min;
                pinEntity = threshold.Entity;
            }
        }

        if (pinEntity == null)
            return;

        var pin = Spawn(pinEntity.Value, Transform(args.Mob).Coordinates);
        if (!_hands.TryPickupAnyHand(args.Mob, pin, checkActionBlocker: false))
            QueueDel(pin);
    }

    private int GetTrackerSeconds(Dictionary<string, TimeSpan> times, NCRRankPinProgressionPrototype proto)
    {
        if (!proto.DepartmentTracker)
            return times.TryGetValue(proto.Tracker, out var t) ? (int)t.TotalSeconds : 0;

        if (!_proto.TryIndex<DepartmentPrototype>(proto.Tracker, out var dept))
            return 0;

        var total = TimeSpan.Zero;
        foreach (var role in dept.Roles)
        {
            // Resolve the job's actual playTimeTracker ID so renamed trackers are found correctly.
            var trackerId = _proto.TryIndex<JobPrototype>(role, out var jobProto)
                ? jobProto.PlayTimeTracker
                : role.Id;
            if (times.TryGetValue(trackerId, out var roleTime))
                total += roleTime;
        }
        return (int)total.TotalSeconds;
    }
}
