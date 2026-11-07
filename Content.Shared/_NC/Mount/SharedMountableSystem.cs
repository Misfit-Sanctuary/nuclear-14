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

        // #Misfits Fix — subscribe to the MOUNT's MoveEvent, not the rider's.
        // The rider is parented to the mount after buckle, so modifying rider
        // position from the rider's own MoveEvent used the wrong coordinate space
        // (mountXform.LocalPosition + offset treated as local-to-mount) and
        // triggered BuckleTransformCheck, causing instant unbuckle & teleports.
        //SubscribeLocalEvent<MountableComponent, MoveEvent>(OnMountMove);
        SubscribeLocalEvent<RiderComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<MountableComponent, MeleeHitEvent>(OnMeleeHit);

        SubscribeLocalEvent<MountableComponent, DownAttemptEvent>(OnDownAttempt);
        //SubscribeLocalEvent<MountableComponent, StrappedEvent>(OnStrapped);
        SubscribeLocalEvent<MountableComponent, InputRelayTrySetEv>(OnTryRelaySet);
        SubscribeLocalEvent<MountableComponent, InputRelaySetEv>(OnRelaySet);
        //SubscribeLocalEvent<MountableComponent, UnstrappedEvent>(OnUnstrapped);
        SubscribeLocalEvent<MountableComponent, InputRelayRemovedEv>(OnInputRelayRemoved);

        SubscribeLocalEvent<MountableComponent, MobStateChangedEvent>(OnMobStateChanged);
    }

    /// <summary>
    /// Responsible for shifting the rider when the mount rotates.
    /// The rider is parented to the mount, so LocalPosition is relative to mount.
    /// We only need the direction-dependent offset, not the mount's grid position.
    /// </summary>
    private void OnMountMove(Entity<MountableComponent> ent, ref MoveEvent args)
    {
        // #Misfits Fix — only update rider offset when mount rotation changes
        if (args.NewRotation == args.OldRotation || ent.Comp.Rider is not { } rider)
            return;

        _buckleOffset.SnapToOffset(ent, rider, GetRiderLocalOffset(ent.Comp, args.NewRotation));
    }

    // parented to mount
    public static Vector2 GetRiderLocalOffset(MountableComponent comp, Angle mountRotation)
    {
        var direction = SpriteFacing.GetCardinalDir(mountRotation);
        var offset = comp.RiderOffset + comp.DirectionOffsets.GetValueOrDefault(direction, Vector2.Zero);
        return (-mountRotation).RotateVec(offset);
    }
    private void OnMeleeHit(Entity<MountableComponent> ent, ref MeleeHitEvent args)
    {
        if (args.User == ent.Comp.Rider) // Don't hit your own horse
            args.Handled = true;
    }
    /// Misfits: Leaving this unaltered since brahdos dont have a down sprite or state yet
    /// <summary>
    /// Checking that you can't drop a mount with a rider
    /// </summary>
    private void OnDownAttempt(Entity<MountableComponent> ent, ref DownAttemptEvent args)
    {
        if (ent.Comp.Rider != null)
            args.Cancel();
    }

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
    /// The method of attaching a rider to a mount
    /// </summary>
    // private void OnStrapped(Entity<MountableComponent> ent, ref StrappedEvent args)
    // {
    //
    //     var rider = EnsureComp<RiderComponent>(args.Buckle.Owner);
    //     rider.Mount = ent.Owner;
    //     ent.Comp.Rider = args.Buckle.Owner;
    //
    //     Dirty(ent.Owner, ent.Comp);
    //     Dirty(args.Buckle.Owner, rider);
    //
    //     //_buckleOffset.SnapToOffset(ent, args.Buckle.Owner, GetRiderLocalOffset(ent.Comp, Transform(ent).LocalRotation));
    //
    //     var controlAttempt = new MountMovementControlAttemptEvent(args.Buckle.Owner);
    //     RaiseLocalEvent(ent.Owner, ref controlAttempt);
    //     ent.Comp.RiderControlsMovement = ent.Comp.ControlMovement && !controlAttempt.Cancelled;
    //     ent.Comp.HadActiveNpcBeforeMount = HasComp<ActiveNPCComponent>(ent);
    // }
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
    // /// <summary>
    // /// Unties and removes the rider
    // /// </summary>
    // private void OnUnstrapped(Entity<MountableComponent> ent, ref UnstrappedEvent args)
    // {
    //
    //
    //     RemComp<RiderComponent>(args.Buckle.Owner);
    //     ent.Comp.Rider = null;
    //     var riderControlledMovement = ent.Comp.RiderControlsMovement;
    //     ent.Comp.RiderControlsMovement = false;
    //
    //     Dirty(ent.Owner, ent.Comp);
    //
    //     if (ent.Comp.HadActiveNpcBeforeMount)
    //         EnsureComp<ActiveNPCComponent>(ent);
    //
    //     ent.Comp.HadActiveNpcBeforeMount = false;
    //     if (!TryComp<MovementSpeedModifierComponent>(ent, out var move))
    //         return;
    //
    //     if (!riderControlledMovement)
    //         return;
    //
    //     var walk = move.BaseWalkSpeed / ent.Comp.DefaultSpeed;
    //     var sprint = move.BaseSprintSpeed / ent.Comp.DefaultSpeed;
    //     _movement.ChangeBaseSpeed(ent, walk, sprint, move.Acceleration, move);
    // }

    /// <summary>
    /// Resets the rider if he starts to die
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
