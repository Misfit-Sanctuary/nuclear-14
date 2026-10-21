using Content.Shared.Buckle.Components;
using Content.Shared.Hands;

namespace Content.Shared.Vehicles;

public sealed partial class InputRelayControllerSystem : EntitySystem
{

    private void EventCallBacks()
    {
        SubscribeLocalEvent<DriveOnBuckleComponent, EngineOffEvent>(OnEngineOff);
        SubscribeLocalEvent<DriveOnBuckleComponent, EngineOnEvent>(OnEngineOn);

        SubscribeLocalEvent<DriveOnBuckleComponent, StrappedEvent>(OnStrapped);
        SubscribeLocalEvent<DriveOnBuckleComponent, UnstrappedEvent>(OnUnstrapped);
        SubscribeLocalEvent<DriveOnBuckleComponent, VirtualItemDeletedEvent>(OnDropped);
    }
    private void OnStrapped(Entity<DriveOnBuckleComponent> car, ref StrappedEvent args)
    {
        TryPilot(car,args.Buckle);
    }

    private void OnUnstrapped(Entity<DriveOnBuckleComponent> car, ref UnstrappedEvent args)
    {
        if (!car.Comp.Pilots.Contains(args.Buckle.Owner))
            return;
        var pilot = args.Buckle;
        StopPiloting(car, pilot);
    }

    private void OnDropped(EntityUid car, DriveOnBuckleComponent comp, VirtualItemDeletedEvent args)
    {
        //misfits: edge case discovered during server shutdown that virtual item deletion(_virtualItem.DeleteInHandsMatching()) causes event chain where TryDrop is still called
        //          this results in an infinite throwing event loop maybe because of vehicle entity being raised on is in the middle of deletion
        if (TerminatingOrDeleted(car))
            return;
        var dismounter = args.User;
        _buckle.TryUnbuckle(dismounter, dismounter, false);
    }
    private void OnEngineOff(Entity<DriveOnBuckleComponent> car,  ref EngineOffEvent args)
    {
        TryRemoveAllPilots(car);
    }
    private void OnEngineOn(Entity<DriveOnBuckleComponent> car,  ref EngineOnEvent args)
    {
        TryPilotAllStrapped(car);
    }


}
