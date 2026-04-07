// #Misfits Add - NPC corpse cleanup system to prevent entity accumulation over long sessions
// #Misfits Fix - use TerminatingOrDeleted + TryComp to prevent NRE on dying entities

using Content.Server.NPC.HTN;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs;
using Robust.Shared.Spawners;

namespace Content.Server._Misfits.Cleanup;

public sealed class MisfitsCleanupSystem : EntitySystem
{
    private const float NpcCorpseLifetime = 1200f;

    public override void Initialize()
    {
        base.Initialize();

        // Broadcast subscription — directed is unavailable because HTNSystem already
        // subscribes <HTNComponent, MobStateChangedEvent> and the engine forbids duplicates.
        SubscribeLocalEvent<MobStateChangedEvent>(OnMobStateChanged);
    }

    private void OnMobStateChanged(MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Dead)
            return;

        var uid = args.Target;

        // TerminatingOrDeleted is the strongest lifecycle guard — catches fully deleted,
        // mid-termination, and recycled-slot entities that Deleted() alone can miss.
        if (TerminatingOrDeleted(uid))
            return;

        // TryComp is null-safe for edge-case entities; HasComp can NRE internally.
        // HTNComponent is the only concrete NPC component (NPCComponent is abstract).
        if (!TryComp<HTNComponent>(uid, out _))
            return;

        // Player-controlled NPCs (possessed mobs) should not be auto-despawned.
        if (TryComp<MindContainerComponent>(uid, out var mindContainer) && mindContainer.HasMind)
            return;

        var despawn = EnsureComp<TimedDespawnComponent>(uid);
        if (despawn.Lifetime < NpcCorpseLifetime)
            despawn.Lifetime = NpcCorpseLifetime;
    }
}
