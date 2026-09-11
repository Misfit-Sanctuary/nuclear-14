// #Misfits Change - Wasteland Map server system
using System;
using System.Collections.Generic;
using Content.Server.Access.Components;
using Content.Server.Chat.Managers; // #Misfits Add - faction death alert chat dispatch
using Content.Server._Misfits.Group; // #Misfits Add - group blip injection
using Content.Server._Misfits.Overwatch;
using Content.Server._Misfits.TribalHunt;
using Content.Server._Misfits.TreeOfLife;
using Content.Server.Radio.Components;
using Content.Shared.Access.Components;
using Content.Shared.Humanoid; // #Misfits Add - Followers casualty filter for humanoid player bodies only
using Content.Shared.Mind; // #Misfits Add - MindComponent (OriginalOwnerUserId player check)
using Content.Shared.Mind.Components; // #Misfits Add - MindContainerComponent
using Content.Shared.Mobs; // #Misfits Add - MobState, MobStateChangedEvent
using Content.Shared.Mobs.Components; // #Misfits Add - MobStateComponent
using Content.Shared.Mobs.Systems; // #Misfits Add - MobStateSystem
using Content.Shared.Tag;
using Content.Shared._Misfits.WastelandMap;
using Content.Shared._Misfits.MaterialExtractor;
using Content.Shared._Misfits.Expeditions;
using Content.Shared._Misfits.TreeOfLife;
using Content.Shared._Misfits.Deathclaw;
using Content.Shared._Misfits.TribalHunt;
using Content.Shared.NPC.Components; // NpcFactionMemberComponent
using Content.Shared.NPC.Systems;
using Content.Shared.Radio;
using Content.Shared.Radio.Components;
using Content.Shared.Radio.EntitySystems;
using Content.Shared.Roles.Jobs; // #Misfits Add - leadership job lookup for Tree TacMap access
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components; // #Misfits Add - MapGridComponent for auto-bounds
using Robust.Shared.Player; // #Misfits Add - ActorComponent for faction filter iteration
using Robust.Shared.Utility; // #Misfits Add - ResPath for auto-detect
// #Misfits Add - MapId→gameMap tracking for auto-detect
using Content.Server.GameTicking;
using Content.Server.Maps;
using Robust.Shared.Prototypes;

namespace Content.Server._Misfits.WastelandMap;

/// <summary>
/// Sends the WastelandMap state (including world bounds) to the client BUI
/// when the UI is opened. Box2 is not NetSerializable, so we unpack it into
/// 4 floats inside the BUI state.
/// </summary>
public sealed class WastelandMapSystem : EntitySystem
{
    private const string WastelandGlobalChannel = "WastelandGlobal";

    private static readonly HashSet<string> EmptyCommunicationsJobs = [];

    private static readonly HashSet<string> BrotherhoodCommunicationsJobs =
    [
        "BoSWestElderCommander",
        "BoSMidPaladinCommander",
        "BoSHonorGuard", // #Cythisiax Added - Honor Guard has Head-Paladin-level BoS comms
        "BoSHeadKnight",
        "BoSWestScribe",
    ];

    private static readonly HashSet<string> NcrCommunicationsJobs =
    [
        "NCRCommander",
        "NCRExecutiveOfficer",
        "NCRPlatoonLeader",
        "NCRRO",
        "NCRProvost",
        "NCRMilitaryPoliceCaptain",
    ];

    private static readonly HashSet<string> EnclaveCommunicationsJobs =
    [
        "EnclaveCommander",
        "EnclaveSeniorOfficer",
        "EnclaveJuniorOfficer",
        "EnclaveHeadScientist",
    ];

    private static readonly HashSet<string> LegionCommunicationsJobs =
    [
        "CaesarLegionLegate",
        "CaesarLegionCenturion",
        "CaesarLegionOptio",
    ];

    private static readonly HashSet<string> FollowersCommunicationsJobs =
    [
        "FollowerDoctor",
        "SuperMutantFollowerDoctor",
    ];

    private static readonly HashSet<string> VaultCommunicationsJobs =
    [
        "Overseer",
    ];

    [Dependency] private readonly UserInterfaceSystem _uiSystem = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly TagSystem _tag = default!;
    [Dependency] private readonly SharedMindSystem _mind = default!; // #Misfits Add - Tree map leadership lookup
    [Dependency] private readonly SharedJobSystem _jobs = default!; // #Misfits Add - Tree map leadership lookup
    [Dependency] private readonly GroupSystem _groupSystem = default!; // #Misfits Add - group member map blips
    [Dependency] private readonly NpcFactionSystem _npcFaction = default!;
    [Dependency] private readonly EncryptionKeySystem _encryptionKeys = default!;
    // #Misfits Add - Followers dead body tracking & death alerts
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly IChatManager _chatManager = default!;
    // #Misfits Add - Auto-detect map bounds and texture
    [Dependency] private readonly SharedMapSystem _mapManager = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly SharedMapSystem _mapSystem = default!;

    private const int MaxSharedAnnotations = 128;
    private const int MaxStrokePoints = 512; // 256 UV points × 2 floats each
    // #Misfits Fix: Slowed from 0.5 s — the map is informational, not real-time.
    // GetIdCardBlips does a global PresetIdCard world-scan every update; 2.5 s is imperceptible to players.
    private const float UpdateInterval = 2.5f;
    private float _updateAccumulator;
    private readonly Dictionary<(MapId MapId, WastelandMapTacticalFeedKind Feed), List<WastelandMapAnnotation>> _sharedFeedAnnotations = new();

    // #Misfits Add - Scratch buffer for Followers death-alert session dispatch.
    private readonly List<ICommonSession> _followerSessionScratch = new();

    // #Misfits Add - Track which GameMapPrototype ID was used for each loaded MapId.
    // Populated by PostGameMapLoad; used by ResolveMapConfig to look up
    // WastelandMapConfig prototypes for auto-detecting map texture and bounds.
    private readonly Dictionary<MapId, string> _gameMapByMapId = new();

