// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Bed.Sleep;
using Content.Shared._Misfits.Genetics.Mutations;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Shared.Timing;


namespace Content.Shared._Misfits.Mobs;

[RegisterComponent, NetworkedComponent]
public sealed partial class AwakeMobComponent : Component;

public sealed class AwakeMobSystem : EntitySystem
{
    [Dependency] private IGameTiming _time = default!;
    [Dependency] private INetManager _net = default!;
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MobStateChangedEvent>(OnStateChanged);
        SubscribeLocalEvent<MutatableComponent, SleepStateChangedEvent>(OnSleepStateChanged);
    }

    private void OnStateChanged(MobStateChangedEvent args) => Refresh(args.Target, args.NewMobState);
    private void OnSleepStateChanged(Entity<MutatableComponent> ent, ref SleepStateChangedEvent args)
    {

        if (args.FellAsleep)
            RemComp<AwakeMobComponent>(ent);
        else if (TryComp<MobStateComponent>(ent, out var state) && state.CurrentState == MobState.Alive)
            EnsureComp<AwakeMobComponent>(ent);
    }

    private void Refresh(EntityUid uid, MobState state)
    {
        // Misfits:
        // Melee clientside seems to predict damage which also predicts mob state change(crit, alive, dead)
        // when predicted entities get reset, damageComp handler calls TryChangeDamage which results in these
        // comps being changed. Call from handler probably not needed
        // Not sure why it's done like that or even if it has been like that(didnt raise debug error before I think).
        // Will try to fix underlying issue in future
        /// <see cref="Robust.Client.GameStates.ClientGameStateManager.ResetPredictedEntities"/>
        /// <see cref="DamageableSystem.DamageableHandleState"/>
        /// <see cref="ClientGameTiming.StartStateApplication"/>
        if (_time.ApplyingState && !_time.IsFirstTimePredicted)
            return;
        if (state == MobState.Alive && !HasComp<SleepingComponent>(uid))
            EnsureComp<AwakeMobComponent>(uid);
        else
            RemComp<AwakeMobComponent>(uid);
    }
}
