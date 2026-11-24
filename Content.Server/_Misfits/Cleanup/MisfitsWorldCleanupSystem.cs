// #Misfits Add - Automatic despawn for puddles, footprints, and giblets to prevent entity accumulation

using Content.Shared.Body.Organ;
using Content.Shared.Body.Part;
using Content.Shared.Fluids.Components;
using Content.Shared.Throwing;
using Robust.Shared.Spawners;

namespace Content.Server._Misfits.Cleanup;

/// <summary>
/// Automatically cleans up entities that would otherwise persist forever and silently
/// accumulate over a long round:
/// <list type="bullet">
///   <item>Blood puddles — created every bleed tick, blood never evaporates (via <see cref="TimedDespawnComponent"/>).</item>
///   <item>Footprints — spawned every step through a puddle (via <see cref="TimedDespawnComponent"/>).</item>
///   <item>Giblets (body parts + organs) — dropped when mobs are gibbed (via <see cref="GibletCleanupComponent"/>).</item>
/// </list>
/// </summary>
public sealed class MisfitsWorldCleanupSystem : EntitySystem
{
    // #Cythisiax Added - one-second expiry resolution avoids scanning ten-minute giblets every tick.
    private const float GibletSweepInterval = 1f;
    private float _gibletSweepAccumulator;
    private readonly List<EntityUid> _toStop = new();
    private readonly List<EntityUid> _toDelete = new();

    // Puddles (blood, chemicals, water) — 10 minutes gives plenty of time
    // for gameplay interaction (slip, forensics, mopping) before cleanup.
    private const float PuddleLifetime = 600f;

    // Body parts and organs scattered during gibbing — 10 minutes.
    private const float GibletLifetime = 600f;

    public override void Initialize()
    {
        base.Initialize();

        // Covers both regular puddles AND footprint entities (Footstep prototype
        // also carries PuddleComponent).
        SubscribeLocalEvent<PuddleComponent, ComponentStartup>(OnPuddleStartup);

        // Giblets are thrown with impulse during gibbing. When they land, we tag
        // them for cleanup. This handles the vast majority of gib scenarios.
        SubscribeLocalEvent<BodyPartComponent, LandEvent>(OnBodyPartLand);
        SubscribeLocalEvent<OrganComponent, LandEvent>(OnOrganLand);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // #Cythisiax Edited - accumulate elapsed time and reuse buffers during periodic cleanup sweeps.
        _gibletSweepAccumulator += frameTime;
        if (_gibletSweepAccumulator < GibletSweepInterval)
            return;

        var elapsed = _gibletSweepAccumulator;
        _gibletSweepAccumulator = 0f;
        _toStop.Clear();
        _toDelete.Clear();

        // Giblet cleanup is driven from this system's own tick rather than subscribing to
        // SharedBodySystem's attach events — the directed (component, event) pairs for body
        // part attachment are already owned by SharedBodySystem and its _Shitmed partials
        // (e.g. (BodyPartComponent, EntInsertedIntoContainerMessage), (BodyComponent,
        // BodyPartAddedEvent)), so hooking them caused "Duplicate Subscriptions" at startup.
        // # #Cythisiax Fixed - duplicate directed subscription crash; cleanup now runs from own Update loop
        var parts = EntityQueryEnumerator<BodyPartComponent, GibletCleanupComponent>();
        while (parts.MoveNext(out var uid, out var part, out var cleanup))
        {
            // Attached to a body (e.g. a surgeon recovered it) — stop cleanup so the limb never despawns.
            if (part.Body != null)
            {
                _toStop.Add(uid);
                continue;
            }
            if (TerminatingOrDeleted(uid))
                continue;
            cleanup.Accumulator += elapsed;
            if (cleanup.Accumulator >= cleanup.Lifetime)
                _toDelete.Add(uid);
        }

        var organs = EntityQueryEnumerator<OrganComponent, GibletCleanupComponent>();
        while (organs.MoveNext(out var uid, out var organ, out var cleanup))
        {
            if (organ.Body != null)
            {
                _toStop.Add(uid);
                continue;
            }
            if (TerminatingOrDeleted(uid))
                continue;
            cleanup.Accumulator += elapsed;
            if (cleanup.Accumulator >= cleanup.Lifetime)
                _toDelete.Add(uid);
        }

        foreach (var uid in _toStop)
            RemComp<GibletCleanupComponent>(uid);
        foreach (var uid in _toDelete)
            QueueDel(uid);
    }

    private void OnPuddleStartup(EntityUid uid, PuddleComponent comp, ComponentStartup args)
    {
        if (TerminatingOrDeleted(uid))
            return;

        var despawn = EnsureComp<TimedDespawnComponent>(uid);

        // Only upgrade lifetime; don't shorten an existing shorter timer.
        if (despawn.Lifetime < PuddleLifetime)
            despawn.Lifetime = PuddleLifetime;
    }

    private void OnBodyPartLand(EntityUid uid, BodyPartComponent comp, ref LandEvent args)
    {
        // Part is still attached to a living body — don't despawn (e.g. held limbs).
        if (comp.Body != null)
            return;

        AddGibletDespawn(uid);
    }

    private void OnOrganLand(EntityUid uid, OrganComponent comp, ref LandEvent args)
    {
        // Organ is still inside a body — don't despawn.
        if (comp.Body != null)
            return;

        AddGibletDespawn(uid);
    }

    private void AddGibletDespawn(EntityUid uid)
    {
        if (TerminatingOrDeleted(uid))
            return;

        var cleanup = EnsureComp<GibletCleanupComponent>(uid);
        cleanup.Lifetime = GibletLifetime;
        cleanup.Accumulator = 0f;
    }
}
