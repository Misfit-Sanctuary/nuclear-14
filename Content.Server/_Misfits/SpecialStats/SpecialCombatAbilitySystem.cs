using System.Numerics;
using Content.Server._Misfits.SpecialStats.Components;
using Content.Server.Actions;
using Content.Shared._Misfits.Special;
using Content.Shared._Misfits.Special.Components;
using Content.Shared._Misfits.SpecialStats;
using Content.Shared.Throwing;

namespace Content.Server._Misfits.SpecialStats;

/// <summary>
/// Grants active combat abilities (charge, parry, cripple) to characters with
/// high physical SPECIAL stats. See the med HUD system for the grant pattern.
/// </summary>
public sealed class SpecialCombatAbilitySystem : EntitySystem
{
    [Dependency] private readonly ActionsSystem _actions = default!;
    [Dependency] private readonly SharedSpecialSystem _special = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly ThrowingSystem _throwing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SpecialChangedEvent>(OnSpecialChanged);
        SubscribeLocalEvent<SpecialStatsReadyEvent>(OnStatsReady);
        SubscribeLocalEvent<SpecialShutdownEvent>(OnSpecialShutdown);
        SubscribeLocalEvent<SpecialCombatAbilitiesComponent, ComponentShutdown>(OnAbilitiesShutdown);
        SubscribeLocalEvent<SpecialCombatAbilitiesComponent, SpecialChargeActionEvent>(OnCharge);
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

    private void OnCharge(Entity<SpecialCombatAbilitiesComponent> ent, ref SpecialChargeActionEvent args)
    {
        if (args.Handled)
            return;

        var user = args.Performer;
        var userPos = _transform.GetMapCoordinates(user);
        var targetPos = _transform.ToMapCoordinates(args.Target);

        if (targetPos.MapId != userPos.MapId)
            return;

        var direction = targetPos.Position - userPos.Position;
        if (direction == Vector2.Zero)
            return;

        if (direction.Length() > ent.Comp.ChargeRange)
            direction = Vector2.Normalize(direction) * ent.Comp.ChargeRange;

        // A throw, not a teleport: walls and obstacles stop the lunge naturally.
        _throwing.TryThrow(user, direction, ent.Comp.ChargeSpeed, user,
            pushbackRatio: 0f, compensateFriction: true, recoil: false, doSpin: false);
        args.Handled = true;
    }

    private void OnAbilitiesShutdown(Entity<SpecialCombatAbilitiesComponent> ent, ref ComponentShutdown args)
    {
        _actions.RemoveAction(ent.Owner, ent.Comp.ChargeActionEntity);
        _actions.RemoveAction(ent.Owner, ent.Comp.ParryActionEntity);
        _actions.RemoveAction(ent.Owner, ent.Comp.CrippleActionEntity);
    }
}
