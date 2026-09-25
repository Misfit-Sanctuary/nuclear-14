using Content.Server.Explosion.EntitySystems;
using Content.Server.Speech.EntitySystems;
using Content.Server.Stunnable;
using Content.Shared._Misfits.PowerArmor;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Prototypes;

namespace Content.Server._Misfits.LungeMine;

public sealed class LungeMineSystem : EntitySystem
{
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly StunSystem _stun = default!;
    [Dependency] private readonly StutteringSystem _stutter = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly TriggerSystem _trigger = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<LungeMineComponent, MeleeHitEvent>(OnMeleeHit);
    }

    private void OnMeleeHit(Entity<LungeMineComponent> ent, ref MeleeHitEvent args)
    {
        if (!args.IsHit)
            return;

        foreach (var target in args.HitEntities)
        {
            if (target == args.User)
                continue;

            _audio.PlayPvs(ent.Comp.TriggerSound, ent);
            _transform.SetCoordinates(ent, Transform(target).Coordinates);
            _trigger.Trigger(ent, args.User);

            Daze(target, ent.Comp.DazeDuration);
            Daze(args.User, ent.Comp.DazeDuration);
            DamageWornArmor(target, ent.Comp.ArmorDamage);
            return;
        }
    }

    private void Daze(EntityUid uid, TimeSpan duration)
    {
        _stun.TryStun(uid, duration, true);
        _stutter.DoStutter(uid, duration, true);
    }

    private void DamageWornArmor(EntityUid target, float armorDamage)
    {
        if (!TryComp<PowerArmorWornComponent>(target, out var worn) || !Exists(worn.Armor))
            return;

        var structural = _prototypes.Index<DamageTypePrototype>("Structural");
        _damageable.TryChangeDamage(
            worn.Armor,
            new DamageSpecifier(structural, FixedPoint2.New(armorDamage)),
            ignoreResistances: true,
            origin: target);
    }
}
