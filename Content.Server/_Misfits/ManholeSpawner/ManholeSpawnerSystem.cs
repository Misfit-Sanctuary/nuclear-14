using Content.Shared._Misfits.ManholeSpawner;
using Content.Shared.Database;
using Content.Shared.DoAfter;
using Content.Shared.Humanoid;
using Content.Shared.Interaction;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Content.Shared.Prying.Components;
using Content.Shared.SSDIndicator;
using Content.Shared.Verbs;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Player;
using Robust.Shared.Random;

namespace Content.Server._Misfits.ManholeSpawner;

/// Crowbar-pried manhole cover: open spawns hostile mobs nearby, closed freezes it.
public sealed class ManholeSpawnerSystem : EntitySystem
{
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly TransformSystem _transform = default!;

    private EntityQuery<PryingComponent> _pryingQuery = default!;

    public override void Initialize()
    {
        base.Initialize();

        _pryingQuery = GetEntityQuery<PryingComponent>();

        SubscribeLocalEvent<ManholeSpawnerComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<ManholeSpawnerComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<ManholeSpawnerComponent, GetVerbsEvent<AlternativeVerb>>(OnAltVerb);
        SubscribeLocalEvent<ManholeSpawnerComponent, ManholePryDoAfterEvent>(OnPryDoAfter);
    }

    // Periodic spawn loop: only open manholes tick, and each interval spawns a small wave.
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

            comp.TimeElapsed = 0;
            if (CanSpawn(uid, comp))
                SpawnMobs(uid, comp);
        }
    }

    // All the gates for one spawn: player in range, local cap not hit, then the chance roll.
    private bool CanSpawn(EntityUid uid, ManholeSpawnerComponent comp)
    {
        if (!IsPlayerNearby(uid, comp.ActivationRange))
            return false;

        if (CountAliveNearby(uid, comp) >= comp.MaxAliveNearby)
            return false;

        return _random.Prob(comp.Chance);
    }

    // Spawns a random count of random prototypes right on top of the manhole.
    private void SpawnMobs(EntityUid uid, ManholeSpawnerComponent comp)
    {
        if (comp.Prototypes.Count == 0)
            return;

        var count = _random.Next(comp.MinimumEntitiesSpawned, comp.MaximumEntitiesSpawned + 1);
        for (var i = 0; i < count; i++)
            SpawnAtPosition(_random.Pick(comp.Prototypes), Transform(uid).Coordinates);
    }

    // True if a living, connected (non-SSD) humanoid player is within range. 0 = always.
    private bool IsPlayerNearby(EntityUid uid, float range)
    {
        if (range <= 0f)
            return true;

        if (!TryComp<TransformComponent>(uid, out var xform) || xform.MapUid == null)
            return false;

        var mapPos = _transform.GetMapCoordinates(uid, xform: xform);

        foreach (var entity in _lookup.GetEntitiesInRange(mapPos, range))
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

    // Counts alive mobs of the configured prototypes within NearbyRange of the manhole (local cap).
    private int CountAliveNearby(EntityUid uid, ManholeSpawnerComponent comp)
    {
        if (comp.MaxAliveNearby <= 0 || comp.Prototypes.Count == 0)
            return 0;

        if (!TryComp<TransformComponent>(uid, out var xform) || xform.MapUid == null)
            return comp.MaxAliveNearby;

        var mapPos = _transform.GetMapCoordinates(uid, xform: xform);
        var count = 0;

        foreach (var entity in _lookup.GetEntitiesInRange(mapPos, comp.NearbyRange))
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

    // Refresh visuals after load so a saved open/closed state matches the sprite.
    private void OnMapInit(EntityUid uid, ManholeSpawnerComponent comp, ref MapInitEvent args)
        => UpdateAppearance(uid, comp);

    // Click with a crowbar (PryingComponent tool) starts a pry.
    private void OnInteractUsing(EntityUid uid, ManholeSpawnerComponent comp, InteractUsingEvent args)
    {
        if (args.Handled || !_pryingQuery.TryGetComponent(args.Used, out var prying) || !prying.Enabled)
            return;

        args.Handled = true;
        TryStartPry(uid, comp, args.User, args.Used);
    }

    // Right-click "Pry manhole open/shut" verb, only while holding a prying tool.
    private void OnAltVerb(EntityUid uid, ManholeSpawnerComponent comp, GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanInteract || !args.CanAccess || args.Using is not { } used
            || !_pryingQuery.TryGetComponent(used, out var prying) || !prying.Enabled)
            return;

        var tool = used;
        args.Verbs.Add(new AlternativeVerb()
        {
            Text = Loc.GetString(comp.Open ? "manhole-pry-verb-close" : "manhole-pry-verb-open"),
            Impact = LogImpact.Low,
            Act = () => TryStartPry(uid, comp, args.User, tool),
        });
    }

    // Starts the timed pry hold; breaks if the user moves or takes damage.
    private void TryStartPry(EntityUid uid, ManholeSpawnerComponent comp, EntityUid user, EntityUid tool)
    {
        if (!_pryingQuery.TryGetComponent(tool, out var prying) || !prying.Enabled)
            return;

        var delay = TimeSpan.FromSeconds(comp.PryTime / prying.SpeedModifier);
        var doAfter = new DoAfterArgs(EntityManager, user, delay, new ManholePryDoAfterEvent(), uid, uid, tool)
        {
            BreakOnDamage = true,
            BreakOnMove = true,
        };

        _doAfter.TryStartDoAfter(doAfter);
    }

    // Pry finished: play the crowbar sound, toggle the cover and reset the visuals.
    private void OnPryDoAfter(EntityUid uid, ManholeSpawnerComponent comp, ManholePryDoAfterEvent args)
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

    // Pushes the open/closed state to the client visualizer, which swaps the sprite.
    private void UpdateAppearance(EntityUid uid, ManholeSpawnerComponent comp)
        => _appearance.SetData(uid, ManholeSpawnerVisuals.State, comp.Open ? ManholeSpawnerState.Open : ManholeSpawnerState.Closed);
}
