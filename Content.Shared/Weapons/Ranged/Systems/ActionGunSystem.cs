using Content.Shared.Actions;
using Content.Shared._Misfits.Robot;
using Content.Shared.CombatMode;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Shared.Weapons.Ranged.Systems;

public sealed class ActionGunSystem : EntitySystem
{
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedCombatModeSystem _combat = default!;
    [Dependency] private readonly SharedGunSystem _gun = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly INetManager _net = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ActionGunComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<ActionGunComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<ActionGunComponent, ActionGunShootEvent>(OnShoot);
    }

    private void OnMapInit(Entity<ActionGunComponent> ent, ref MapInitEvent args)
    {
        if (string.IsNullOrEmpty(ent.Comp.Action))
            return;

        _actions.AddAction(ent, ref ent.Comp.ActionEntity, ent.Comp.Action);
        ent.Comp.Gun = Spawn(ent.Comp.GunProto);
    }

    private void OnShutdown(Entity<ActionGunComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.Gun is {} gun)
            QueueDel(gun);
    }

    private void OnShoot(Entity<ActionGunComponent> ent, ref ActionGunShootEvent args)
    {
        args.Handled = true;

        if (ent.Comp.Gun is not { } gunUid)
        {
            if (_net.IsClient)
                return;

            gunUid = Spawn(ent.Comp.GunProto);
            ent.Comp.Gun = gunUid;
        }

        if (TryComp<AssaultronBeamChargeComponent>(gunUid, out var charge))
        {
            if (_net.IsClient)
                return;

            HandleAssaultronEmitter(gunUid, charge, args);
            return;
        }

        if (TryComp<GunComponent>(gunUid, out var gun))
            _gun.AttemptShoot(ent, gunUid, gun, args.Target);
    }

    private void HandleAssaultronEmitter(
        EntityUid gunUid,
        AssaultronBeamChargeComponent charge,
        ActionGunShootEvent args)
    {
        if (_net.IsClient)
            return;

        var now = _timing.CurTime;

        if (!charge.IsCharging && now < charge.CooldownEndTime)
        {
            _actions.SetCooldown(args.Action.Owner, now, charge.CooldownEndTime);
            return;
        }

        if (!charge.IsCharging)
        {
            charge.IsCharging = true;
            charge.ReadyToFire = false;
            charge.ChargeEndTime = now + TimeSpan.FromSeconds(charge.ChargeDuration);
            _actions.SetCooldown(args.Action.Owner, now, charge.ChargeEndTime);
            BeginTargeting(args.Performer, charge);

            var chargeStarted = new AssaultronChargeStartedEvent(args.Performer, charge.ChargeEmoteLocale);
            RaiseLocalEvent(gunUid, ref chargeStarted);
            return;
        }

        if (now < charge.ChargeEndTime)
        {
            _actions.SetCooldown(args.Action.Owner, now, charge.ChargeEndTime);
            KeepTargeting(args.Performer, charge);
            return;
        }

        if (!TryComp<GunComponent>(gunUid, out var gun))
            return;

        _gun.AttemptShoot(args.Performer, gunUid, gun, args.Target);
        EndTargeting(args.Performer, charge);

        if (charge.CooldownEndTime > now)
            _actions.SetCooldown(args.Action.Owner, now, charge.CooldownEndTime);
    }

    private void BeginTargeting(EntityUid user, AssaultronBeamChargeComponent charge)
    {
        if (!TryComp<CombatModeComponent>(user, out var combat) ||
            combat.IsInCombatMode)
        {
            charge.ForcedCombatMode = false;
            return;
        }

        charge.ForcedCombatMode = true;
        _combat.SetInCombatMode(user, true, combat);
    }

    private void KeepTargeting(EntityUid user, AssaultronBeamChargeComponent charge)
    {
        if (!charge.ForcedCombatMode ||
            !TryComp<CombatModeComponent>(user, out var combat) ||
            combat.IsInCombatMode)
        {
            return;
        }

        _combat.SetInCombatMode(user, true, combat);
    }

    private void EndTargeting(EntityUid user, AssaultronBeamChargeComponent charge)
    {
        if (!charge.ForcedCombatMode)
            return;

        charge.ForcedCombatMode = false;
        _combat.SetInCombatMode(user, false);
    }
}

