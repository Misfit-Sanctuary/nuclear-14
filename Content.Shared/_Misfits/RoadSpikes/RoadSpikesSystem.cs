using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using Content.Shared.Damage;
using Content.Shared.Popups;
using Content.Shared.Stunnable;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Physics.Events;

namespace Content.Shared._Misfits.RoadSpikes;
/// Original iteration by Superspheres for misfits
/// file moved from server to shared for compatability with vehicle refactor
/// <summary>
/// Handles the vehicle-specific response when a motorbike crosses road spikes.
/// </summary>
public sealed partial class RoadSpikesSystem : EntitySystem
{
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedBuckleSystem _buckle = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedStunSystem _stun = default!;

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
        if (!TryComp<StrapComponent>(bike, out var vehicle) || ent.Comp.Triggered)
        {
            return false;
        }

        ent.Comp.Triggered = true;
        _damageable.TryChangeDamage(bike, ent.Comp.BikeDamage, origin: ent);
        _audio.PlayPvs(ent.Comp.ImpactSound, bike);

        if (vehicle.BuckledEntities.Count <= 0)
            return true;
        KnockDownBuckled(vehicle.BuckledEntities,  bike, ent.Comp);
        return true;
    }

    private void KnockDownBuckled(HashSet<EntityUid> ents, EntityUid bike, RoadSpikesComponent spikeComp)
    {
        foreach (var ent in ents)
        {
            _popup.PopupEntity(Loc.GetString(spikeComp.RiderWarning), bike, ent, PopupType.LargeCaution);
            _buckle.TryUnbuckle(ent);
            _stun.TryKnockdown(ent, spikeComp.RiderKnockdownTime, refresh: true);
        }
    }
}