    // #Misfits Add - Scratch buffers + tick-local cache for BuildState.
    // At 150 pop with many open wasteland maps, the 2.5s sweep was the single hottest user-
    // facing UI allocator. These buffers are reused per Update sweep; the _nonActorCache
    // holds faction/tribal blips keyed by (mapId, feed) so multiple map entities with the
    // same feed only pay for one world-scan per sweep.
    private readonly List<WastelandMapTrackedBlip> _blipScratch = new();
    private readonly List<WastelandMapTrackedBlip> _groupScratch = new();
    private readonly Dictionary<(MapId MapId, WastelandMapTacticalFeedKind Feed), WastelandMapTrackedBlip[]> _nonActorCache = new();
    private bool _inUpdateSweep;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WastelandMapComponent, AfterActivatableUIOpenEvent>(OnAfterOpen);
        SubscribeLocalEvent<WastelandMapComponent, ActivatableUIOpenAttemptEvent>(OnOpenAttempt); // #Misfits Add - optional Tree map job gate
        SubscribeLocalEvent<WastelandMapComponent, WastelandMapAddAnnotationMessage>(OnAddAnnotationMessage);
        SubscribeLocalEvent<WastelandMapComponent, WastelandMapRemoveAnnotationMessage>(OnRemoveAnnotationMessage);
        SubscribeLocalEvent<WastelandMapComponent, WastelandMapClearAnnotationsMessage>(OnClearAnnotationsMessage);
        SubscribeLocalEvent<WastelandMapComponent, WastelandMapCommunicationsMessage>(OnCommunicationsMessage);
        SubscribeLocalEvent<BwonsamdiComponent, OpenUiActionEvent>(OnBwonsamdiSoulCompassOpen);
        // #Misfits Add - notify Followers players when a player humanoid dies
        SubscribeLocalEvent<MindContainerComponent, MobStateChangedEvent>(OnMindedEntityMobStateChanged);
        // #Misfits Add - track MapId→gameMap for auto-detect bounds/texture
        SubscribeLocalEvent<PostGameMapLoad>(OnPostGameMapLoad);
    }

    // #Misfits Add - Track MapId→gameMap prototype ID so BuildState can auto-resolve
    // the correct WastelandMapConfig (texture path + world bounds) for each game map.
    private void OnPostGameMapLoad(PostGameMapLoad ev)
    {
        _gameMapByMapId[ev.Map] = ev.GameMap.ID;
    }

    // #Misfits Add - Resolve map texture path and world bounds for auto-detect.
    // Priority:
    //   1. If component has explicit MapTexturePath/WorldBounds (not default), use them.
    //   2. If component has MapConfigId set, look up that WastelandMapConfig prototype.
    //   3. If we know the MapId's game map (from PostGameMapLoad), look up config by that ID.
    //   4. Fall back to computing world bounds from all grids on the given MapId.
    private (ResPath TexturePath, Box2 Bounds) ResolveMapConfig(
        WastelandMapComponent comp, MapId mapId)
    {
        // Priority 1: component has explicit values
        if (comp.MapTexturePath != null && comp.WorldBounds != default)
            return (comp.MapTexturePath.Value, comp.WorldBounds);

        // Priority 2: explicit MapConfigId on the component
        WastelandMapConfigPrototype? config = null;
        if (comp.MapConfigId != null)
            _prototypeManager.TryIndex(comp.MapConfigId, out config);

        // Priority 3: look up by game map ID from PostGameMapLoad tracking
        if (config == null && _gameMapByMapId.TryGetValue(mapId, out var gameMapId))
            _prototypeManager.TryIndex(gameMapId, out config);

        if (config != null)
        {
            var texPath = comp.MapTexturePath ?? config.MapTexturePath;
            var bounds = comp.WorldBounds != default ? comp.WorldBounds : config.WorldBounds;
            // If still default after config, compute from grids
            if (bounds == default)
                bounds = ComputeMapBounds(mapId);
            return (texPath, bounds);
        }

        // Priority 4: compute from grids
        var fallbackTex = comp.MapTexturePath ?? new ResPath("_Misfits/Maps/wendover_map.png");
        var fallbackBounds = comp.WorldBounds != default ? comp.WorldBounds : ComputeMapBounds(mapId);
        return (fallbackTex, fallbackBounds);
    }

    // #Misfits Add - Compute the combined world AABB of all grids on a MapId.
    private Box2 ComputeMapBounds(MapId mapId)
    {
        var bounds = new Box2(float.MaxValue, float.MaxValue, float.MinValue, float.MinValue);
        var any = false;

        foreach (var grid in _mapManager.GetAllGrids(mapId))
        {
            var gridBounds = grid.Comp.LocalAABB;
            if (gridBounds.IsEmpty())
                continue;
            any = true;
            // #Misfits Fix - Map-grid combos may have empty LocalAABB but non-empty tile data.
            // Compute from tiles in that case (same pattern as MapPainter).
            if (gridBounds.IsEmpty() && grid.Comp.ChunkCount > 0)
            {
                int minX = int.MaxValue, minY = int.MaxValue;
                int maxX = int.MinValue, maxY = int.MinValue;
                var enumerator = _mapSystem.GetAllTilesEnumerator(grid.Owner, grid.Comp);
                while (enumerator.MoveNext(out var tileRef))
                {
                    if (tileRef.Value.X < minX) minX = tileRef.Value.X;
                    if (tileRef.Value.X > maxX) maxX = tileRef.Value.X;
                    if (tileRef.Value.Y < minY) minY = tileRef.Value.Y;
                    if (tileRef.Value.Y > maxY) maxY = tileRef.Value.Y;
                }
                if (minX <= maxX)
                {
                    gridBounds = new Box2(minX, minY, maxX + 1, maxY + 1);
                    any = true;
                }
            }
            bounds = bounds.Union(gridBounds);
        }

        return any ? bounds : new Box2(-517, -308, 484, 311); // fallback to Wendover bounds
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _updateAccumulator += frameTime;
        if (_updateAccumulator < UpdateInterval)
            return;

        _updateAccumulator = 0f;

        // #Misfits Add - Open a sweep window so BuildState can cache the non-actor blip
        // portion per (mapId, feed) across multiple map entities on this tick.
        _nonActorCache.Clear();
        _inUpdateSweep = true;
        try
        {
            var query = EntityQueryEnumerator<WastelandMapComponent, UserInterfaceComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out var map, out var ui, out var xform))
            {
                // #Misfits Fix: Skip the expensive BUI rebuild when nobody has this map open.
                // GetActors() is O(1) with the early-out; the rebuild + GetIdCardBlips world-scan is O(all id cards).
                var viewerMap = xform.MapID;
                EntityUid? firstActor = null;
                foreach (var actor in _uiSystem.GetActors((uid, ui), WastelandMapUiKey.Key))
                {
                    viewerMap = Transform(actor).MapID;
                    firstActor = actor; // #Misfits Add - pass actor so group blips are relative to who holds the map
                    break;
                }
                if (firstActor == null)
                    continue;

                _uiSystem.SetUiState((uid, ui), WastelandMapUiKey.Key, BuildState(uid, map, viewerMap, actor: firstActor));
            }
        }
        finally
        {
            _inUpdateSweep = false;
            _nonActorCache.Clear();
        }
    }

    private void OnAfterOpen(EntityUid uid, WastelandMapComponent comp, AfterActivatableUIOpenEvent args)
    {
        var userMap = Transform(args.User).MapID;
        // #Misfits Add - pass the user so group member blips are seeded correctly on open
        _uiSystem.SetUiState(uid, WastelandMapUiKey.Key, BuildState(uid, comp, userMap, actor: args.User));
    }

    // An action opens the BUI directly, bypassing ActivatableUI's AfterOpen event.
    // Seed the state immediately so the Soul Compass never opens as a blank map.
    private void OnBwonsamdiSoulCompassOpen(Entity<BwonsamdiComponent> ent, ref OpenUiActionEvent args)
    {
        if (args.Key is not WastelandMapUiKey.Key ||
            !TryComp<WastelandMapComponent>(ent, out var map) ||
            !TryComp<UserInterfaceComponent>(ent, out var ui))
        {
            return;
        }

        _uiSystem.SetUiState((ent.Owner, ui), WastelandMapUiKey.Key,
            BuildState(ent.Owner, map, Transform(args.Performer).MapID, actor: args.Performer));
    }

    // #Misfits Add - preserve unrestricted maps unless they define a leadership allowlist.
    private void OnOpenAttempt(Entity<WastelandMapComponent> ent, ref ActivatableUIOpenAttemptEvent args)
    {
        if (args.Cancelled || CanOpenMap(args.User, ent.Comp))
            return;

        args.Cancel();
    }

    internal bool CanOpenMap(EntityUid user, WastelandMapComponent component)
    {
        if (component.ActivatorJobs is not { Count: > 0 })
            return true;

        return _mind.TryGetMind(user, out var mindId, out _)
            && _jobs.MindTryGetJob(mindId, out _, out var job)
            && component.ActivatorJobs.Contains(job.ID);
    }

    private void OnAddAnnotationMessage(EntityUid uid, WastelandMapComponent comp, WastelandMapAddAnnotationMessage args)
    {
        if (!TryAddAnnotation(args.Actor, comp, Transform(args.Actor).MapID, args.Annotation))
            return;

        UpdateMapUi(uid, comp, Transform(args.Actor).MapID);
    }

    private void OnRemoveAnnotationMessage(EntityUid uid, WastelandMapComponent comp, WastelandMapRemoveAnnotationMessage args)
    {
        if (!TryRemoveAnnotation(args.Actor, comp, Transform(args.Actor).MapID, args.Index))
            return;

        UpdateMapUi(uid, comp, Transform(args.Actor).MapID);
    }

    private void OnClearAnnotationsMessage(EntityUid uid, WastelandMapComponent comp, WastelandMapClearAnnotationsMessage args)
    {
        if (!TryClearAnnotations(args.Actor, comp, Transform(args.Actor).MapID))
            return;

        UpdateMapUi(uid, comp, Transform(args.Actor).MapID);
    }

    private void OnCommunicationsMessage(EntityUid uid, WastelandMapComponent comp, WastelandMapCommunicationsMessage args)
    {
        if (!TryResolveCommunications(comp, out var factionId, out var factionChannelId) ||
            !CanManageCommunications(args.Actor, comp))
        {
            return;
        }

        var channelId = args.ChannelKind == WastelandMapCommunicationsChannelKind.Wasteland
            ? WastelandGlobalChannel
            : factionChannelId;

        var target = GetEntity(args.Target);
        if (Deleted(target) ||
            !HasComp<ActorComponent>(target) ||
            !_npcFaction.IsMember(target, factionId))
        {
            return;
        }

        if (!TrySetFactionEncryptionRevoked(target, channelId, args.Revoke))
            return;

        UpdateMapUi(uid, comp, Transform(args.Actor).MapID);
    }

    // #Misfits Add - optional actor param so group-member blips can be injected per-viewer
    public WastelandMapBoundUserInterfaceState BuildState(WastelandMapComponent comp, MapId mapId, WastelandMapTacticalFeedKind? feedOverride = null, EntityUid? actor = null)
    {
        return BuildState(null, comp, mapId, feedOverride, actor);
    }

    // #Misfits Add - optional uid lets fixed TacMap entities expose Overwatch without leaking it to cartridges/HUDs.
    public WastelandMapBoundUserInterfaceState BuildState(EntityUid? uid, WastelandMapComponent comp, MapId mapId, WastelandMapTacticalFeedKind? feedOverride = null, EntityUid? actor = null)
    {
        // #Misfits Add - auto-detect map texture and bounds if not hardcoded
        var (texPath, bounds) = ResolveMapConfig(comp, mapId);

        var feed = feedOverride ?? GetEffectiveFeed(comp);
        var trackedBlips = GetTrackedBlips(feed, mapId, bounds, actor);
        var sharedAnnotations = GetSharedAnnotations(comp, mapId, feed).ToArray();
        var overwatch = uid == null
            ? null
            : EntityManager.System<OverwatchConsoleSystem>().BuildUiState(uid.Value);
        var communications = uid == null || actor == null
            ? null
            : BuildCommunicationsState(uid.Value, comp, actor.Value);

        return new WastelandMapBoundUserInterfaceState(
            comp.MapTitle,
            texPath.ToString(),
            comp.CompactHud,
            bounds.Left,
            bounds.Bottom,
            bounds.Right,
            bounds.Top,
            trackedBlips,
            sharedAnnotations,
            overwatch,
            communications);
    }

    public WastelandMapTacticalFeedKind GetEffectiveFeed(WastelandMapComponent comp)
    {
        if (comp.TacticalFeed != WastelandMapTacticalFeedKind.None)
            return comp.TacticalFeed;

        return comp.TrackBrotherhoodHolotags
            ? WastelandMapTacticalFeedKind.Brotherhood
            : WastelandMapTacticalFeedKind.None;
    }

    public bool TryAddAnnotation(EntityUid actor, WastelandMapComponent comp, MapId mapId, WastelandMapAnnotation annotation, WastelandMapTacticalFeedKind? feedOverride = null)
    {
        var sanitized = SanitizeAnnotation(annotation);
        if (sanitized == null)
            return false;

        var annotations = GetSharedAnnotations(comp, mapId, feedOverride ?? GetEffectiveFeed(comp));
        annotations.Add(sanitized.Value);
        if (annotations.Count > MaxSharedAnnotations)
            annotations.RemoveAt(0);

        return true;
    }

    public bool TryRemoveAnnotation(EntityUid actor, WastelandMapComponent comp, MapId mapId, int index, WastelandMapTacticalFeedKind? feedOverride = null)
    {
        var annotations = GetSharedAnnotations(comp, mapId, feedOverride ?? GetEffectiveFeed(comp));
        if (index < 0 || index >= annotations.Count)
            return false;

        annotations.RemoveAt(index);
        return true;
    }

    public bool TryClearAnnotations(EntityUid actor, WastelandMapComponent comp, MapId mapId, WastelandMapTacticalFeedKind? feedOverride = null)
    {
        var annotations = GetSharedAnnotations(comp, mapId, feedOverride ?? GetEffectiveFeed(comp));
        if (annotations.Count == 0)
            return false;

        annotations.Clear();
        return true;
    }

    private WastelandMapCommunicationsState? BuildCommunicationsState(EntityUid uid, WastelandMapComponent comp, EntityUid actor)
    {
        if (!IsCommunicationsPanelAvailable(uid, comp) ||
            !TryResolveCommunications(comp, out var factionId, out var channelId))
        {
            return null;
        }

        var canManage = CanManageCommunications(actor, comp);
        var entries = new List<WastelandMapCommunicationsEntry>();

        if (canManage)
        {
            var query = EntityQueryEnumerator<NpcFactionMemberComponent, ActorComponent>();

            while (query.MoveNext(out var player, out _, out _))
            {
                if (!_npcFaction.IsMember(player, factionId))
                    continue;

                var (hasFactionHeadset, factionRevoked) = GetChannelHeadsetState(player, channelId);
                var (hasWastelandHeadset, wastelandRevoked) = GetChannelHeadsetState(player, WastelandGlobalChannel);
                entries.Add(new WastelandMapCommunicationsEntry(
                    GetNetEntity(player),
                    Name(player),
                    TryGetJobTitle(player),
                    hasFactionHeadset,
                    factionRevoked,
                    hasWastelandHeadset,
                    wastelandRevoked));
            }

            entries.Sort((left, right) => string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase));
        }

        return new WastelandMapCommunicationsState(
            channelId,
            GetChannelName(channelId),
            WastelandGlobalChannel,
            GetChannelName(WastelandGlobalChannel),
            canManage,
            entries.ToArray());
    }

    private bool CanManageCommunications(EntityUid actor, WastelandMapComponent component)
    {
        var communicationsJobs = GetCommunicationsJobs(component);
        if (communicationsJobs.Count == 0)
            return false;

        return _mind.TryGetMind(actor, out var mindId, out _) &&
               _jobs.MindTryGetJob(mindId, out _, out var job) &&
               communicationsJobs.Contains(job.ID);
    }

    private bool IsCommunicationsPanelAvailable(EntityUid uid, WastelandMapComponent component)
    {
        if (component.CommunicationsJobs is { Count: > 0 })
            return true;

        return HasComp<OverwatchConsoleComponent>(uid) && GetDefaultCommunicationsJobs(GetEffectiveFeed(component)).Count > 0;
    }

    private IReadOnlySet<string> GetCommunicationsJobs(WastelandMapComponent component)
    {
        return component.CommunicationsJobs is { Count: > 0 }
            ? component.CommunicationsJobs
            : GetDefaultCommunicationsJobs(GetEffectiveFeed(component));
    }

    private static IReadOnlySet<string> GetDefaultCommunicationsJobs(WastelandMapTacticalFeedKind feed)
    {
        return feed switch
        {
            WastelandMapTacticalFeedKind.Brotherhood => BrotherhoodCommunicationsJobs,
            WastelandMapTacticalFeedKind.NCR => NcrCommunicationsJobs,
            WastelandMapTacticalFeedKind.Enclave => EnclaveCommunicationsJobs,
            WastelandMapTacticalFeedKind.Legion => LegionCommunicationsJobs,
            WastelandMapTacticalFeedKind.Followers => FollowersCommunicationsJobs,
            WastelandMapTacticalFeedKind.Vault => VaultCommunicationsJobs,
            _ => EmptyCommunicationsJobs,
        };
    }

    private string? TryGetJobTitle(EntityUid player)
    {
        if (!_mind.TryGetMind(player, out var mindId, out _) ||
            !_jobs.MindTryGetJob(mindId, out _, out var job))
        {
            return null;
        }

        return job.LocalizedName;
    }

    private string GetChannelName(string channelId)
    {
        return _prototypeManager.TryIndex<RadioChannelPrototype>(channelId, out var channel)
            ? channel.LocalizedName
            : channelId;
    }

    private (bool HasHeadset, bool Revoked) GetChannelHeadsetState(EntityUid player, string channelId)
    {
        if (!TryComp<WearingHeadsetComponent>(player, out var wearing) ||
            !TryComp<EncryptionKeyHolderComponent>(wearing.Headset, out var holder))
        {
            return (false, false);
        }

        var hasChannel = holder.Channels.Contains(channelId);
        var revoked = TryComp<DisabledEncryptionChannelsComponent>(wearing.Headset, out var disabledHolder) &&
                      disabledHolder.Channels.Contains(channelId);

        foreach (var key in holder.KeyContainer.ContainedEntities)
        {
            if (!TryComp<EncryptionKeyComponent>(key, out var keyComp) ||
                !keyComp.Channels.Contains(channelId))
            {
                continue;
            }

            hasChannel = true;
            revoked |= TryComp<DisabledEncryptionChannelsComponent>(key, out var disabledKey) &&
                       disabledKey.Channels.Contains(channelId);
        }

        return (hasChannel || revoked, revoked);
    }

    private bool TrySetFactionEncryptionRevoked(EntityUid player, string channelId, bool revoked)
    {
        if (!TryComp<WearingHeadsetComponent>(player, out var wearing) ||
            !TryComp<EncryptionKeyHolderComponent>(wearing.Headset, out var holder))
        {
            return false;
        }

        var changed = SetChannelDisabled(wearing.Headset, channelId, revoked);

        foreach (var key in holder.KeyContainer.ContainedEntities)
        {
            if (!TryComp<EncryptionKeyComponent>(key, out var keyComp) ||
                !keyComp.Channels.Contains(channelId))
            {
                continue;
            }

            changed |= SetChannelDisabled(key, channelId, revoked);
        }

        if (!changed)
            return false;

        _encryptionKeys.UpdateChannels(wearing.Headset, holder);
        return true;
    }

    private bool SetChannelDisabled(EntityUid uid, string channelId, bool disabled)
    {
        if (disabled)
        {
            var comp = EnsureComp<DisabledEncryptionChannelsComponent>(uid);
            if (!comp.Channels.Add(channelId))
                return false;

            Dirty(uid, comp);
            return true;
        }

        if (!TryComp<DisabledEncryptionChannelsComponent>(uid, out var existing) ||
            !existing.Channels.Remove(channelId))
        {
            return false;
        }

        if (existing.Channels.Count == 0)
            RemComp<DisabledEncryptionChannelsComponent>(uid);
        else
            Dirty(uid, existing);

        return true;
    }

    private bool TryResolveCommunications(WastelandMapComponent comp, out string factionId, out string channelId)
    {
        switch (GetEffectiveFeed(comp))
        {
            case WastelandMapTacticalFeedKind.Brotherhood:
                factionId = "BrotherhoodOfSteel";
                channelId = "BrotherhoodOfSteel";
                return true;
            case WastelandMapTacticalFeedKind.NCR:
                factionId = "NCR";
                channelId = "NCR";
                return true;
            case WastelandMapTacticalFeedKind.Enclave:
                factionId = "Enclave";
                channelId = "Enclave";
                return true;
            case WastelandMapTacticalFeedKind.Legion:
                factionId = "CaesarLegion";
                channelId = "Legion";
                return true;
            case WastelandMapTacticalFeedKind.Followers:
                factionId = "Followers";
                channelId = "FollowersOfApocalypse";
                return true;
            case WastelandMapTacticalFeedKind.Vault:
                factionId = "Vault";
                channelId = "VaultCommon";
                return true;
            default:
                factionId = string.Empty;
                channelId = string.Empty;
                return false;
        }
    }

    private void UpdateMapUi(EntityUid uid, WastelandMapComponent comp, MapId? mapId = null)
    {
        if (!TryComp<UserInterfaceComponent>(uid, out var ui))
            return;

        _uiSystem.SetUiState((uid, ui), WastelandMapUiKey.Key, BuildState(uid, comp, mapId ?? Transform(uid).MapID));
    }

    public void RefreshUi(EntityUid uid, EntityUid actor)
    {
        if (!TryComp<WastelandMapComponent>(uid, out var comp) ||
            !TryComp<UserInterfaceComponent>(uid, out var ui))
        {
            return;
        }

        _uiSystem.SetUiState((uid, ui), WastelandMapUiKey.Key, BuildState(uid, comp, Transform(actor).MapID, actor: actor));
    }

    private static WastelandMapAnnotation? SanitizeAnnotation(WastelandMapAnnotation annotation)
    {
        if (annotation.Type is not (WastelandMapAnnotationType.Marker
            or WastelandMapAnnotationType.Box
            or WastelandMapAnnotationType.Draw))
            return null;

        var label = annotation.Label.Trim();
        if (label.Length > 64)
            label = label[..64].TrimEnd();

        // Draw type: sanitize stroke points
        if (annotation.Type == WastelandMapAnnotationType.Draw)
        {
            var pts = annotation.StrokePoints;
            if (pts == null || pts.Length < 4)
                return null;
            var count = Math.Min(pts.Length & ~1, MaxStrokePoints); // ensure even, cap to max
            var sanitizedPts = new float[count];
            for (var i = 0; i < count; i++)
                sanitizedPts[i] = Math.Clamp(pts[i], 0f, 1f);
            if (string.IsNullOrWhiteSpace(label))
                label = "Drawing";
            return new WastelandMapAnnotation(WastelandMapAnnotationType.Draw, 0f, 0f, 0f, 0f, label, annotation.PackedColor, Math.Clamp(annotation.StrokeWidth, 1f, 12f), sanitizedPts);
        }

        // Marker / Box
        var startX = Math.Clamp(annotation.StartX, 0f, 1f);
        var startY = Math.Clamp(annotation.StartY, 0f, 1f);
        var endX = Math.Clamp(annotation.EndX, 0f, 1f);
        var endY = Math.Clamp(annotation.EndY, 0f, 1f);

        if (string.IsNullOrWhiteSpace(label))
            label = annotation.Type == WastelandMapAnnotationType.Marker ? "Marker" : "Box";

        return new WastelandMapAnnotation(annotation.Type, startX, startY, endX, endY, label, annotation.PackedColor, Math.Clamp(annotation.StrokeWidth, 1f, 12f), null);
    }

    private List<WastelandMapAnnotation> GetSharedAnnotations(WastelandMapComponent comp, MapId mapId, WastelandMapTacticalFeedKind feed)
    {
        if (feed == WastelandMapTacticalFeedKind.None)
            return comp.SharedAnnotations;

        var key = (mapId, feed);
        if (_sharedFeedAnnotations.TryGetValue(key, out var annotations))
            return annotations;

        annotations = new List<WastelandMapAnnotation>(comp.SharedAnnotations);
        _sharedFeedAnnotations[key] = annotations;
        return annotations;
    }

    // #Misfits Add - actor param enables group-member blip injection
    private WastelandMapTrackedBlip[] GetTrackedBlips(WastelandMapTacticalFeedKind feed, MapId mapId, Box2 bounds, EntityUid? actor = null)
    {
        // #Misfits Tweak - Cache the non-actor (faction + tribal) portion per (mapId, feed)
        // for the lifetime of a single Update sweep, so multiple open maps sharing a feed
        // pay for one world-scan instead of N. Outside the sweep this falls back to a
        // direct rebuild (e.g. OnAfterOpen, annotation messages).
        WastelandMapTrackedBlip[] nonActorBlips;
        var cacheKey = (mapId, feed);
        if (_inUpdateSweep && _nonActorCache.TryGetValue(cacheKey, out var cached))
        {
            nonActorBlips = cached;
        }
        else
        {
            _blipScratch.Clear();
            AppendFactionBlips(_blipScratch, feed, mapId, bounds);
            AppendMaterialExtractorBlips(_blipScratch, mapId, bounds);
            AppendExpeditionEntranceBlips(_blipScratch, mapId, bounds);
            if (AllowsSharedOverlays(feed)) // #Misfits Change - Tribe maps are tagged-ID-only.
                AppendTribalHuntTargetBlips(_blipScratch, mapId, bounds);
            nonActorBlips = _blipScratch.ToArray();
            if (_inUpdateSweep)
                _nonActorCache[cacheKey] = nonActorBlips;
        }

        // Group blips are per-actor and therefore never cached across viewers.
        if (actor.HasValue && AllowsSharedOverlays(feed)) // #Misfits Change - Tribe maps exclude viewer-specific group overlays.
        {
            _groupScratch.Clear();
            AppendGroupMemberBlips(_groupScratch, actor.Value, mapId, bounds);
            if (_groupScratch.Count == 0)
                return nonActorBlips;

            var combined = new WastelandMapTrackedBlip[nonActorBlips.Length + _groupScratch.Count];
            nonActorBlips.CopyTo(combined, 0);
            for (var i = 0; i < _groupScratch.Count; i++)
                combined[nonActorBlips.Length + i] = _groupScratch[i];
            return combined;
        }

        return nonActorBlips;
    }

    // #Misfits Add - keep the Tribe feed limited to its explicitly tagged identification items.
    internal bool AllowsSharedOverlays(WastelandMapTacticalFeedKind feed)
    {
        return feed is not (WastelandMapTacticalFeedKind.Tribe or WastelandMapTacticalFeedKind.Bwonsamdi);
    }

    // #Misfits Add - Append the faction blip set for this feed into the supplied buffer.
    private void AppendFactionBlips(List<WastelandMapTrackedBlip> buffer, WastelandMapTacticalFeedKind feed, MapId mapId, Box2 bounds)
    {
        switch (feed)
        {
            case WastelandMapTacticalFeedKind.Brotherhood:
                AppendIdCardBlips(buffer, mapId, bounds, "IdCardBrotherhood");
                break;
            case WastelandMapTacticalFeedKind.Vault:
                AppendIdCardBlips(buffer, mapId, bounds, "IdCardVault");
                break;
            case WastelandMapTacticalFeedKind.NCR:
                AppendIdCardBlips(buffer, mapId, bounds, "IdCardNCR");
                break;
            case WastelandMapTacticalFeedKind.Enclave:
                AppendIdCardBlips(buffer, mapId, bounds, "IdCardEnclave");
                break;
            case WastelandMapTacticalFeedKind.Legion:
                AppendIdCardBlips(buffer, mapId, bounds, "IdCardLegion");
                break;
            case WastelandMapTacticalFeedKind.Tribe:
                AppendIdCardBlips(buffer, mapId, bounds, "IdCardTribe"); // #Misfits Add - Willower pendant feed
                AppendTribeCriticalBlips(buffer, mapId, bounds);
                break;
            // #Misfits Add - Followers feed shows dead player humanoids
            case WastelandMapTacticalFeedKind.Followers:
                AppendDeadBodyBlips(buffer, mapId, bounds);
                break;
            case WastelandMapTacticalFeedKind.Bwonsamdi:
                AppendBwonsamdiSoulBlips(buffer, mapId, bounds);
                break;
        }
    }

    /// <summary>Appends a blip for each group member on the same map as the actor, excluding the actor themselves.</summary>
    private void AppendGroupMemberBlips(List<WastelandMapTrackedBlip> buffer, EntityUid actor, MapId mapId, Box2 bounds)
    {
        var members = _groupSystem.GetGroupMemberEntities(actor);
        if (members == null || members.Count == 0)
            return;

        foreach (var member in members)
        {
            if (member == actor)
                continue; // don't show the holder as a blip

            var mapCoords = _transform.GetMapCoordinates(member);
            if (mapCoords.MapId != mapId)
                continue;

            var pos = mapCoords.Position;
            if (!bounds.Contains(pos))
                continue;

            var label = Name(member);
            buffer.Add(new WastelandMapTrackedBlip(pos.X, pos.Y, label, WastelandMapTrackedBlipKind.PipBoyGroupMember));
        }

        var rallyPoint = _groupSystem.GetGroupRallyPoint(actor);
        if (rallyPoint.HasValue &&
            rallyPoint.Value.MapId == mapId &&
            bounds.Contains(rallyPoint.Value.Position))
        {
            buffer.Add(new WastelandMapTrackedBlip(
                rallyPoint.Value.Position.X,
                rallyPoint.Value.Position.Y,
                "RALLY",
                WastelandMapTrackedBlipKind.GroupRallyPoint));
        }
    }

    private void AppendTribeCriticalBlips(List<WastelandMapTrackedBlip> buffer, MapId mapId, Box2 bounds)
    {
        var ritesQuery = EntityQueryEnumerator<TreeOfLifeRitesComponent>();
        var returningIsActive = false;
        while (ritesQuery.MoveNext(out _, out var rites))
        {
            if (rites.ActiveRite != TreeOfLifeRite.Returning)
                continue;

            returningIsActive = true;
            break;
        }

        if (!returningIsActive)
            return;

        var query = EntityQueryEnumerator<TreeOfLifeReturningMarkerComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (!TryComp<MobStateComponent>(uid, out var mobState) || mobState.CurrentState != MobState.Critical)
                continue;

            var coordinates = _transform.GetMapCoordinates(uid, xform);
            if (coordinates.MapId != mapId || !bounds.Contains(coordinates.Position))
                continue;

            buffer.Add(new WastelandMapTrackedBlip(
                coordinates.Position.X,
                coordinates.Position.Y,
                Name(uid),
                WastelandMapTrackedBlipKind.TribeCritical));
        }
    }

    private void AppendMaterialExtractorBlips(List<WastelandMapTrackedBlip> buffer, MapId mapId, Box2 bounds)
    {
        var query = EntityQueryEnumerator<MaterialExtractorLandmarkComponent, TransformComponent>();

        while (query.MoveNext(out var uid, out _, out var xform))
        {
            var coordinates = _transform.GetMapCoordinates(uid, xform);
            if (coordinates.MapId != mapId || !bounds.Contains(coordinates.Position))
                continue;

            var position = coordinates.Position;
            var label = $"Seismic Extractor GPS: {MathF.Round(position.X)}, {MathF.Round(position.Y)}";
            buffer.Add(new WastelandMapTrackedBlip(
                position.X,
                position.Y,
                label,
                WastelandMapTrackedBlipKind.MaterialExtractor));
        }
    }

    private void AppendExpeditionEntranceBlips(List<WastelandMapTrackedBlip> buffer, MapId mapId, Box2 bounds)
    {
        // DirectLaunch is the authoritative marker: if an entrance is actually
        // usable, every faction tacmap must show it. Do not depend on a second
        // optional landmark component drifting from the functional prototype.
        var query = EntityQueryEnumerator<N14ExpeditionBoardComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var entrance, out var xform))
        {
            if (!entrance.DirectLaunch)
                continue;

            var coordinates = _transform.GetMapCoordinates(uid, xform);
            if (coordinates.MapId != mapId || !bounds.Contains(coordinates.Position))
                continue;

            buffer.Add(new WastelandMapTrackedBlip(
                coordinates.Position.X,
                coordinates.Position.Y,
                Loc.GetString("n14-expedition-entrance-map-label"),
                WastelandMapTrackedBlipKind.ExpeditionEntrance));
        }
    }

    private void AppendTribalHuntTargetBlips(List<WastelandMapTrackedBlip> buffer, MapId mapId, Box2 bounds)
    {
        var query = EntityQueryEnumerator<LegendaryCreatureComponent, TransformComponent>();

        while (query.MoveNext(out var uid, out var legendary, out var xform))
        {
            if (!legendary.RevealLocation)
                continue;

            var mapCoordinates = _transform.GetMapCoordinates(uid, xform);
            if (mapCoordinates.MapId != mapId)
                continue;

            var pos = mapCoordinates.Position;
            if (!bounds.Contains(pos))
                continue;

            var label = string.IsNullOrWhiteSpace(legendary.CreatureName)
                ? "Legendary Target"
                : $"Legendary {legendary.CreatureName}";

            buffer.Add(new WastelandMapTrackedBlip(
                pos.X,
                pos.Y,
                label,
                WastelandMapTrackedBlipKind.TribalHuntTarget));
        }

        var minorQuery = EntityQueryEnumerator<MinorHuntCreatureComponent, TransformComponent>();

        while (minorQuery.MoveNext(out var uid, out var minor, out var xform))
        {
            if (!minor.RevealLocation)
                continue;

            var mapCoordinates = _transform.GetMapCoordinates(uid, xform);
            if (mapCoordinates.MapId != mapId)
                continue;

            var pos = mapCoordinates.Position;
            if (!bounds.Contains(pos))
                continue;

            var label = string.IsNullOrWhiteSpace(minor.CreatureName)
                ? "Minor Hunt Target"
                : $"Minor {minor.CreatureName}";

            buffer.Add(new WastelandMapTrackedBlip(
                pos.X,
                pos.Y,
                label,
                WastelandMapTrackedBlipKind.TribalHuntTarget));
        }
    }

    // #Misfits Add - Blips for dead player humanoids; used by the Followers tac-map feed.
    private void AppendDeadBodyBlips(List<WastelandMapTrackedBlip> buffer, MapId mapId, Box2 bounds)
    {
        var query = EntityQueryEnumerator<MindContainerComponent, MobStateComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var mindContainer, out var mobState, out var xform))
        {
            if (!_mobState.IsDead(uid, mobState))
                continue;

            if (!IsFollowersTrackableCasualty(uid, mindContainer))
                continue;

            var mapCoords = _transform.GetMapCoordinates(uid, xform);
            if (mapCoords.MapId != mapId)
                continue;

            var pos = mapCoords.Position;
            if (!bounds.Contains(pos))
                continue;

            buffer.Add(new WastelandMapTrackedBlip(pos.X, pos.Y, Loc.GetString("followers-missing-person"), WastelandMapTrackedBlipKind.DeadBody));
        }
    }

    private bool IsFollowersTrackableCasualty(EntityUid uid, MindContainerComponent mindContainer)
    {
        // Some non-humanoid entities can temporarily have a player mind, e.g. controlled
        // creatures or ghost roles. Followers rescue alerts are only for humanoid characters.
        if (!HasComp<HumanoidAppearanceComponent>(uid))
            return false;

        if (mindContainer.OriginalMind == null)
            return false;

        return TryComp<MindComponent>(mindContainer.OriginalMind.Value, out var mindComp)
            && mindComp.OriginalOwnerUserId != null;
    }

    // Bwonsamdi sees every player soul, including a player mind currently inhabiting a non-humanoid mob.
    private void AppendBwonsamdiSoulBlips(List<WastelandMapTrackedBlip> buffer, MapId mapId, Box2 bounds)
    {
        var query = EntityQueryEnumerator<MindContainerComponent, MobStateComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var mindContainer, out var mobState, out var xform))
        {
            if (mobState.CurrentState is not (MobState.Critical or MobState.Dead) ||
                !IsBwonsamdiTrackableSoul(mindContainer))
            {
                continue;
            }

            var mapCoords = _transform.GetMapCoordinates(uid, xform);
            if (mapCoords.MapId != mapId || !bounds.Contains(mapCoords.Position))
                continue;

            var kind = mobState.CurrentState == MobState.Critical
                ? WastelandMapTrackedBlipKind.CriticalSoul
                : WastelandMapTrackedBlipKind.DeadSoul;
            buffer.Add(new WastelandMapTrackedBlip(mapCoords.Position.X, mapCoords.Position.Y, Name(uid), kind));
        }
    }

    private bool IsBwonsamdiTrackableSoul(MindContainerComponent mindContainer)
    {
        return IsPlayerMind(mindContainer.Mind) || IsPlayerMind(mindContainer.OriginalMind);
    }

    private bool IsPlayerMind(EntityUid? mindUid)
    {
        return mindUid != null &&
               TryComp<MindComponent>(mindUid.Value, out var mind) &&
               mind.OriginalOwnerUserId != null;
    }

    // #Misfits Add - Notify Followers on player death and immediately refresh maps on revival.
    private void OnMindedEntityMobStateChanged(EntityUid uid, MindContainerComponent comp, MobStateChangedEvent args)
    {
        var wasSoulState = args.OldMobState is MobState.Critical or MobState.Dead;
        var isSoulState = args.NewMobState is MobState.Critical or MobState.Dead;
        if ((wasSoulState || isSoulState) && IsBwonsamdiTrackableSoul(comp))
            RefreshBwonsamdiMaps();

        // Only care about transitions to or from Dead.
        var wasDead = args.OldMobState == MobState.Dead;
        var isDead  = args.NewMobState == MobState.Dead;
        if (!wasDead && !isDead)
            return;

        // Ignore NPCs and controlled creatures; only act on real humanoid player characters.
        if (!IsFollowersTrackableCasualty(uid, comp))
            return;

        if (isDead)
        {
            // Player just died — notify all online Followers.
            _followerSessionScratch.Clear();
            var factionQuery = EntityQueryEnumerator<NpcFactionMemberComponent, ActorComponent>();
            while (factionQuery.MoveNext(out _, out var factionComp, out var actor))
            {
                foreach (var f in factionComp.Factions)
                {
                    if (f.Id == "Followers")
                    {
                        _followerSessionScratch.Add(actor.PlayerSession);
                        break;
                    }
                }
            }

            if (_followerSessionScratch.Count > 0)
            {
                var msg = Loc.GetString("followers-death-alert");
                foreach (var session in _followerSessionScratch)
                    _chatManager.DispatchServerMessage(session, msg);
            }
        }
        else
        {
            // Player was revived — immediately remove the blip from all active Followers maps.
            RefreshFollowersMaps();
        }
    }

    // #Misfits Add - Push an immediate state update to every open Followers tac-map.
    // Called on revival so the dead-body blip disappears without waiting for the 2.5s sweep.
    private void RefreshFollowersMaps()
    {
        var query = EntityQueryEnumerator<WastelandMapComponent, UserInterfaceComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var map, out var ui, out var xform))
        {
            if (GetEffectiveFeed(map) != WastelandMapTacticalFeedKind.Followers)
                continue;

            // Only refresh if at least one player has this map open.
            var hasViewer = false;
            foreach (var _ in _uiSystem.GetActors((uid, ui), WastelandMapUiKey.Key))
            {
                hasViewer = true;
                break;
            }
            if (!hasViewer)
                continue;

            _uiSystem.SetUiState((uid, ui), WastelandMapUiKey.Key,
                BuildState(uid, map, xform.MapID));
        }
    }

    private void RefreshBwonsamdiMaps()
    {
        var query = EntityQueryEnumerator<WastelandMapComponent, UserInterfaceComponent>();
        while (query.MoveNext(out var uid, out var map, out var ui))
        {
            if (GetEffectiveFeed(map) != WastelandMapTacticalFeedKind.Bwonsamdi)
                continue;

            foreach (var actor in _uiSystem.GetActors((uid, ui), WastelandMapUiKey.Key))
            {
                _uiSystem.SetUiState((uid, ui), WastelandMapUiKey.Key,
                    BuildState(uid, map, Transform(actor).MapID, actor: actor));
                break;
            }
        }
    }

    private void AppendIdCardBlips(List<WastelandMapTrackedBlip> buffer, MapId mapId, Box2 bounds, string requiredTag)
    {
        var query = EntityQueryEnumerator<PresetIdCardComponent, IdCardComponent, TransformComponent>();

        while (query.MoveNext(out var uid, out var presetId, out var idCard, out var xform))
        {
            if (!_tag.HasTag(uid, requiredTag))
                continue;

            var meta = MetaData(uid);

            var mapCoordinates = _transform.GetMapCoordinates(uid, xform);
            if (mapCoordinates.MapId != mapId)
                continue;

            var pos = mapCoordinates.Position;
            if (!bounds.Contains(pos))
                continue;

            var label = GetHolotagLabel(idCard, presetId);
            var kind = GetHolotagKind(idCard, presetId, meta);
            buffer.Add(new WastelandMapTrackedBlip(pos.X, pos.Y, label, kind));
        }
    }

    private static string GetHolotagLabel(IdCardComponent idCard, PresetIdCardComponent presetId)
    {
        var fullName = idCard.FullName?.Trim();
        var rank = idCard.LocalizedJobTitle?.Trim();

        if (string.IsNullOrWhiteSpace(fullName))
            return "Unknown Holotag";

        if (string.IsNullOrWhiteSpace(rank))
            rank = presetId.JobName?.ToString()?.Trim();

        if (string.IsNullOrWhiteSpace(rank))
            return fullName;

        return $"{fullName} ({rank})";
    }

    private static WastelandMapTrackedBlipKind GetHolotagKind(IdCardComponent idCard, PresetIdCardComponent presetId, MetaDataComponent meta)
    {
        // #Misfits Add - shared Willower marker for every tagged pendant/navigation card.
        var jobId = presetId.JobName?.Id;
        if (jobId is "TribalElder" or "TribalShaman" or "TribalFarmer" or "Tribal" or "SyntheticProtectronTribal")
            return WastelandMapTrackedBlipKind.Willower;

        var rank = idCard.LocalizedJobTitle?.Trim();
        if (string.IsNullOrWhiteSpace(rank))
            rank = presetId.JobName?.ToString()?.Trim();

        var protoId = meta.EntityPrototype?.ID ?? string.Empty;
        var source = string.IsNullOrWhiteSpace(rank) ? protoId : rank;

        if (source.Contains("elder", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("commander", StringComparison.OrdinalIgnoreCase))
        {
            return WastelandMapTrackedBlipKind.Elder;
        }

        if (source.Contains("paladin", StringComparison.OrdinalIgnoreCase))
            return WastelandMapTrackedBlipKind.Paladin;

        if (source.Contains("knight", StringComparison.OrdinalIgnoreCase))
            return WastelandMapTrackedBlipKind.Knight;

        if (source.Contains("scribe", StringComparison.OrdinalIgnoreCase))
            return WastelandMapTrackedBlipKind.Scribe;

        if (source.Contains("squire", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("initiate", StringComparison.OrdinalIgnoreCase))
        {
            return WastelandMapTrackedBlipKind.Squire;
        }

        // #Misfits Add - Legion rank detection for the Centurion tactical computer
        if (source.Contains("legate", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("centurion", StringComparison.OrdinalIgnoreCase))
        {
            return WastelandMapTrackedBlipKind.LegionCenturion;
        }

        if (source.Contains("decanus", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("dean", StringComparison.OrdinalIgnoreCase)) // CaesarLegionDean = Decanus in-game
        {
            return WastelandMapTrackedBlipKind.LegionDecanus;
        }

        if (source.Contains("legionnaire", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("vexillarius", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("houndmaster", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("frumentarii", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("optio", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("explorer", StringComparison.OrdinalIgnoreCase))
        {
            return WastelandMapTrackedBlipKind.LegionWarrior;
        }

        if (source.Contains("auxilia", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("recruit", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("slave", StringComparison.OrdinalIgnoreCase))
        {
            return WastelandMapTrackedBlipKind.LegionRecruit;
        }
        // End Misfits Add

        return WastelandMapTrackedBlipKind.Unknown;
    }
}
