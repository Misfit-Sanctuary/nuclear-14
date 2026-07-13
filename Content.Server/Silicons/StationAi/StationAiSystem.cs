using System.Linq;
using System.Diagnostics.CodeAnalysis;
using Content.Server.Chat.Managers;
using Content.Server.Chat.Systems;
using Content.Shared.Chat;
using Content.Shared.Examine;
using Content.Shared.Mind;
using Content.Shared.Pointing;
using Content.Shared.Power;
using Content.Shared.Power.Components;
using Content.Shared.Roles;
using Content.Shared.Silicons.StationAi;
using Content.Shared.StationAi;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Player;
using static Content.Server.Chat.Systems.ChatSystem;

namespace Content.Server.Silicons.StationAi;

public sealed class StationAiSystem : SharedStationAiSystem
{
    [Dependency] private readonly IChatManager _chats = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly ExamineSystemShared _examine = default!;
    [Dependency] private readonly SharedTransformSystem _xforms = default!;
    [Dependency] private readonly SharedMindSystem _mind = default!;
    [Dependency] private readonly SharedRoleSystem _roles = default!;
    [Dependency] private readonly ViewSubscriberSystem _viewSubscriber = default!;

    private readonly HashSet<Entity<StationAiCoreComponent>> _ais = new();
    // [Changed by MisfitsCrew/Operator] Tracks which AI player sessions were subscribed
    // to which Station AI vision sources so camera PVS can be added and removed safely.
    private readonly Dictionary<EntityUid, HashSet<EntityUid>> _visionSubscriptions = new();
    private readonly HashSet<EntityUid> _desiredVisionSubscriptions = new();

    // [Changed by MisfitsCrew/Operator] Mirrors normal pointing reach when the AI must
    // fall back from camera-source attribution to its active relayed eye entity.
    private const float AiPointingRange = 15f;

    private EntityQuery<BroadphaseComponent> _broadphaseQuery;

    public override void Initialize()
    {
        base.Initialize();

        _broadphaseQuery = GetEntityQuery<BroadphaseComponent>();

        SubscribeLocalEvent<ExpandICChatRecipientsEvent>(OnExpandICChatRecipients);
        // [Changed by MisfitsCrew/Operator] Hooks Station AI point attempts from any
        // attached AI entity so they originate from the nearest supervised camera/core.
        SubscribeLocalEvent<GetPointingSourceEvent>(OnAiGetPointingSource);
        // [Changed by MisfitsCrew/Operator] Watches AI vision source startup/shutdown to
        // keep active AI camera PVS subscriptions in sync with mapped cameras.
        SubscribeLocalEvent<StationAiVisionComponent, ComponentStartup>(OnAiVisionStartup);
        SubscribeLocalEvent<StationAiVisionComponent, ComponentShutdown>(OnAiVisionShutdown);
    }

    private void OnExpandICChatRecipients(ExpandICChatRecipientsEvent ev)
    {
        var xformQuery = GetEntityQuery<TransformComponent>();
        var sourceXform = Transform(ev.Source);
        var sourcePos = _xforms.GetWorldPosition(sourceXform, xformQuery);

        // This function ensures that chat popups appear on camera views that have connected microphones.
        var query = EntityManager.EntityQueryEnumerator<StationAiCoreComponent, TransformComponent>();
        while (query.MoveNext(out var ent, out var entStationAiCore, out var entXform))
        {
            var stationAiCore = new Entity<StationAiCoreComponent>(ent, entStationAiCore);

            if (!TryGetInsertedAI(stationAiCore, out var insertedAi) || !TryComp(insertedAi, out ActorComponent? actor))
                continue;

            if (stationAiCore.Comp.RemoteEntity == null || stationAiCore.Comp.Remote)
                continue;

            var xform = Transform(stationAiCore.Comp.RemoteEntity.Value);

            var range = (xform.MapID != sourceXform.MapID)
                ? -1
                : (sourcePos - _xforms.GetWorldPosition(xform, xformQuery)).Length();

            if (range < 0 || range > ev.VoiceRange)
                continue;

            ev.Recipients.TryAdd(actor.PlayerSession, new ICChatRecipientData(range, false));
        }
    }

