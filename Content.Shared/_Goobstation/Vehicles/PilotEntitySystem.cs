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
public partial class PilotEntitySystem
{

    [Dependency] private SharedBuckleSystem _buckle = default!;
    [Dependency] private SharedMoverController _mover = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<DriveOnBuckleComponent, ComponentRemove>(OnRemove);

        CompListeners();
        DriveOnBuckleHandles();
    }

    private void OnRemove(EntityUid uid, DriveOnBuckleComponent component, ComponentRemove args)
    {
        if (component.Pilots.Count == 0)
            return;
        UnpilotAll((uid, component));
    }

    private void Unpilot(Entity<DriveOnBuckleComponent> car,EntityUid pilot)
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

    private bool TryUnpilot(Entity<DriveOnBuckleComponent> car, EntityUid pilot)
    {
        if (!ShouldUnpilot(car, pilot))
            return false;

        Unpilot(car, pilot);
        return true;
    }
    private bool TryPilot(Entity<DriveOnBuckleComponent> car, EntityUid pilot)
    {
        if (!ShouldPilot(car, pilot))
            return false;

        SetPilot(car, pilot);
        return true;
    }


    private bool ShouldUnpilot(Entity<DriveOnBuckleComponent> car, EntityUid pilot)
    {
        InputRelayTrySetOffEv trySetOffEv = new(pilot);

        RaiseLocalEvent(car ,ref trySetOffEv);
        RaiseLocalEvent(pilot ,ref trySetOffEv);

        return !trySetOffEv.Cancel;
    }
    private bool ShouldPilot(Entity<DriveOnBuckleComponent> car, EntityUid pilot)
    {
        if (car.Comp.Pilots.Count >= car.Comp.MaxPilots)
            return false;

        InputRelayTrySetEv trySetEv = new(pilot);

        RaiseLocalEvent(car ,ref trySetEv);
        RaiseLocalEvent(pilot ,ref trySetEv);

        return !trySetEv.Cancel;
    }


    /// <summary>
    /// force pilot/unpilot all
    /// </summary>
    private void PilotAll(Entity<DriveOnBuckleComponent> car)
    {
        var buckled = Comp<StrapComponent>(car).BuckledEntities.ToList();
        var num = Math.Min(buckled.Count,car.Comp.MaxPilots);
        for (var i = 0; i < num; i++)
            SetPilot(car, buckled[i]);
    }
    private void UnpilotAll(Entity<DriveOnBuckleComponent> car)
    {
        var max = car.Comp.Pilots.Count;
        for (var i = max - 1; i >= 0; i--)
        {
            var pilot = car.Comp.Pilots[i];
            Unpilot(car, pilot);
        }
    }
    /// <summary>
    ///  Try (un)Piloting everyone strapped to ent
    /// </summary>
    private void TryPilotAll(Entity<DriveOnBuckleComponent> car)
    {
        var buckled = Comp<StrapComponent>(car).BuckledEntities.ToList();
        var num = Math.Min(buckled.Count,car.Comp.MaxPilots);
        for (var i = 0; i < num; i++)
            TryPilot(car, buckled[i]);
    }
    private void TryUnpilotAll(Entity<DriveOnBuckleComponent> car)
    {
        var max = car.Comp.Pilots.Count;
        for (var i = max - 1; i >= 0; i--)
        {
            var pilot = car.Comp.Pilots[i];
            TryUnpilot(car, pilot);
        }
    }
}


