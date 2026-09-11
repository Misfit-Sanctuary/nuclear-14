using Content.Server.Explosion.EntitySystems;
using Content.Shared._Misfits.Weapons.Throwable;
using Content.Shared.Buckle.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Physics;
using Content.Shared.Vehicles;
using Content.Shared.Examine;
using Content.Shared.Interaction.Events;
using Content.Shared.Popups;
using Content.Shared.Throwing;
using Content.Shared.Trigger;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Events;

namespace Content.Server._Misfits.Weapons.Throwable;

/// <summary>Manual safety and impact triggering for impact grenades.</summary>
public sealed class ImpactGrenadeSystem : EntitySystem
{
    [Dependency] private readonly TriggerSystem _trigger = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;

    private static readonly SoundSpecifier PrimeSound =
        new SoundPathSpecifier("/Audio/Items/smoke_grenade_prime.ogg");

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ImpactGrenadeComponent, UseInHandEvent>(OnUse);
        SubscribeLocalEvent<ImpactGrenadeComponent, ExaminedEvent>(OnExamine);
        SubscribeLocalEvent<ImpactGrenadeComponent, ThrowDoHitEvent>(OnHit);
        SubscribeLocalEvent<ImpactGrenadeComponent, ThrownEvent>(OnThrown);
        SubscribeLocalEvent<ImpactGrenadeComponent, PhysicsSleepEvent>(OnSleep);
        SubscribeLocalEvent<ImpactGrenadeComponent, StartCollideEvent>(OnCollide);
    }

    private void OnUse(Entity<ImpactGrenadeComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        if (_trigger.TryPacifiedBlockArm(ent, args.User))
            return;

        if (ent.Comp.Armed)
        {
            _popup.PopupEntity(Loc.GetString("impact-grenade-already-armed"), ent, args.User);
            return;
        }

        ent.Comp.Armed = true;
        Dirty(ent);
        if (TryComp<AppearanceComponent>(ent, out var appearance))
            _appearance.SetData(ent, TriggerVisuals.VisualState, TriggerVisualState.Primed, appearance);
        _audio.PlayPvs(PrimeSound, ent.Owner);
        _popup.PopupEntity(Loc.GetString("impact-grenade-armed"), ent, args.User, PopupType.SmallCaution);
    }

    private void OnExamine(Entity<ImpactGrenadeComponent> ent, ref ExaminedEvent args)
    {
        args.PushText(Loc.GetString(ent.Comp.Armed ? "impact-grenade-examine-armed" : "impact-grenade-examine-safe"));
    }

    private void OnHit(Entity<ImpactGrenadeComponent> ent, ref ThrowDoHitEvent args)
    {
        if (args.Target == ent.Comp.Thrower || args.Target == ent.Comp.ThrowerMount)
            return;

        Detonate(ent, args.Component.Thrower);
    }

    private void OnThrown(Entity<ImpactGrenadeComponent> ent, ref ThrownEvent args)
    {
        ent.Comp.WasThrown = ent.Comp.Armed;
        ent.Comp.Thrower = args.User;
        ent.Comp.ThrowerMount = args.User is { } user && TryComp<BuckleComponent>(user, out var buckle)
            ? buckle.BuckledTo
            : null;
    }

    private void OnSleep(Entity<ImpactGrenadeComponent> ent, ref PhysicsSleepEvent args)
    {
        // LandEvent AND StopThrowEvent can occur while the item is still moving.
        // For an unobstructed throw, wait until its physical motion actually ends.
        if (ent.Comp.WasThrown)
            Detonate(ent, ent.Comp.Thrower);
    }

    private void OnCollide(Entity<ImpactGrenadeComponent> ent, ref StartCollideEvent args)
    {
        if (!ent.Comp.WasThrown || args.OtherEntity == ent.Comp.Thrower ||
            args.OtherEntity == ent.Comp.ThrowerMount)
            return;

        // The persistent, soft impact fixture survives removal of the temporary
        // throwing fixture. Never require OurFixture.Hard: sensors are soft.
        // Mob/vehicle body fixtures may also be soft (e.g. prone or mounted mobs).
        // Restrict soft targets to their bullet-collidable body, not vision sensors.
        var bodyContact = (args.OtherFixture.CollisionLayer & (int) CollisionGroup.BulletImpassable) != 0;
        var targetBody = bodyContact && (HasComp<MobStateComponent>(args.OtherEntity) ||
                                        HasComp<VehicleComponent>(args.OtherEntity));
        if (args.OtherFixture.Hard || targetBody)
            Detonate(ent, ent.Comp.Thrower);
    }

    private void Detonate(Entity<ImpactGrenadeComponent> ent, EntityUid? user)
    {
        if (!ent.Comp.Armed || ent.Comp.Triggered)
            return;

        ent.Comp.Triggered = true;
        _trigger.Trigger(ent, user);
    }
}
