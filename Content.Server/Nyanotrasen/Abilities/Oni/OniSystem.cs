using Content.Server.Tools;
using Content.Server.Weapons.Ranged.Systems;
using Content.Shared.Humanoid;
using Content.Shared.Tools.Components;
using Content.Shared.Damage.Events;
using Content.Shared.Nyanotrasen.Abilities.Oni;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Wieldable;
using Content.Shared.Contests;
using Robust.Shared.Containers;
using Content.Server._Misfits.Special; // #Misfits Add - ordering dependency

namespace Content.Server.Abilities.Oni
{
    public sealed class OniSystem : SharedOniSystem
    {
        [Dependency] private readonly ToolSystem _toolSystem = default!;
        [Dependency] private readonly GunSystem _gunSystem = default!;

        // #Misfits Add - Track weapons whose health contest we disabled for super mutants.
        private readonly HashSet<EntityUid> _healthContestDisabled = new();

        public override void Initialize()
        {
            base.Initialize();
            SubscribeLocalEvent<OniComponent, EntInsertedIntoContainerMessage>(OnEntInserted);
            SubscribeLocalEvent<OniComponent, EntRemovedFromContainerMessage>(OnEntRemoved);
            SubscribeLocalEvent<MeleeWeaponComponent, GetMeleeDamageEvent>(OnGetMeleeDamage, after: [typeof(WieldableSystem), typeof(SpecialCombatSystem)]);
            SubscribeLocalEvent<HeldByOniComponent, TakeStaminaDamageEvent>(OnStamHit);
        }

        private void OnEntInserted(EntityUid uid, OniComponent component, EntInsertedIntoContainerMessage args)
        {
            var heldComp = EnsureComp<HeldByOniComponent>(args.Entity);
            heldComp.Holder = uid;

            if (TryComp<ToolComponent>(args.Entity, out var tool) && _toolSystem.HasQuality(args.Entity, "Prying", tool))
                _toolSystem.SetSpeedModifier((args.Entity, tool), tool.SpeedModifier * 1.66f);

            // #Cythisiax Fixed - restore normal gun handling for super mutants and nightkin.
            if (!IsMutant(uid) && _gunSystem.TryGetGun(args.Entity, out _, out var gun))
            {
                gun.MinAngle *= 15f;
                gun.AngleIncrease *= 15f;
                gun.MaxAngle *= 15f;
            }
        }

        private void OnEntRemoved(EntityUid uid, OniComponent component, EntRemovedFromContainerMessage args)
        {
            if (TryComp<ToolComponent>(args.Entity, out var tool) && _toolSystem.HasQuality(args.Entity, "Prying", tool))
                _toolSystem.SetSpeedModifier((args.Entity, tool), tool.SpeedModifier / 1.66f);

            // #Cythisiax Fixed - only undo the gun penalty when it was applied on pickup.
            if (!IsMutant(uid) && _gunSystem.TryGetGun(args.Entity, out _, out var gun))
            {
                gun.MinAngle /= 15f;
                gun.AngleIncrease /= 15f;
                gun.MaxAngle /= 15f;
            }

            // #Misfits Add - Restore health contest on the weapon when dropped from a super mutant.
            if (_healthContestDisabled.Remove(args.Entity)
                && TryComp<MeleeWeaponComponent>(args.Entity, out var melee)
                && melee.ContestArgs is not null)
            {
                melee.ContestArgs.DoHealthInteraction = true;
            }

            RemComp<HeldByOniComponent>(args.Entity);
        }



        private void OnGetMeleeDamage(EntityUid uid, MeleeWeaponComponent component, ref GetMeleeDamageEvent args)
        {
            if (!TryComp<OniComponent>(args.User, out var oni))

                return;

            // #Cythisiax Fixed - mutant melee now uses its prototype multiplier without a logarithmic curve or cap.
            if (IsMutant(args.User))
            {
                // #Cythisiax Fixed - keep health scaling from restoring excessive mutant weapon damage.
                if (component.ContestArgs is { DoHealthInteraction: true })
                {
                    component.ContestArgs.DoHealthInteraction = false;
                    _healthContestDisabled.Add(uid);
                }

                args.Modifiers.Add(oni.MeleeModifiers);
                return;
            }

            // Non-super-mutant Oni: flat modifiers via args.Modifiers (no log curve, no cap).
            args.Modifiers.Add(oni.MeleeModifiers);
        }

        // #Cythisiax Added - share the species check across melee and gun handling.
        private bool IsMutant(EntityUid uid) =>
            TryComp<HumanoidAppearanceComponent>(uid, out var appearance) &&
            (appearance.Species == "SuperMutant" || appearance.Species == "Nightkin");


        private void OnStamHit(EntityUid uid, HeldByOniComponent component, TakeStaminaDamageEvent args)
        {
            if (!TryComp<OniComponent>(component.Holder, out var oni))
                return;

            args.Multiplier *= oni.StamDamageMultiplier;
        }
    }
}
