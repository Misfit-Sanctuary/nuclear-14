using Content.Shared.Buckle.Components;
using Content.Shared.Interaction.Events;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Weapons.Ranged.Components;


namespace Content.Shared.Buckle;

/// <summary>
/// Misfits addition.
/// This isn't its own component and system since this logic is already obviously coupled to strap comp
/// I imagine that being different if projectiles get a strapComp for some reason
/// (maybe bulldozed/carried by a big bullet loon toons style lol?)
/// </summary>
public abstract partial class SharedBuckleSystem
{
    private void InitFriendlyFire()
    {
        SubscribeLocalEvent<StrapComponent, ProjectilePreventCollideEvent>(OnBeforeProjCollideStrap);
        SubscribeLocalEvent<BuckleComponent, ProjectilePreventCollideEvent>(OnBeforeProjCollideBuckle);

        SubscribeLocalEvent<StrapComponent, GettingAttackedAttemptEvent>(OnMeleeAttemptStrap);
        SubscribeLocalEvent<BuckleComponent, GettingAttackedAttemptEvent>(OnMeleeAttemptBuckle);
    }
    /// <summary>
    /// assume the strap ent is not the projectile duh
    /// Do not collide any projectile from buckler with ride
    /// </summary>
    private void OnBeforeProjCollideStrap(Entity<StrapComponent> ent, ref ProjectilePreventCollideEvent ev)
    {
        if (!ent.Comp.FriendlyFire || ev.Cancelled || ev.Projectile.Comp.Shooter is null)
            return;

        ev.Cancelled = ent.Comp.BuckledEntities.Contains(ev.Projectile.Comp.Shooter.Value);


    }
    /// <summary>
    /// assume the buckler ent is not the projectile duh
    /// Bucklers(aka riders) cannot friendly fire eachother if on same ride
    /// unless they directly click on eachother
    /// </summary>
    private void OnBeforeProjCollideBuckle(Entity<BuckleComponent> ent, ref ProjectilePreventCollideEvent ev)
    {
        if (ev.Cancelled || ev.Projectile.Comp.Shooter is null || ent.Comp.BuckledTo is null)
            return;
        var strap = Comp<StrapComponent>(ent.Comp.BuckledTo.Value);
        var shooter = ev.Projectile.Comp.Shooter.Value; // hate nullables
        if (!strap.FriendlyFire || !strap.BuckledEntities.Contains(shooter))
            return;

        // TargetedProjectileComponent is put on an projectile if you DIRECTLY CLICK on someone
        ev.Cancelled = !TryComp<TargetedProjectileComponent>(ev.Projectile, out var compTarget) ||
                       ev.OtherEntity != compTarget.Target;

    }
    private void OnMeleeAttemptStrap(Entity<StrapComponent> ent, ref GettingAttackedAttemptEvent ev)
    {
        if (!ent.Comp.FriendlyFire || ev.Cancelled || !ent.Comp.BuckledEntities.Contains(ev.Attacker))
            return;

        switch (ev.AttackType)
        {
            case HeavyAttackEvent:
                ev.Cancelled = true;
                return;
            default:
                return;
        }

    }
    private void OnMeleeAttemptBuckle(Entity<BuckleComponent> ent, ref GettingAttackedAttemptEvent ev)
    {

        if (ev.Cancelled || ent.Comp.BuckledTo is null)
            return;
        var strap = Comp<StrapComponent>(ent.Comp.BuckledTo.Value);
        if (!strap.FriendlyFire || !strap.BuckledEntities.Contains(ev.Attacker))
            return;

        switch (ev.AttackType)
        {
            case HeavyAttackEvent:
                ev.Cancelled = true;
                return;
            default:
                return;
        }
    }

}
