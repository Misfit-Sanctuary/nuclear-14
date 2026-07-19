using Content.Server._Misfits.SpecialStats.Components;
using Content.Server.Actions;
using Content.Shared._Misfits.Special;
using Content.Shared._Misfits.Special.Components;
using Content.Shared._Misfits.SpecialStats;

namespace Content.Server._Misfits.SpecialStats;

/// <summary>
/// Grants active combat abilities (charge, parry, cripple) to characters with
/// high physical SPECIAL stats. See the med HUD system for the grant pattern.
/// </summary>
public sealed class SpecialCombatAbilitySystem : EntitySystem
{
    [Dependency] private readonly ActionsSystem _actions = default!;
    [Dependency] private readonly SharedSpecialSystem _special = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SpecialChangedEvent>(OnSpecialChanged);
        SubscribeLocalEvent<SpecialStatsReadyEvent>(OnStatsReady);
        SubscribeLocalEvent<SpecialShutdownEvent>(OnSpecialShutdown);
        SubscribeLocalEvent<SpecialCombatAbilitiesComponent, ComponentShutdown>(OnAbilitiesShutdown);
    }

    private void OnSpecialChanged(ref SpecialChangedEvent args)
    {
        if (TryComp<SpecialComponent>(args.ChangedEntity, out var special))
            ApplyAbilities((args.ChangedEntity, special));
    }

    private void OnStatsReady(ref SpecialStatsReadyEvent args)
    {
        if (TryComp<SpecialComponent>(args.Entity, out var special))
            ApplyAbilities((args.Entity, special));
    }

    private void OnSpecialShutdown(ref SpecialShutdownEvent args)
    {
        RemComp<SpecialCombatAbilitiesComponent>(args.Entity);
    }

    private void ApplyAbilities(Entity<SpecialComponent> ent)
    {
        var comp = EnsureComp<SpecialCombatAbilitiesComponent>(ent.Owner);

        UpdateAbility(ent, comp, SpecialStat.Agility, ref comp.ChargeActionEntity, comp.ChargeAction);
        UpdateAbility(ent, comp, SpecialStat.Endurance, ref comp.ParryActionEntity, comp.ParryAction);
        UpdateAbility(ent, comp, SpecialStat.Strength, ref comp.CrippleActionEntity, comp.CrippleAction);
    }

    private void UpdateAbility(
        Entity<SpecialComponent> ent,
        SpecialCombatAbilitiesComponent comp,
        SpecialStat stat,
        ref EntityUid? actionEntity,
        string actionId)
    {
        if (_special.GetEffective(ent.Owner, stat, ent.Comp) >= comp.Threshold)
        {
            _actions.AddAction(ent.Owner, ref actionEntity, actionId);
        }
        else
        {
            _actions.RemoveAction(ent.Owner, actionEntity);
            actionEntity = null;
        }
    }

    private void OnAbilitiesShutdown(Entity<SpecialCombatAbilitiesComponent> ent, ref ComponentShutdown args)
    {
        _actions.RemoveAction(ent.Owner, ent.Comp.ChargeActionEntity);
        _actions.RemoveAction(ent.Owner, ent.Comp.ParryActionEntity);
        _actions.RemoveAction(ent.Owner, ent.Comp.CrippleActionEntity);
    }
}