    public override bool SetVisionEnabled(Entity<StationAiVisionComponent> entity, bool enabled, bool announce = false)
    {
        if (!base.SetVisionEnabled(entity, enabled, announce))
            return false;

        // [Changed by MisfitsCrew/Operator] Updates active AI camera PVS subscriptions
        // whenever a camera/core vision wire enables or disables Station AI supervision.
        if (enabled)
            AddVisionToRelevantAis(entity.Owner);
        else
            RemoveVisionFromAllAis(entity.Owner);

        if (announce)
        {
            AnnounceSnip(entity.Owner);
        }

        return true;
    }

    private void OnAiGetPointingSource(ref GetPointingSourceEvent args)
    {
        // [Changed by MisfitsCrew/Operator] Resolves Station AI pointing from visible
        // supervised sources and rejects points outside the powered core's camera network.
        if (!TryResolvePointingCore(args.Pointer, out var core))
            return;

        args.Handled = true;

        if (!IsCorePowered(core.Value.Owner))
        {
            args.Cancelled = true;
            return;
        }

        // [Changed by MisfitsCrew/Operator] Resolve the clicked coordinate's own grid
        // instead of requiring it to rediscover as the physical core's grid.
        var targetMap = args.Coordinates.ToMap(EntityManager, _xforms);
        if (!TryGetPointingGrid(args.Coordinates, targetMap, out var gridUid, out var grid) ||
            !_broadphaseQuery.TryComp(gridUid, out var broadphase))
        {
            args.Cancelled = true;
            return;
        }

        var targetTile = Maps.LocalToTile(gridUid, grid, args.Coordinates);

        lock (Vision)
        {
            // [Changed by MisfitsCrew/Operator] Prefer the exact nearest camera/core that
            // can see the tile so the point arrow visually emerges from that source.
            if (Vision.TryGetNearestVisibleSource((gridUid, broadphase, grid), targetTile, targetMap, out var source))
            {
                args.Source = source;
                args.RotateSource = false;
                return;
            }

            // [Changed by MisfitsCrew/Operator] Keeps pointing usable if stricter source
            // attribution misses the exact seed while the tile is still AI-supervised.
            if (Vision.IsAccessible((gridUid, broadphase, grid), targetTile))
            {
                args.Source = core.Value.Owner;
                args.RotateSource = false;
                return;
            }
        }

        if (TryUseRemoteEyePointingSource(core.Value, args.Coordinates, ref args))
            return;

        args.Cancelled = true;
    }

    private bool TryUseRemoteEyePointingSource(
        Entity<StationAiCoreComponent> core,
        EntityCoordinates coordinates,
        ref GetPointingSourceEvent args)
    {
        // [Changed by MisfitsCrew/Operator] Lets inserted positronic-brain AIs point
        // from their relayed AI eye when camera supervision attribution misses the tile,
        // instead of falling back to the hidden brain/core position and reporting range.
        var remoteEntity = core.Comp.RemoteEntity;
        if (remoteEntity == null || !Exists(remoteEntity.Value))
            return false;

        if (!_examine.InRangeUnOccluded(
                remoteEntity.Value,
                coordinates,
                AiPointingRange,
                predicate: entity => entity == remoteEntity.Value))
        {
            return false;
        }

        args.Source = remoteEntity.Value;
        args.RotateSource = false;
        return true;
    }

    private bool TryResolvePointingCore(
        EntityUid pointer,
        [NotNullWhen(true)] out Entity<StationAiCoreComponent>? core)
    {
        // [Changed by MisfitsCrew/Operator] Resolves the AI core from the hidden AI brain,
        // the physical core, or the spawned AI eye so pointing works after setmind/relay use.
        core = null;

        if (TryGetContainingStationAiCore(pointer, out core))
            return true;

        if (TryComp(pointer, out StationAiHeldComponent? held) &&
            TryGetStationAiCoreForHeld((pointer, held), out core))
        {
            return true;
        }

        if (TryComp(pointer, out StationAiCoreComponent? coreComp))
        {
            core = (pointer, coreComp);
            return true;
        }

        var query = EntityQueryEnumerator<StationAiCoreComponent>();
        while (query.MoveNext(out var coreUid, out var candidateCore))
        {
            if (candidateCore.RemoteEntity == pointer)
            {
                core = (coreUid, candidateCore);
                return true;
            }

            var coreEnt = new Entity<StationAiCoreComponent>(coreUid, candidateCore);
            if (TryGetInsertedAI(coreEnt, out var insertedAi) &&
                insertedAi.Value.Owner == pointer)
            {
                core = coreEnt;
                return true;
            }
        }

        return false;
    }

