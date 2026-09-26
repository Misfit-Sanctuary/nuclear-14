using Content.Shared._Misfits.ManholeSpawner;
using Content.Shared.Database;
using Content.Shared.Humanoid;
using Content.Shared.Interaction;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Content.Shared.Prying.Components;
using Content.Shared.Prying.Systems;
using Content.Shared.SSDIndicator;
using Content.Shared.Verbs;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Player;
using Robust.Shared.Random;

namespace Content.Server._Misfits.ManholeSpawner;

/// Open spawns mobs, closed does not.
public sealed class ManholeSpawnerSystem : EntitySystem
{
    [Dependency] private readonly PryingSystem _prying = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly TransformSystem _transform = default!;

    // Reused buffers, clear before use.
    private readonly HashSet<EntityUid> _playerScan = new();
    private readonly HashSet<EntityUid> _aliveScan = new();

    private EntityQuery<TransformComponent> _xformQuery = default!;

    public override void Initialize()
    {
        base.Initialize();

        _xformQuery = GetEntityQuery<TransformComponent>();

        SubscribeLocalEvent<ManholeSpawnerComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<ManholeSpawnerComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<ManholeSpawnerComponent, GetVerbsEvent<AlternativeVerb>>(OnAltVerb);
        SubscribeLocalEvent<ManholeSpawnerComponent, GetPryTimeModifierEvent>(OnPryTimeModifier);
        SubscribeLocalEvent<ManholeSpawnerComponent, DoorPryDoAfterEvent>(OnPryDoAfter);
    }

    // Only open manholes spawn.
    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<ManholeSpawnerComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (!comp.Open)
                continue;

            comp.TimeElapsed += frameTime;
            if (comp.TimeElapsed < comp.IntervalSeconds)
                continue;

