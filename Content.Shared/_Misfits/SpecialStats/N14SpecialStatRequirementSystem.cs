// #Misfits Add - Applies N14SpecialStatRequirementComponent to wielding, use-in-hand and firing
using Content.Shared._Misfits.PowerArmor;
using Content.Shared._Misfits.Special;
using Content.Shared._Misfits.Special.Components;
using Content.Shared._Misfits.SpecialStats.Components;
using Content.Shared.Interaction.Events;
using Content.Shared.Popups;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Wieldable;
using Content.Shared.Wieldable.Components;

namespace Content.Shared._Misfits.SpecialStats;

/// Refuses wielding, use-in-hand and shooting when an item's stat minimums are not met; shared so prediction cancels it.
public sealed class N14SpecialStatRequirementSystem : EntitySystem
{
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedSpecialSystem _special = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<N14SpecialStatRequirementComponent, UseInHandEvent>(OnUseInHand, before: [typeof(WieldableSystem)]);
        SubscribeLocalEvent<N14SpecialStatRequirementComponent, BeforeWieldEvent>(OnBeforeWield);
        SubscribeLocalEvent<N14SpecialStatRequirementComponent, AttemptShootEvent>(OnAttemptShoot);
    }

    private void OnUseInHand(Entity<N14SpecialStatRequirementComponent> ent, ref UseInHandEvent args)
    {
        if (TryComp<WieldableComponent>(ent, out var wieldable) && wieldable.Wielded)
            return;

        if (!TryGetMissingRequirement(args.User, ent.Comp, out var stat, out var required))
            return;

        _popup.PopupClient(GetFailureMessage(ent.Comp, stat, required), ent, args.User);
        args.Handled = true;
    }

    private void OnBeforeWield(Entity<N14SpecialStatRequirementComponent> ent, ref BeforeWieldEvent args)
    {
        if (!TryGetMissingRequirement(args.User, ent.Comp, out var stat, out var required))
            return;

        _popup.PopupClient(GetFailureMessage(ent.Comp, stat, required), ent, args.User);
        args.Cancel();
    }

    private void OnAttemptShoot(Entity<N14SpecialStatRequirementComponent> ent, ref AttemptShootEvent args)
    {
        if (!TryGetMissingRequirement(args.User, ent.Comp, out var stat, out var required))
            return;

        args.Message = GetFailureMessage(ent.Comp, stat, required);
        args.Cancelled = true;
    }

    /// Finds the first configured minimum the user does not meet, with the stat and value to report.
    private bool TryGetMissingRequirement(
        EntityUid user,
        N14SpecialStatRequirementComponent requirement,
        out SpecialStat stat,
        out int required)
    {
        stat = default;
        required = 0;

        if (requirement.MinimumStats.Count == 0)
            return false;

        // Power armor servos carry the weight; SPECIAL does not matter.
        if (requirement.BypassWithPowerArmor && HasComp<PowerArmorWornComponent>(user))
            return false;

        // Entities without SPECIAL read as the neutral 5; opt in to letting them through.
        if (requirement.AllowNonSpecialEntities && !HasComp<SpecialComponent>(user))
            return false;

        foreach (var (candidate, minimum) in requirement.MinimumStats)
        {
            if (minimum <= SpecialProfile.Minimum)
                continue;

            if (_special.HasRequirement(user, candidate, minimum))
                continue;

            stat = candidate;
            required = minimum;
            return true;
        }

        return false;
    }

    private string GetFailureMessage(N14SpecialStatRequirementComponent requirement, SpecialStat stat, int required)
    {
        return Loc.GetString(requirement.Popup, ("stat", GetStatName(stat)), ("required", required));
    }

    private string GetStatName(SpecialStat stat)
    {
        var id = stat switch
        {
            SpecialStat.Strength => "special-stat-name-strength",
            SpecialStat.Perception => "special-stat-name-perception",
            SpecialStat.Endurance => "special-stat-name-endurance",
            SpecialStat.Charisma => "special-stat-name-charisma",
            SpecialStat.Intelligence => "special-stat-name-intelligence",
            SpecialStat.Agility => "special-stat-name-agility",
            SpecialStat.Luck => "special-stat-name-luck",
            _ => "special-stat-name-strength",
        };

        return Loc.GetString(id);
    }
}