    private bool TryGetPointingGrid(
        EntityCoordinates coordinates,
        MapCoordinates mapCoordinates,
        out EntityUid gridUid,
        [NotNullWhen(true)] out MapGridComponent? grid)
    {
        // [Changed by MisfitsCrew/Operator] Finds the grid for an AI point attempt from
        // the original click coordinates first, falling back to map lookup only when needed.
        gridUid = EntityUid.Invalid;
        grid = null;

        if (TryComp(coordinates.EntityId, out MapGridComponent? coordinateGrid))
        {
            gridUid = coordinates.EntityId;
            grid = coordinateGrid;
            return true;
        }

        if (_xforms.GetGrid(coordinates) is { } resolvedGrid &&
            TryComp(resolvedGrid, out grid))
        {
            gridUid = resolvedGrid;
            return true;
        }

        return _mapManager.TryFindGridAt(mapCoordinates, out gridUid, out grid);
    }

    protected override void OnStationAiInserted(Entity<StationAiCoreComponent> core, EntityUid ai)
    {
        // [Changed by MisfitsCrew/Operator] Adds camera PVS subscriptions after the AI
        // brain is inserted into a powered core.
        RefreshAiVisionSubscriptions(ai);
    }

    protected override void OnStationAiRemoved(Entity<StationAiCoreComponent> core, EntityUid ai)
    {
        // [Changed by MisfitsCrew/Operator] Removes camera PVS subscriptions when the AI
        // brain leaves the core.
        ClearAiVisionSubscriptions(ai);
    }

    protected override void OnStationAiCoreMapInitialized(Entity<StationAiCoreComponent> core, EntityUid? ai)
    {
        // [Changed by MisfitsCrew/Operator] Rebuilds camera PVS subscriptions when a mapped
        // AI core initializes with an AI already inserted.
        if (ai != null)
            RefreshAiVisionSubscriptions(ai.Value);
    }

    protected override void OnStationAiCoreShuttingDown(Entity<StationAiCoreComponent> core, EntityUid? ai)
    {
        // [Changed by MisfitsCrew/Operator] Clears camera PVS subscriptions before the core
        // and its remote eye are torn down.
        if (ai != null)
            ClearAiVisionSubscriptions(ai.Value);
    }

    protected override void OnStationAiCorePowerChanged(Entity<StationAiCoreComponent> core, bool powered, EntityUid? ai)
    {
        // [Changed by MisfitsCrew/Operator] Treats core power as the authority for whether
        // the AI session should receive camera-supervised PVS locations.
        if (ai == null)
            return;

        if (powered)
            RefreshAiVisionSubscriptions(ai.Value);
        else
            ClearAiVisionSubscriptions(ai.Value);
    }

    private void OnAiVisionStartup(Entity<StationAiVisionComponent> ent, ref ComponentStartup args)
    {
        // [Changed by MisfitsCrew/Operator] Adds newly initialized enabled vision sources
        // to any active same-grid AI sessions.
        if (ent.Comp.Enabled)
            AddVisionToRelevantAis(ent.Owner);
    }

    private void OnAiVisionShutdown(Entity<StationAiVisionComponent> ent, ref ComponentShutdown args)
    {
        // [Changed by MisfitsCrew/Operator] Removes deleted vision sources from any AI
        // sessions that were using them for camera-supervised PVS.
        RemoveVisionFromAllAis(ent.Owner);
    }

