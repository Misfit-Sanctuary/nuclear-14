using System.Numerics;
using Content.Server.Chat.Managers;
using Content.Shared._Misfits.Deathclaw;
using Content.Shared.Chat;
using Content.Shared.GameTicking;
using Content.Shared.Mobs;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Timing;
using Robust.Shared.Player;

namespace Content.Server._Misfits.Deathclaw;

/// <summary>
/// Round atmosphere, direct-kill flavor, and private map-wide soul sensing for Bwonsamdi.
/// </summary>
public sealed partial class BwonsamdiSenseSystem : EntitySystem
{
    [Dependency] private readonly IChatManager _chat = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private readonly Dictionary<(EntityUid Seer, EntityUid Victim, MobState State), TimeSpan> _nextSense = new();
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    private void OnMobStateChanged(MobStateChangedEvent args)
    {
        if (args.OldMobState == args.NewMobState
            || args.NewMobState is not (MobState.Critical or MobState.Dead)
            || !TryComp<ActorComponent>(args.Target, out _))
        {
            return;
        }

        if (args.NewMobState == MobState.Dead
            && args.OldMobState != MobState.Dead
            && args.Origin is { } origin
            && HasComp<BwonsamdiComponent>(origin)
            && origin != args.Target)
        {
            _chat.DispatchServerAnnouncement(Loc.GetString("bwonsamdi-grave-announcement"), Color.DarkSeaGreen);
        }

        var victimMap = Transform(args.Target).MapID;
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<BwonsamdiComponent, ActorComponent>();

        while (query.MoveNext(out var seer, out var bwonsamdi, out var actor))
        {
            if (seer == args.Target || Transform(seer).MapID != victimMap)
                continue;

            var key = (seer, args.Target, args.NewMobState);
            if (_nextSense.TryGetValue(key, out var next) && now < next)
                continue;

            _nextSense[key] = now + bwonsamdi.DeathSenseDebounce;
            SendSoulLocation(seer, actor, args.Target, args.NewMobState);
        }
    }

    private void SendSoulLocation(EntityUid seer, ActorComponent actor, EntityUid victim, MobState state)
    {
        var offset = _transform.GetMapCoordinates(victim).Position - _transform.GetMapCoordinates(seer).Position;
        var victimPosition = _transform.GetMapCoordinates(victim).Position;
        var message = Loc.GetString(
            state == MobState.Dead ? "bwonsamdi-death-sense-dead" : "bwonsamdi-death-sense-critical",
            ("name", Name(victim)),
            ("x", (int) victimPosition.X),
            ("y", (int) victimPosition.Y),
            ("distance", (int) MathF.Round(offset.Length())),
            ("direction", Loc.GetString(Direction(offset))));

        _chat.ChatMessageToOne(ChatChannel.Emotes, message, message, EntityUid.Invalid, false, actor.PlayerSession.Channel);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent args)
    {
        _nextSense.Clear();
    }

    private static string Direction(Vector2 offset)
    {
        var angle = MathF.Atan2(offset.Y, offset.X) * (180f / MathF.PI);
        if (angle < 0)
            angle += 360f;

        return angle switch
        {
            < 22.5f or >= 337.5f => "scent-direction-east",
            < 67.5f => "scent-direction-northeast",
            < 112.5f => "scent-direction-north",
            < 157.5f => "scent-direction-northwest",
            < 202.5f => "scent-direction-west",
            < 247.5f => "scent-direction-southwest",
            < 292.5f => "scent-direction-south",
            _ => "scent-direction-southeast"
        };
    }
}
