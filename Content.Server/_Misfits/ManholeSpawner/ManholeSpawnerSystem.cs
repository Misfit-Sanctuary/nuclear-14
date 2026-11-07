using Content.Server._Misfits.ManholeSpawner.Components;
using Content.Shared._Misfits.ManholeSpawner;
using Content.Shared.Database;
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
using Robust.Shared.Timing;
using Robust.Shared.Utility;

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
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ManholeSpawnerComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<ManholeSpawnerComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<ManholeSpawnerComponent, GetVerbsEvent<AlternativeVerb>>(OnAltVerb);
        SubscribeLocalEvent<ManholeSpawnerComponent, GetPryTimeModifierEvent>(OnPryTimeModifier);
        SubscribeLocalEvent<ManholeSpawnerComponent, DoorPryDoAfterEvent>(OnPryDoAfter);
        SubscribeLocalEvent<ManholeSpawnerComponent, ComponentShutdown>(OnSpawnerShutdown);
        SubscribeLocalEvent<SpawnedByManholeComponent, EntityTerminatingEvent>(OnSpawnedTerminating);
        SubscribeLocalEvent<SpawnedByManholeComponent, MobStateChangedEvent>(OnSpawnedMobStateChanged);
    }

    // Only open manholes spawn.
    public override void Update(float frameTime)
    {
        var curTime = _timing.CurTime;
        var query = EntityQueryEnumerator<ManholeSpawnerComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (!comp.Open || curTime < comp.CheckTime)
                continue;

            comp.CheckTime = curTime + CheckInterval(comp);
            if (CanSpawn(uid, comp))
                SpawnMobs(uid, comp);
        }
    }

    // Clamped so a zero interval cannot fire every frame.
    private TimeSpan CheckInterval(ManholeSpawnerComponent comp)
        => TimeSpan.FromSeconds(Math.Max(1u, comp.IntervalSeconds));

    // Player near, under cap, and lucky.
    private bool CanSpawn(EntityUid uid, ManholeSpawnerComponent comp)
    {
        if (!IsPlayerNearby(uid, comp.ActivationRange))
            return false;

        // Zero or less means no cap. Counted by event, never by scanning.
        if (comp.MaxAliveNearby > 0 && comp.AliveCount >= comp.MaxAliveNearby)
            return false;

        // Content-editable, so clamp rather than assert in Prob.
        return _random.Prob(Math.Clamp(comp.Chance, 0f, 1f));
    }

    private void SpawnMobs(EntityUid uid, ManholeSpawnerComponent comp)
    {
        if (comp.Prototypes.Count == 0)
            return;

        // Shuffle and slice so a wave draws without replacement instead of repeating one entry.
        var pool = comp.Prototypes.ToArray();
        _random.Shuffle(pool.AsSpan());

        var count = Math.Min(_random.Next(comp.MinimumEntitiesSpawned, comp.MaximumEntitiesSpawned + 1), pool.Length);
        var coordinates = Transform(uid).Coordinates;
        foreach (var picked in pool.AsSpan(0, count))
        {
            if (comp.MaxAliveNearby > 0 && comp.AliveCount >= comp.MaxAliveNearby)
                return;

            try
            {
                Track(uid, comp, SpawnAtPosition(picked, coordinates));
            }
            catch (EntityCreationException e)
            {
                // One bad prototype should not stop the wave, but never let broken YAML pass silently.
                DebugTools.Assert($"Manhole spawner {ToPrettyString(uid)} could not spawn {picked}: {e}");
                Log.Warning($"Caught an exception while trying to spawn {picked} from manhole spawner " +
                            $"{ToPrettyString(uid)}: {e}");
            }
        }
    }

    private void Track(EntityUid spawner, ManholeSpawnerComponent comp, EntityUid mob)
    {
        var marker = EnsureComp<SpawnedByManholeComponent>(mob);
        marker.Spawner = spawner;

        comp.AliveCount++;
    }

    /// Idempotent, a mob that already stopped counting cannot double decrement.
    private void Untrack(EntityUid spawner, SpawnedByManholeComponent marker)
    {
        if (!marker.Counted)
            return;

        marker.Counted = false;

        // Markers are stripped on spawner shutdown, so a live marker implies a live spawner comp.
        var comp = Comp<ManholeSpawnerComponent>(spawner);
        comp.AliveCount = Math.Max(0, comp.AliveCount - 1);
    }

    // Living player in range, 0 means always.
    private bool IsPlayerNearby(EntityUid uid, float range)
    {
        if (range <= 0f)
            return true;

        var xform = Transform(uid);
        var mapPos = _transform.GetMapCoordinates(uid, xform);

        // Only actors come back, so the broad phase does the filtering for us.
        foreach (var actor in _lookup.GetEntitiesInRange<ActorComponent>(mapPos, range, LookupFlags.Dynamic))
        {
            if (actor.Owner != uid && IsActivePlayer(actor))
                return true;
        }

        return false;
    }

    /// Living and not AFK.
    private bool IsActivePlayer(Entity<ActorComponent> actor)
    {
        if (!TryComp(actor.Owner, out MobStateComponent? mob)
            || (mob.CurrentState != MobState.Alive && mob.CurrentState != MobState.Critical))
            return false;

        return !TryComp<SSDIndicatorComponent>(actor.Owner, out var ssd) || !ssd.IsSSD;
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

        if (comp.MinimumEntitiesSpawned > comp.MaximumEntitiesSpawned)
            throw new InvalidOperationException($"Manhole spawner {ToPrettyString(uid)} has " +
                                                $"minimumEntitiesSpawned ({comp.MinimumEntitiesSpawned}) above " +
                                                $"maximumEntitiesSpawned ({comp.MaximumEntitiesSpawned}).");

        comp.CheckTime = _timing.CurTime + CheckInterval(comp);
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

        // Re-opening a manhole rolls on the next update instead of waiting out the old deadline.
        if (comp.Open)
            comp.CheckTime = _timing.CurTime;

        _popup.PopupClient(Loc.GetString(comp.Open ? "manhole-pry-open-popup" : "manhole-pry-close-popup"), uid, args.User);
        UpdateAppearance(uid, comp);
    }

    private void UpdateAppearance(EntityUid uid, ManholeSpawnerComponent comp)
        => _appearance.SetData(uid, ManholeSpawnerVisuals.Open, comp.Open);

    // A dying mob stops counting immediately, corpses are reaped much later.
    private void OnSpawnedMobStateChanged(EntityUid uid, SpawnedByManholeComponent marker, MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Dead)
            return;

        Untrack(marker.Spawner, marker);
    }

    // Catches queue dels, map changes and anything else that skips the death path.
    private void OnSpawnedTerminating(EntityUid uid, SpawnedByManholeComponent marker, ref EntityTerminatingEvent args)
    {
        if (TerminatingOrDeleted(marker.Spawner))
            return;

        Untrack(marker.Spawner, marker);
    }

    private void OnSpawnerShutdown(EntityUid uid, ManholeSpawnerComponent comp, ComponentShutdown args)
    {
        comp.AliveCount = 0;

        // Drop our markers too, or they outlive the spawner they point at.
        var query = EntityQueryEnumerator<SpawnedByManholeComponent>();
        while (query.MoveNext(out var mob, out var marker))
        {
            if (marker.Spawner == uid)
                RemComp<SpawnedByManholeComponent>(mob);
        }
    }
}