    private void RefreshAiVisionSubscriptions(EntityUid ai)
    {
        // [Changed by MisfitsCrew/Operator] Recomputes all enabled same-grid Station AI
        // vision sources for one AI session and subscribes the session to their PVS views.
        if (!TryComp(ai, out ActorComponent? actor) ||
            !TryComp(ai, out StationAiHeldComponent? held) ||
            !TryGetStationAiCoreForHeld((ai, held), out var core) ||
            !IsCorePowered(core.Value.Owner))
        {
            ClearAiVisionSubscriptions(ai);
            return;
        }

        var coreGrid = Transform(core.Value.Owner).GridUid;
        if (coreGrid == null)
        {
            ClearAiVisionSubscriptions(ai);
            return;
        }

        _desiredVisionSubscriptions.Clear();
        var visionQuery = EntityQueryEnumerator<StationAiVisionComponent, TransformComponent>();
        while (visionQuery.MoveNext(out var visionUid, out var vision, out var xform))
        {
            if (!vision.Enabled || xform.GridUid != coreGrid.Value)
                continue;

            _desiredVisionSubscriptions.Add(visionUid);
            AddVisionSubscription(ai, visionUid, actor);
        }

        if (!_visionSubscriptions.TryGetValue(ai, out var current))
            return;

        foreach (var visionUid in current.ToArray())
        {
            if (!_desiredVisionSubscriptions.Contains(visionUid))
                RemoveVisionSubscription(ai, visionUid, actor);
        }
    }

    /// <summary>
    /// [Changed by MisfitsCrew/Operator] Removes camera-network PVS while an AI mind controls a physical chassis.
    /// </summary>
    public void SuspendVisionSubscriptions(EntityUid ai)
    {
        ClearAiVisionSubscriptions(ai);
    }

    /// <summary>
    /// [Changed by MisfitsCrew/Operator] Rebuilds camera-network PVS after a shunted AI mind returns to its core.
    /// </summary>
    public void ResumeVisionSubscriptions(EntityUid ai)
    {
        RefreshAiVisionSubscriptions(ai);
    }

    private void AddVisionToRelevantAis(EntityUid vision)
    {
        // [Changed by MisfitsCrew/Operator] Adds one enabled vision source to all powered
        // same-grid AI sessions, used when a camera/core vision source appears or re-enables.
        var visionGrid = Transform(vision).GridUid;
        if (visionGrid == null)
            return;

        var query = EntityQueryEnumerator<StationAiCoreComponent, TransformComponent>();
        while (query.MoveNext(out var coreUid, out _, out var coreXform))
        {
            if (coreXform.GridUid != visionGrid.Value ||
                !IsCorePowered(coreUid) ||
                !TryComp(coreUid, out StationAiCoreComponent? core) ||
                !TryGetInsertedAI((coreUid, core), out var insertedAi) ||
                !TryComp(insertedAi.Value.Owner, out ActorComponent? actor))
            {
                continue;
            }

            AddVisionSubscription(insertedAi.Value.Owner, vision, actor);
        }
    }

    private void RemoveVisionFromAllAis(EntityUid vision)
    {
        // [Changed by MisfitsCrew/Operator] Removes one vision source from every tracked AI
        // session when that source is disabled or deleted.
        foreach (var (ai, _) in _visionSubscriptions.ToArray())
            RemoveVisionSubscription(ai, vision);
    }

    private void AddVisionSubscription(EntityUid ai, EntityUid vision, ActorComponent actor)
    {
        // [Changed by MisfitsCrew/Operator] Records and creates the Robust view subscription
        // that makes camera-supervised areas visible to the original AI player session.
        if (!_visionSubscriptions.TryGetValue(ai, out var current))
        {
            current = new HashSet<EntityUid>();
            _visionSubscriptions[ai] = current;
        }

        if (current.Contains(vision) || actor.PlayerSession.ViewSubscriptions.Contains(vision))
            return;

        _viewSubscriber.AddViewSubscriber(vision, actor.PlayerSession);
        current.Add(vision);
    }

