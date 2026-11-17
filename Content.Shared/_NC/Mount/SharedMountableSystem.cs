using Content.Shared._Misfits.Buckle;
using Content.Shared._NC.Mountable.Components;
using Content.Shared.Buckle;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.NPC;
using Content.Shared.Standing;
using Content.Shared.Vehicles;
using Content.Shared.Weapons.Melee.Events;


namespace Content.Shared._NC.Mountable;

/// <summary>
/// The system responsible for mount management
/// </summary>
public sealed class SharedMountSystem : EntitySystem
{
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SharedBuckleSystem _buckle = default!;
    [Dependency] private readonly SharedMoverController _mover = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movement = default!;
    [Dependency] private readonly StandingStateSystem _standing = default!;
    [Dependency] private readonly DirectionalBuckleOffsetSystem _buckleOffset = default!;

    public override void Initialize()
    {
        base.Initialize();


        //SubscribeLocalEvent<MountableComponent, MoveEvent>(OnMountMove);
        SubscribeLocalEvent<RiderComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<MountableComponent, MeleeHitEvent>(OnMeleeHit);

        //SubscribeLocalEvent<MountableComponent, DownAttemptEvent>(OnDownAttempt);
        //SubscribeLocalEvent<MountableComponent, StrappedEvent>(OnStrapped);
        SubscribeLocalEvent<MountableComponent, InputRelayTrySetEv>(OnTryRelaySet);
        SubscribeLocalEvent<MountableComponent, InputRelaySetEv>(OnRelaySet);
        //SubscribeLocalEvent<MountableComponent, UnstrappedEvent>(OnUnstrapped);
        SubscribeLocalEvent<MountableComponent, InputRelayRemovedEv>(OnInputRelayRemoved);

        SubscribeLocalEvent<MountableComponent, MobStateChangedEvent>(OnMobStateChanged);
    }




    private void OnMeleeHit(Entity<MountableComponent> ent, ref MeleeHitEvent args)
    {
        if (args.User == ent.Comp.Rider) // Don't hit your own horse
            args.Handled = true;
    }
    /// Misfits: Leaving this unaltered since brahdos dont have a down sprite or down state yet
    /// <summary>
    /// Checking that you can't drop a mount with a rider
    /// </summary>
    // private void OnDownAttempt(Entity<MountableComponent> ent, ref DownAttemptEvent args)
    // {
    //     if (ent.Comp.Rider != null)
    //         args.Cancel();
    // }

    private void OnTryRelaySet(Entity<MountableComponent> ent, ref InputRelayTrySetEv args)
    {
        if (args.Cancel)
            return;
        if (_mobState.IsDead(ent))
        {
            args.Cancel = true;
            return;
        }

        var controlAttempt = new MountMovementControlAttemptEvent(args.Driver);
        RaiseLocalEvent(ent.Owner, ref controlAttempt);
        var canControl = ent.Comp.ControlMovement && !controlAttempt.Cancelled;
        args.Cancel = !canControl;
    }

    /// <summary>
    /// The method of attaching a rider to a mount
    /// </summary>
    private void OnRelaySet(Entity<MountableComponent> ent, ref InputRelaySetEv args)
    {
        ent.Comp.RiderControlsMovement = true;
        ent.Comp.HadActiveNpcBeforeMount = HasComp<ActiveNPCComponent>(ent);
        RemComp<ActiveNPCComponent>(ent);

        if (_standing.IsDown(ent))
            _standing.Stand(ent);

        EnsureComp<InputMoverComponent>(ent);
        var rider = EnsureComp<RiderComponent>(args.Driver);
        rider.Mount = ent.Owner;
        ent.Comp.Rider = args.Driver;

        Dirty(ent.Owner, ent.Comp);
        Dirty(args.Driver, rider);
    }

    /// <summary>
    /// Unties and removes the rider
    /// </summary>
    private void OnInputRelayRemoved(Entity<MountableComponent> ent, ref InputRelayRemovedEv args)
    {
        RemComp<RiderComponent>(args.Driver);
        ent.Comp.Rider = null;
        ent.Comp.RiderControlsMovement = false;

        if (ent.Comp.HadActiveNpcBeforeMount)
            EnsureComp<ActiveNPCComponent>(ent);

        ent.Comp.HadActiveNpcBeforeMount = false;
        Dirty(ent.Owner, ent.Comp);

    }

    /// <summary>
    /// Unbuckles the rider if crit/dead
    /// </summary>
    private void OnMobStateChanged(Entity<RiderComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Alive)
            return;

        DebugTools.Assert(ent.Comp.Mount != null,"Mount is null but rider still has rider comp");

        _buckle.TryUnbuckle(ent.Owner, ent.Comp.Mount);
    }

    /// <summary>
    /// Resets the rider if the mount starts to die and returns everything as it was if everything is fine with it
    /// </summary>
    private void OnMobStateChanged(Entity<MountableComponent> ent, ref MobStateChangedEvent args)
    {
        if (ent.Comp.Rider != null)
            _buckle.TryUnbuckle(ent.Comp.Rider.Value, ent.Owner);
    }
}

[ByRefEvent]
public record struct MountMovementControlAttemptEvent(EntityUid Rider)
{
    public bool Cancelled;
}
