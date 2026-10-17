using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using Content.Shared.Hands;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;



namespace Content.Shared.Vehicles;
[ByRefEvent]
public record struct InputRelaySetEv(EntityUid Driver, EntityUid Vehicle);

[ByRefEvent]
public record struct InputRelayRemovedEv(EntityUid Driver, EntityUid Vehicle);

[ByRefEvent]
public record struct InputRelayTrySetEv(EntityUid Driver, bool  Cancel = false);
/// <summary>
/// requires strap/buckle
/// </summary>
public partial class InputRelayControllerSystem : EntitySystem
{

    [Dependency] private SharedBuckleSystem _buckle = default!;
    [Dependency] private SharedMoverController _mover = default!;
    private static InputRelayTrySetEv _trySetEv;
    private static InputRelaySetEv _setEv;
    private static InputRelayRemovedEv _removedEv;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<DriveOnBuckleComponent, ComponentRemove>(OnRemove);
        SubscribeLocalEvent<DriveOnBuckleComponent, StrappedEvent>(OnStrapped);
        SubscribeLocalEvent<DriveOnBuckleComponent, UnstrappedEvent>(OnUnstrapped);
        SubscribeLocalEvent<DriveOnBuckleComponent, VirtualItemDeletedEvent>(OnDropped);

        CompListeners();


    }

    private static void OnRemove(EntityUid uid, DriveOnBuckleComponent component, ComponentRemove args)
    {
        if (component.Pilots.Count == 0)
            return;
        component.Pilots.Clear();
    }

    private void OnStrapped(Entity<DriveOnBuckleComponent> car, ref StrappedEvent args)
    {
        if (car.Comp.Pilots.Count >= car.Comp.MaxPilots)
            return;
        var pilot = args.Buckle;

        _trySetEv = new(pilot);

        RaiseLocalEvent(car ,ref _trySetEv);
        if (_trySetEv.Cancel)
            return;
        RaiseLocalEvent(pilot ,ref _trySetEv);
        if (_trySetEv.Cancel)
            return;

        _mover.SetRelay(pilot, car);
        car.Comp.Pilots.Add(pilot);
        _setEv = new(pilot, car);
        RaiseLocalEvent(car ,ref _setEv);
        RaiseLocalEvent(pilot ,ref _setEv);
    }


    private void OnUnstrapped(Entity<DriveOnBuckleComponent> car, ref UnstrappedEvent args)
    {
        if (!car.Comp.Pilots.Contains(args.Buckle.Owner))
            return;

        var pilot = args.Buckle;
        RemComp<RelayInputMoverComponent>(pilot);
        car.Comp.Pilots.Remove(pilot);
        _removedEv = new(pilot, car);
        RaiseLocalEvent(car ,ref _removedEv);
        RaiseLocalEvent(pilot ,ref _removedEv);
    }

    private void OnDropped(EntityUid car, DriveOnBuckleComponent comp, VirtualItemDeletedEvent args)
    {
        //misfits: edge case discovered during server shutdown that virtual item deletion(_virtualItem.DeleteInHandsMatching()) causes event chain where TryDrop is still called
        //          this results in an infinite throwing event loop maybe because of vehicle entity being raised on is in the middle of deletion
         if (TerminatingOrDeleted(car))
            return;
         var dismounter = args.User;
         var buckle = (dismounter, Comp<BuckleComponent>(dismounter));
         var strap = (uid: car,Comp<StrapComponent>(car));

         _buckle.Unbuckle(buckle, strap, dismounter);
    }

}


