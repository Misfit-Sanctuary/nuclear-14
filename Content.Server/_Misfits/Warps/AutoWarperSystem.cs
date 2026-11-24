using System;
using Content.Server.Warps;
using Content.Shared._Misfits.Warps;
using Content.Shared.StepTrigger.Systems;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._Misfits.Warps;

/// <summary>
/// Bridges the native StepTrigger and Warper systems for mapper-placed walk-over warp points.
/// WarperSystem remains responsible for all movement, including cross-map safety, pulls and
/// followers; this system only decides when a player has entered an endpoint.
/// </summary>
public sealed class AutoWarperSystem : EntitySystem
{
    private static readonly TimeSpan ReturnDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan TravelDuration = TimeSpan.FromSeconds(2.5);
    private static readonly TimeSpan FadeOutDuration = TimeSpan.FromSeconds(0.25);

    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly WarperSystem _warper = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedMapSystem _maps = default!;
    private readonly Dictionary<EntityUid, (EntityUid Destination, TimeSpan Due)> _pendingWarps = new();

    public override void Initialize()
    {
        SubscribeLocalEvent<AutoWarperComponent, StepTriggerAttemptEvent>(OnStepTriggerAttempt);
        SubscribeLocalEvent<AutoWarperComponent, StepTriggeredOnEvent>(OnSteppedOnto);
    }

    public override void Update(float frameTime)
    {
        var now = _timing.CurTime;
        foreach (var (traveller, pending) in _pendingWarps.ToArray())
        {
            if (now < pending.Due)
                continue;

            _pendingWarps.Remove(traveller);
            if (Deleted(traveller))
                continue;

            var sourceMap = Transform(traveller).MapID;
            if (Deleted(pending.Destination) || !_warper.WarpEntityTo(traveller, pending.Destination))
            {
                RemComp<AutoWarperCooldownComponent>(traveller);
                continue;
            }

            if (TryComp<AutoWarperCooldownComponent>(traveller, out var arrivalGuard))
                arrivalGuard.HasArrived = true;

            if (sourceMap != Transform(pending.Destination).MapID &&
                TryComp<ActorComponent>(traveller, out var actor) &&
                GetDestinationName(pending.Destination) is { } placeName)
            {
                RaiseNetworkEvent(new AutoWarperArrivalEvent(placeName), actor.PlayerSession.Channel);
            }
        }

        var guardQuery = EntityQueryEnumerator<AutoWarperCooldownComponent>();
        while (guardQuery.MoveNext(out var traveller, out var guard))
        {
            if (guard.HasArrived && !guard.HasLeftDestination)
                guard.HasLeftDestination = IsOutsideArrivalLine(traveller, guard.ArrivalPrototypeId);
        }
    }

    private void OnStepTriggerAttempt(Entity<AutoWarperComponent> ent, ref StepTriggerAttemptEvent args)
    {
        if (!HasComp<ActorComponent>(args.Tripper))
        {
            args.Cancelled = true;
            return;
        }

        args.Continue = true;
    }

    private void OnSteppedOnto(Entity<AutoWarperComponent> ent, ref StepTriggeredOnEvent args)
    {
        // These endpoints are for player traversal, not loose items or NPCs.
        if (!TryComp<ActorComponent>(args.Tripper, out var actor) ||
            (TryComp<AutoWarperCooldownComponent>(args.Tripper, out var cooldown) &&
             (cooldown.ExpiresAt > _timing.CurTime || !cooldown.HasLeftDestination)))
        {
            return;
        }

        var sourcePrototypeId = MetaData(ent.Owner).EntityPrototype?.ID;
        var destinationPrototypeId = sourcePrototypeId == null ? null : GetOppositePrototypeId(sourcePrototypeId);
        if (destinationPrototypeId == null)
            return;

        var destination = FindOppositeEndpoint(ent.Owner, destinationPrototypeId);
        if (destination == null || !PrepareDestinationMap(destination.Value))
            return;

        var arrivalGuard = EnsureComp<AutoWarperCooldownComponent>(args.Tripper);
        arrivalGuard.ExpiresAt = _timing.CurTime + TravelDuration + ReturnDelay;
        arrivalGuard.ArrivalPrototypeId = destinationPrototypeId;
        arrivalGuard.HasArrived = false;
        arrivalGuard.HasLeftDestination = false;
        _pendingWarps[args.Tripper] = (destination.Value, _timing.CurTime + TravelDuration);
        // The final quarter second occurs after the server moves the traveller, so the player
        // fades back in only once they have reached the other endpoint.
        RaiseNetworkEvent(new AutoWarperTravelEvent((float) (TravelDuration + FadeOutDuration).TotalSeconds), actor.PlayerSession.Channel);
    }