    private void RemoveVisionSubscription(EntityUid ai, EntityUid vision, ActorComponent? actor = null)
    {
        // [Changed by MisfitsCrew/Operator] Tears down a tracked camera PVS subscription and
        // removes local bookkeeping once the AI should no longer see that source.
        if (!_visionSubscriptions.TryGetValue(ai, out var current) ||
            !current.Remove(vision))
        {
            return;
        }

        if (current.Count == 0)
            _visionSubscriptions.Remove(ai);

        if (!Resolve(ai, ref actor, false))
        {
            return;
        }

        _viewSubscriber.RemoveViewSubscriber(vision, actor.PlayerSession);
    }

    private void ClearAiVisionSubscriptions(EntityUid ai)
    {
        // [Changed by MisfitsCrew/Operator] Clears every tracked camera PVS subscription for
        // an AI session, used on removal, shutdown, or power loss.
        if (!_visionSubscriptions.TryGetValue(ai, out var current))
            return;

        foreach (var vision in current.ToArray())
            RemoveVisionSubscription(ai, vision);
    }

    private bool IsCorePowered(EntityUid core)
    {
        // [Changed by MisfitsCrew/Operator] Centralizes the powered-core check used by AI
        // pointing and camera PVS subscription decisions.
        SharedApcPowerReceiverComponent? receiver = null;
        return PowerReceiverSystem.IsPowered((core, receiver));
    }

    private bool TryGetStationAiCoreForHeld(
        Entity<StationAiHeldComponent> ai,
        [NotNullWhen(true)] out Entity<StationAiCoreComponent>? core)
    {
        // [Changed by MisfitsCrew/Operator] Finds the AI core for a held AI even when the
        // transform parent does not directly point at the core container owner.
        if (TryGetStationAiCore((ai.Owner, (StationAiHeldComponent?) ai.Comp), out core))
            return true;

        var query = EntityQueryEnumerator<StationAiCoreComponent>();
        while (query.MoveNext(out var coreUid, out var coreComp))
        {
            var coreEnt = new Entity<StationAiCoreComponent>(coreUid, coreComp);
            if (!TryGetInsertedAI(coreEnt, out var insertedAi) ||
                insertedAi.Value.Owner != ai.Owner)
            {
                continue;
            }

            core = coreEnt;
            return true;
        }

        core = null;
        return false;
    }

    public override bool SetWhitelistEnabled(Entity<StationAiWhitelistComponent> entity, bool enabled, bool announce = false)
    {
        if (!base.SetWhitelistEnabled(entity, enabled, announce))
            return false;

        if (announce)
        {
            AnnounceSnip(entity.Owner);
        }

        return true;
    }

    public override void AnnounceIntellicardUsage(EntityUid uid, SoundSpecifier? cue = null)
    {
        if (!TryComp<ActorComponent>(uid, out var actor))
            return;

        var msg = Loc.GetString("ai-consciousness-download-warning");
        var wrappedMessage = Loc.GetString("chat-manager-server-wrap-message", ("message", msg));
        _chats.ChatMessageToOne(ChatChannel.Server, msg, wrappedMessage, default, false, actor.PlayerSession.Channel, colorOverride: Color.Red);

        if (cue != null && _mind.TryGetMind(uid, out var mindId, out _))
            _roles.MindPlaySound(mindId, cue);
    }

    private void AnnounceSnip(EntityUid entity)
    {
        var xform = Transform(entity);

        if (!TryComp(xform.GridUid, out MapGridComponent? grid))
            return;

        _ais.Clear();
        _lookup.GetChildEntities(xform.GridUid.Value, _ais);
        var filter = Filter.Empty();

        foreach (var ai in _ais)
        {
            // TODO: Filter API?
            if (TryComp(ai.Owner, out ActorComponent? actorComp))
            {
                filter.AddPlayer(actorComp.PlayerSession);
            }
        }

        // TEST
        // filter = Filter.Broadcast();

        // No easy way to do chat notif embeds atm.
        var tile = Maps.LocalToTile(xform.GridUid.Value, grid, xform.Coordinates);
        var msg = Loc.GetString("ai-wire-snipped", ("coords", tile));

        _chats.ChatMessageToMany(ChatChannel.Notifications, msg, msg, entity, false, true, filter.Recipients.Select(o => o.Channel));
        // Apparently there's no sound for this.
    }
}