            comp.TimeElapsed -= comp.IntervalSeconds;
            if (CanSpawn(uid, comp))
                SpawnMobs(uid, comp);
        }
    }

    // Player near, under cap, and lucky.
    private bool CanSpawn(EntityUid uid, ManholeSpawnerComponent comp)
    {
        if (!IsPlayerNearby(uid, comp.ActivationRange))
            return false;

        // Zero or less means no cap.
        if (comp.MaxAliveNearby > 0 && CountAliveNearby(uid, comp) >= comp.MaxAliveNearby)
            return false;

        return _random.Prob(comp.Chance);
    }

    private void SpawnMobs(EntityUid uid, ManholeSpawnerComponent comp)
    {
        if (comp.Prototypes.Count == 0)
            return;

        var coordinates = _xformQuery.GetComponent(uid).Coordinates;
        var count = _random.Next(comp.MinimumEntitiesSpawned, comp.MaximumEntitiesSpawned + 1);
        for (var i = 0; i < count; i++)
        {
            var picked = _random.Pick(comp.Prototypes);
            try
            {
                SpawnAtPosition(picked, coordinates);
            }
            catch (EntityCreationException e)
            {
                // One bad prototype should not stop the wave.
                Log.Warning($"Caught an exception while trying to spawn {picked} from manhole spawner " +
                            $"{ToPrettyString(uid)}: {e}");
            }
        }
    }

    // Living player in range, 0 means always.
    private bool IsPlayerNearby(EntityUid uid, float range)
    {
        if (range <= 0f)
            return true;

        if (!_xformQuery.TryGetComponent(uid, out var xform) || xform.MapUid == null)
            return false;

        var mapPos = _transform.GetMapCoordinates(uid, xform: xform);

        _playerScan.Clear();
        _lookup.GetEntitiesInRange(mapPos.MapId, mapPos.Position, range, _playerScan);

        foreach (var entity in _playerScan)
        {
            if (!Exists(entity) || entity == uid)
                continue;

            if (TryComp(entity, out MobStateComponent? mob) &&
                (mob.CurrentState == MobState.Alive || mob.CurrentState == MobState.Critical) &&
                HasComp<HumanoidAppearanceComponent>(entity) &&
                HasComp<ActorComponent>(entity) &&
                (!TryComp<SSDIndicatorComponent>(entity, out var ssd) || !ssd.IsSSD))
                return true;
        }

        return false;
    }

    private int CountAliveNearby(EntityUid uid, ManholeSpawnerComponent comp)
    {
        // Zero or less means no cap.
        if (comp.NearbyRange <= 0f || comp.Prototypes.Count == 0)
            return 0;

        if (!_xformQuery.TryGetComponent(uid, out var xform) || xform.MapUid == null)
            return comp.MaxAliveNearby;

        var mapPos = _transform.GetMapCoordinates(uid, xform: xform);
        var count = 0;

        _aliveScan.Clear();
        _lookup.GetEntitiesInRange(mapPos.MapId, mapPos.Position, comp.NearbyRange, _aliveScan);

        foreach (var entity in _aliveScan)
        {
            if (!Exists(entity) || entity == uid)
                continue;

            if (!TryComp(entity, out MobStateComponent? mob) ||
                (mob.CurrentState != MobState.Alive && mob.CurrentState != MobState.Critical))
                continue;

            if (!TryComp(entity, out MetaDataComponent? meta) ||
                meta.EntityPrototype?.ID is not { } prototypeId ||
                !comp.Prototypes.Contains(prototypeId))
                continue;

            if (++count >= comp.MaxAliveNearby)
                return count;
        }

        return count;
    }

    // Sync sprite on load.
    private void OnMapInit(EntityUid uid, ManholeSpawnerComponent comp, ref MapInitEvent args)
    {
        // Warn once about empty prototype list.
        if (comp.Prototypes.Count == 0)
        {
            Log.Warning($"Manhole spawner {ToPrettyString(uid)} has an empty prototype list and will " +
                        $"never spawn anything.");
        }

        UpdateAppearance(uid, comp);
    }

    // Starts pry with a crowbar.
    private void OnInteractUsing(EntityUid uid, ManholeSpawnerComponent comp, InteractUsingEvent args)
    {
        // Let other tools fall through.
        if (args.Handled || !HasComp<PryingComponent>(args.Used))
            return;

        args.Handled = _prying.TryPry(uid, args.User, out _, args.Used);
    }

    // Right-click pry verb, needs a pry tool.
    private void OnAltVerb(EntityUid uid, ManholeSpawnerComponent comp, GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanInteract || !args.CanAccess || args.Using is not { } tool
            || !HasComp<PryingComponent>(tool))
            return;

        args.Verbs.Add(new AlternativeVerb()
        {
            Text = Loc.GetString(comp.Open ? "manhole-pry-verb-close" : "manhole-pry-verb-open"),
            Impact = LogImpact.Low,
            // Humans cannot hand-pry, pass the tool.
            Act = () => _prying.TryPry(uid, args.User, out _, tool),
        });
    }

    private void OnPryTimeModifier(EntityUid uid, ManholeSpawnerComponent comp, ref GetPryTimeModifierEvent args)
    {
        args.BaseTime = comp.PryTime;
    }

    // Manholes finish the door pry event here.
    private void OnPryDoAfter(EntityUid uid, ManholeSpawnerComponent comp, DoorPryDoAfterEvent args)
    {
        if (args.Cancelled || args.Target is null)
            return;

        if (args.Used is { } used && TryComp<PryingComponent>(used, out var prying))
            _audio.PlayPredicted(prying.UseSound, used, args.User);

        comp.Open = !comp.Open;
        Dirty(uid, comp);

        _popup.PopupClient(Loc.GetString(comp.Open ? "manhole-pry-open-popup" : "manhole-pry-close-popup"), uid, args.User);
        UpdateAppearance(uid, comp);
    }

    private void UpdateAppearance(EntityUid uid, ManholeSpawnerComponent comp)
        => _appearance.SetData(uid, ManholeSpawnerVisuals.Open, comp.Open);
}