    /// <summary>
    /// The endpoint name defines its opposite. For example, AtoB can only resolve BtoA.
    /// This also works for additional letter pairs without changing the system.
    /// </summary>
    private static string? GetOppositePrototypeId(string sourcePrototypeId)
    {
        const string prefix = "MisfitsWarperPoint";
        if (!sourcePrototypeId.StartsWith(prefix, StringComparison.Ordinal))
            return null;

        var pair = sourcePrototypeId[prefix.Length..];
        var separator = pair.IndexOf("to", StringComparison.Ordinal);
        if (separator <= 0 || separator + 2 >= pair.Length ||
            pair.IndexOf("to", separator + 2, StringComparison.Ordinal) >= 0)
        {
            return null;
        }

        return $"{prefix}{pair[(separator + 2)..]}to{pair[..separator]}";
    }

    /// <summary>
    /// Only an entity of the opposite endpoint prototype is a valid destination. Prefer a
    /// running map over a map loaded for staging; the latter can be initialized on use.
    /// </summary>
    private EntityUid? FindOppositeEndpoint(EntityUid source, string destinationPrototypeId)
    {
        EntityUid? loadedEndpoint = null;
        // loadmap pauses every endpoint until its map is initialized. The regular entity query
        // excludes paused entities, so it would never find a marker on the loaded map.
        var query = AllEntityQuery<AutoWarperComponent, MetaDataComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var meta, out var xform))
        {
            if (uid == source || Deleted(uid) || meta.EntityPrototype?.ID != destinationPrototypeId ||
                !_maps.MapExists(xform.MapID))
                continue;

            if (_maps.IsInitialized(xform.MapID))
            {
                if (_maps.IsPaused(xform.MapID))
                    continue;

                return uid;
            }

            loadedEndpoint ??= uid;
        }

        return loadedEndpoint;
    }

    /// <summary>
    /// The loadmap console command leaves newly loaded maps uninitialized and paused. Start only
    /// the selected destination map, so WarperSystem can move a player to it after the fade.
    /// An initialized map that an admin deliberately paused remains unavailable.
    /// </summary>
    private bool PrepareDestinationMap(EntityUid destination)
    {
        var mapId = Transform(destination).MapID;
        if (!_maps.MapExists(mapId))
            return false;

        if (!_maps.IsInitialized(mapId))
            _maps.InitializeMap(mapId);

        return !_maps.IsPaused(mapId);
    }

    /// <summary>Prefer the destination grid's mapper-provided name, then its map name.</summary>
    private string? GetDestinationName(EntityUid destination)
    {
        var xform = Transform(destination);
        if (xform.MapUid is { } destinationMap && HasComp<AutoWarperSuppressWelcomeComponent>(destinationMap))
            return null;

        if (xform.GridUid is { } grid && TryComp<MetaDataComponent>(grid, out var gridMeta) &&
            IsPlaceName(gridMeta.EntityName))
            return gridMeta.EntityName;

        if (xform.MapUid is { } map && TryComp<MetaDataComponent>(map, out var mapMeta) &&
            IsPlaceName(mapMeta.EntityName))
            return mapMeta.EntityName;

        return null;
    }

    private static bool IsPlaceName(string name) =>
        !string.IsNullOrWhiteSpace(name) &&
        !name.Equals("grid", StringComparison.OrdinalIgnoreCase) &&
        !name.Equals("Map Entity", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A row of arrival markers is one exit area. Leaving one marker while still touching another
    /// must not arm an immediate return trip.
    /// </summary>
    private bool IsOutsideArrivalLine(EntityUid traveller, string arrivalPrototypeId)
    {
        var travellerXform = Transform(traveller);
        var travellerBounds = _lookup.GetWorldAABB(traveller, travellerXform);
        var query = EntityQueryEnumerator<AutoWarperComponent, MetaDataComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var meta, out var xform))
        {
            if (meta.EntityPrototype?.ID == arrivalPrototypeId &&
                xform.MapID == travellerXform.MapID &&
                _lookup.GetWorldAABB(uid, xform).Intersects(travellerBounds))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>Enables walk-over triggering for a named pair of endpoint prototypes.</summary>
[RegisterComponent]
public sealed partial class AutoWarperComponent : Component;

/// <summary>Prevents a return warp until the delay passes and the traveller leaves the arrival point.</summary>
[RegisterComponent]
public sealed partial class AutoWarperCooldownComponent : Component
{
    public TimeSpan ExpiresAt;
    public string ArrivalPrototypeId = string.Empty;
    public bool HasArrived;
    public bool HasLeftDestination;
}

/// <summary>Suppresses the auto-warper arrival greeting for a specific map.</summary>
[RegisterComponent]
public sealed partial class AutoWarperSuppressWelcomeComponent : Component;
