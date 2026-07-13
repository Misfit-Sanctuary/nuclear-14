using Content.Shared._Misfits.C27;
using Content.Shared.Actions;
using Content.Shared.Damage.Components;
using Content.Shared.DoAfter;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;

namespace Content.Server._Misfits.C27;

public sealed class ZaxC27RepairModeSystem : EntitySystem
{
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movement = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ZaxC27RepairModeComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<ZaxC27RepairModeComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<ZaxC27RepairModeComponent, ToggleZaxC27RepairModeEvent>(OnToggle);
        SubscribeLocalEvent<ZaxC27RepairModeComponent, ZaxC27RepairModeDoAfterEvent>(OnDoAfter);
        SubscribeLocalEvent<ZaxC27RepairModeActiveComponent, ComponentShutdown>(OnActiveShutdown);
        SubscribeLocalEvent<ZaxC27RepairModeTransitionComponent, ComponentShutdown>(OnTransitionShutdown);
    }

    private void OnMapInit(EntityUid uid, ZaxC27RepairModeComponent component, MapInitEvent args)
    {
        ApplyRepairRate(uid, component, HasComp<ZaxC27RepairModeActiveComponent>(uid));
    }

    private void OnMobStateChanged(EntityUid uid, ZaxC27RepairModeComponent component, MobStateChangedEvent args)
    {
        if (args.NewMobState is not (MobState.Critical or MobState.Dead))
            return;

        ExitRepairMode(uid, component);
    }

    private void OnToggle(EntityUid uid, ZaxC27RepairModeComponent component, ToggleZaxC27RepairModeEvent args)
    {
        if (args.Handled ||
            args.Performer != uid ||
            HasComp<ZaxC27RepairModeTransitionComponent>(uid) ||
            !CanUseRepairMode(uid))
        {
            return;
        }

        var activate = !HasComp<ZaxC27RepairModeActiveComponent>(uid);
        component.ActionEntity = args.Action.Owner;

        EnsureComp<ZaxC27RepairModeTransitionComponent>(uid);
        _movement.RefreshMovementSpeedModifiers(uid);

        var doAfter = new DoAfterArgs(
            EntityManager,
            uid,
            component.ToggleDelay,
            new ZaxC27RepairModeDoAfterEvent(activate),
            uid,
            target: uid)
        {
            BreakOnDamage = false,
            BreakOnMove = false,
            NeedHand = false,
            RequireCanInteract = false,
        };

        if (!_doAfter.TryStartDoAfter(doAfter))
        {
            RemComp<ZaxC27RepairModeTransitionComponent>(uid);
            _movement.RefreshMovementSpeedModifiers(uid);
            return;
        }

        _popup.PopupEntity(
            Loc.GetString(activate
                ? "zax-c27-repair-mode-entering"
                : "zax-c27-repair-mode-exiting"),
            uid,
            uid);

        args.Handled = true;
    }

    private void OnDoAfter(EntityUid uid, ZaxC27RepairModeComponent component, ZaxC27RepairModeDoAfterEvent args)
    {
        if (args.User != uid)
            return;

        if (!HasComp<ZaxC27RepairModeTransitionComponent>(uid))
        {
            args.Handled = true;
            return;
        }

        RemComp<ZaxC27RepairModeTransitionComponent>(uid);

        if (args.Cancelled)
        {
            _movement.RefreshMovementSpeedModifiers(uid);
            _actions.SetToggled(component.ActionEntity, HasComp<ZaxC27RepairModeActiveComponent>(uid));
            return;
        }

        if (!CanUseRepairMode(uid))
        {
            ExitRepairMode(uid, component);
            args.Handled = true;
            return;
        }

        if (args.Activate)
            EnsureComp<ZaxC27RepairModeActiveComponent>(uid);
        else
            RemComp<ZaxC27RepairModeActiveComponent>(uid);

        ApplyRepairRate(uid, component, args.Activate);
        _movement.RefreshMovementSpeedModifiers(uid);
        _actions.SetToggled(component.ActionEntity, args.Activate);

        _popup.PopupEntity(
            Loc.GetString(args.Activate
                ? "zax-c27-repair-mode-entered"
                : "zax-c27-repair-mode-exited"),
            uid,
            uid);

        args.Handled = true;
    }

    private void OnActiveShutdown(EntityUid uid, ZaxC27RepairModeActiveComponent component, ComponentShutdown args)
    {
        if (TerminatingOrDeleted(uid))
            return;

        if (TryComp<ZaxC27RepairModeComponent>(uid, out var repairMode))
        {
            ApplyRepairRate(uid, repairMode, false);
            _actions.SetToggled(repairMode.ActionEntity, false);
        }

        _movement.RefreshMovementSpeedModifiers(uid);
    }

    private void OnTransitionShutdown(EntityUid uid, ZaxC27RepairModeTransitionComponent component, ComponentShutdown args)
    {
        if (!TerminatingOrDeleted(uid))
            _movement.RefreshMovementSpeedModifiers(uid);
    }

    private bool CanUseRepairMode(EntityUid uid)
    {
        return !TryComp<MobStateComponent>(uid, out var mobState) ||
               mobState.CurrentState == MobState.Alive;
    }

    private void ExitRepairMode(EntityUid uid, ZaxC27RepairModeComponent component)
    {
        RemComp<ZaxC27RepairModeTransitionComponent>(uid);
        RemComp<ZaxC27RepairModeActiveComponent>(uid);
        ApplyRepairRate(uid, component, false);
        _movement.RefreshMovementSpeedModifiers(uid);
        _actions.SetToggled(component.ActionEntity, false);
    }

    private void ApplyRepairRate(EntityUid uid, ZaxC27RepairModeComponent component, bool active)
    {
        if (!TryComp<PassiveDamageComponent>(uid, out var passiveDamage))
            return;

        passiveDamage.Damage = active
            ? component.ActiveRepair
            : component.NormalRepair;

        Dirty(uid, passiveDamage);
    }
}
