using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Storage.Components;
using Robust.Shared.Player;

namespace Content.Server._Misfits.Storage;

/// <summary>
/// Prevents living NPC mobs (raiders, animals, etc.) from being inserted into
/// EntityStorage containers such as crates and lockers.
/// Dead mobs are allowed so corpses can be stored in crates for trade contracts.
/// Player-controlled entities (ActorComponent present) are always allowed in,
/// preserving normal gameplay for human players and accepted ghost roles.
/// </summary>
public sealed class NoNpcMobInEntityStorageSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MobStateComponent, InsertIntoEntityStorageAttemptEvent>(OnInsertAttempt);
    }

    private void OnInsertAttempt(EntityUid uid, MobStateComponent component, ref InsertIntoEntityStorageAttemptEvent args)
    {
        // Allow players and inhabited ghost roles through.
        if (HasComp<ActorComponent>(uid))
            return;

        // Allow dead mobs — corpses need to go into crates for trade contracts.
        if (component.CurrentState == MobState.Dead)
            return;

        args.Cancelled = true;
    }
}
