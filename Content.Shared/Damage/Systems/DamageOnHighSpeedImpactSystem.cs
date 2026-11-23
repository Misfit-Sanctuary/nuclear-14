using Content.Shared.Stunnable;
using Content.Shared.Damage.Components;
using Content.Shared.Effects;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Physics.Events;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Content.Shared.Voting;
using Robust.Shared.Physics.Systems;

namespace Content.Shared.Damage.Systems;

public sealed partial class DamageOnHighSpeedImpactSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private IRobustRandom _robustRandom = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedColorFlashEffectSystem _color = default!;
    [Dependency] private SharedStunSystem _stun = default!;
    [Dependency] private SharedPhysicsSystem _phys = default!;
    [Dependency] private SharedTransformSystem _xform = default!;
    [Dependency] private INetManager _net = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<DamageOnHighSpeedImpactComponent, StartCollideEvent>(HandleCollide);
        SubscribeLocalEvent<EffectOnCollideComponent, StartCollideEvent>(OnHurtCollide);
    }
    /// <summary>
    /// "our" being the one doing the colliding effect. Other the one being ran over
    /// roughly based on what monolith repo did for shuttle collision tho also just youtubed stuff from there too
    /// https://www.youtube.com/watch?v=HvuAq7iaRHY
    /// homework
    /// https://openstax.org/books/physics/pages/8-1-linear-momentum-force-and-impulse
    /// thinking of world normal being the "contact" vector when our and other body collide
    /// </summary>
    private void OnHurtCollide(Entity<EffectOnCollideComponent> ent, ref StartCollideEvent args)
    {
        if (!args.OtherFixture.Hard && ent.Comp.CollideHardOnly
            || !args.OurBody.CanCollide || !args.OtherBody.CanCollide)
            return;
        var ourSpeed = args.OurBody.LinearVelocity;
        if (ourSpeed.Length() < ent.Comp.MinimumSpeed || _gameTiming.CurTime < ent.Comp.NextHit)
            return;
        ent.Comp.NextHit = _gameTiming.CurTime + ent.Comp.CollideCooldown;

        var otherSpeed = args.OtherBody.LinearVelocity;
        var ourPos = _xform.GetWorldPosition(args.OurEntity);
        var otherPos = _xform.GetWorldPosition(args.OtherEntity);
        var velocityDirDiff = (ourSpeed - otherSpeed);
        var velocityMagDiff = velocityDirDiff.Length();
        // can be seen as by what % does the point of contact match up with directions our and other were going
        // (car lightly side bumping vs direct collision)
        var dotProduct = MathF.Abs(Vector2.Dot(velocityDirDiff.Normalized(), args.WorldNormal.Normalized()));
        var impactVelocity = velocityMagDiff * dotProduct;

        // not done very wip
        var ourIntertia = args.OurBody.Mass*ourSpeed.Length();
        var otherInertia = args.OtherBody.Mass*otherSpeed.Length();
        if (impactVelocity < ent.Comp.MinimumImpactVelocity)
            return;



        if (_net.IsServer && ent.Comp.DamageOnCollide) // debug error if client predicts dmg. todo fix damage
        {
            var damageScale = ent.Comp.SpeedDamageFactor * ourSpeed;
            _damageable.TryChangeDamage(args.OtherEntity, ent.Comp.Damage * damageScale.Length()); // very wip change later
        }
        if (ent.Comp.StunOnCollide && _robustRandom.Prob(ent.Comp.StunChance))
        {
            _stun.TryStun(args.OtherEntity, TimeSpan.FromSeconds(ent.Comp.StunSeconds), true);
        }
        if (ent.Comp.PushOnCollide)
        {
            var dir = _xform.GetMapCoordinates(ent).Position - _xform.GetMapCoordinates(args.OtherEntity).Position;
            _phys.ApplyForce(args.OtherEntity, dir);
        }
    }
    private void HandleCollide(EntityUid uid, DamageOnHighSpeedImpactComponent component, ref StartCollideEvent args)
    {
        if (!args.OurFixture.Hard || !args.OtherFixture.Hard)
            return;

        if (!HasComp<DamageableComponent>(uid))
            return;

        var speed = args.OurBody.LinearVelocity.Length();

        if (speed < component.MinimumSpeed)
            return;

        if (component.LastHit != null
            && (_gameTiming.CurTime - component.LastHit.Value).TotalSeconds < component.DamageCooldown)
            return;

        component.LastHit = _gameTiming.CurTime;

        if (_robustRandom.Prob(component.StunChance))
            _stun.TryStun(uid, TimeSpan.FromSeconds(component.StunSeconds), true);

        var damageScale = component.SpeedDamageFactor * speed / component.MinimumSpeed;

        _damageable.TryChangeDamage(uid, component.Damage * damageScale);

        if (_gameTiming.IsFirstTimePredicted)
            _audio.PlayPvs(component.SoundHit, uid, AudioParams.Default.WithVariation(0.125f).WithVolume(-0.125f));
        _color.RaiseEffect(Color.Red, new List<EntityUid>() { uid }, Filter.Pvs(uid, entityManager: EntityManager));
    }

    public void ChangeCollide(EntityUid uid, float minimumSpeed, float stunSeconds, float damageCooldown, float speedDamage, DamageOnHighSpeedImpactComponent? collide = null)
    {
        if (!Resolve(uid, ref collide, false))
            return;

        collide.MinimumSpeed = minimumSpeed;
        collide.StunSeconds = stunSeconds;
        collide.DamageCooldown = damageCooldown;
        collide.SpeedDamageFactor = speedDamage;
        Dirty(uid, collide);
    }
}
