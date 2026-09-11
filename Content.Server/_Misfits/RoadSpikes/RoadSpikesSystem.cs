using Content.Server.Stunnable;
using Content.Shared.Buckle;
using Content.Shared.Damage;
using Content.Shared.Popups;
using Content.Shared.Vehicles;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Physics.Events;

namespace Content.Server._Misfits.RoadSpikes;

/// <summary>
/// Handles the vehicle-specific response when a motorbike crosses road spikes.
/// </summary>
public sealed class RoadSpikesSystem : EntitySystem
{
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedBuckleSystem _buckle = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly StunSystem _stun = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RoadSpikesComponent, StartCollideEvent>(OnStartCollide);
    }

    private void OnStartCollide(Entity<RoadSpikesComponent> ent, ref StartCollideEvent args)
    {
        if (!TryHitMotorbike(ent, args.OtherEntity))
            return;

        // Consume the spikes here just as the normal shard trigger does for pedestrians.
        QueueDel(ent);
    }

    private bool TryHitMotorbike(Entity<RoadSpikesComponent> ent, EntityUid bike)
    {
        if (!HasComp<MotorbikeComponent>(bike) ||
            !TryComp<VehicleComponent>(bike, out var vehicle))
        {
            return false;
        }

        if (ent.Comp.Triggered)
            return false;

        ent.Comp.Triggered = true;
        _damageable.TryChangeDamage(bike, ent.Comp.BikeDamage, origin: ent);
        _audio.PlayPvs(ent.Comp.ImpactSound, bike);

        if (vehicle.Driver is not { } rider)
            return true;

        _popup.PopupEntity(Loc.GetString(ent.Comp.RiderWarning), bike, rider, PopupType.LargeCaution);
        _buckle.Unbuckle((rider, null), null);
        _stun.TryKnockdown(rider, ent.Comp.RiderKnockdownTime, refresh: true);
        return true;
    }
}
