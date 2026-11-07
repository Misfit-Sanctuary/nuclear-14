using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using Content.Shared.Hands;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;



namespace Content.Shared.Vehicles;
[ByRefEvent]
public record struct InputRelaySetEv(EntityUid Driver, EntityUid Vehicle, bool Handled = false);

[ByRefEvent]
public record struct InputRelayRemovedEv(EntityUid Driver, EntityUid Vehicle, bool Handled = false);

[ByRefEvent]
public record struct InputRelayTrySetEv(EntityUid Driver, bool  Cancel = false);
[ByRefEvent]
public record struct InputRelayTrySetOffEv(EntityUid Driver, bool  Cancel = false);
/// <summary>
/// requires strap/buckle components
/// </summary>
public partial class InputRelayControllerSystem
{

    [Dependency] private SharedBuckleSystem _buckle = default!;
    [Dependency] private SharedMoverController _mover = default!;
    private static InputRelayTrySetEv _trySetEv;
    private static InputRelaySetEv _setEv;
    private static InputRelayRemovedEv _removedEv;
    private static InputRelayTrySetOffEv _trySetOffEv;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<DriveOnBuckleComponent, ComponentRemove>(OnRemove);

        CompListeners();
        EventCallBacks();
    }

    private void OnRemove(EntityUid uid, DriveOnBuckleComponent component, ComponentRemove args)
    {
        if (component.Pilots.Count == 0)
            return;
        RemoveAllPilots((uid, component));
    }



    private bool TryPilot(Entity<DriveOnBuckleComponent> car, EntityUid pilot)
    {
        if (!ShouldPilot(car, pilot))
            return false;

        SetPilot(car, pilot);
        return true;
    }

    private void TryPilotAllStrapped(Entity<DriveOnBuckleComponent> car)
    {
        var buckled = Comp<StrapComponent>(car).BuckledEntities.ToList();
        var num = Math.Min(buckled.Count,car.Comp.MaxPilots);
        for (var i = 0; i < num; i++)
            TryPilot(car, buckled[i]);
    }
    private bool TryUnPilot(Entity<DriveOnBuckleComponent> car, EntityUid pilot)
    {
        if (!ShouldUnpilot(car, pilot))
            return false;

        StopPiloting(car, pilot);
        return true;
    }

    private bool ShouldUnpilot(Entity<DriveOnBuckleComponent> car, EntityUid pilot)
    {
        InputRelayTrySetOffEv trySetOffEv = new(pilot);

        RaiseLocalEvent(car ,ref trySetOffEv);
        if (trySetOffEv.Cancel)
            return false;

        RaiseLocalEvent(pilot ,ref trySetOffEv);
        if (trySetOffEv.Cancel)
            return false;

        return true;
    }
    private bool ShouldPilot(Entity<DriveOnBuckleComponent> car, EntityUid pilot)
    {
        if (car.Comp.Pilots.Count >= car.Comp.MaxPilots)
            return false;

        InputRelayTrySetEv trySetEv = new(pilot);

        RaiseLocalEvent(car ,ref trySetEv);
        if (trySetEv.Cancel)
            return false;

        RaiseLocalEvent(pilot ,ref trySetEv);
        if (trySetEv.Cancel)
            return false;

        return true;
    }
    private void StopPiloting(Entity<DriveOnBuckleComponent> car,EntityUid pilot)
    {
        RemComp<RelayInputMoverComponent>(pilot);
        car.Comp.Pilots.Remove(pilot);
        InputRelayRemovedEv removedEv = new(pilot, car);
        RaiseLocalEvent(car ,ref removedEv);
        RaiseLocalEvent(pilot ,ref removedEv);
    }
    private void SetPilot(Entity<DriveOnBuckleComponent> car,EntityUid pilot)
    {
        _mover.SetRelay(pilot, car);
        car.Comp.Pilots.Add(pilot);
        InputRelaySetEv setEv = new(pilot, car);
        RaiseLocalEvent(car ,ref setEv);
        RaiseLocalEvent(pilot ,ref setEv);

    }

    private void RemoveAllPilots(Entity<DriveOnBuckleComponent> car)
    {
        var max = car.Comp.Pilots.Count;
        for (var i = max - 1; i >= 0; i--)
        {
            var pilot = car.Comp.Pilots[i];
            StopPiloting(car, pilot);
        }
    }
    private void TryRemoveAllPilots(Entity<DriveOnBuckleComponent> car)
    {
        var max = car.Comp.Pilots.Count;
        for (var i = max - 1; i >= 0; i--)
        {
            var pilot = car.Comp.Pilots[i];
            TryUnPilot(car, pilot);
        }
    }
}


