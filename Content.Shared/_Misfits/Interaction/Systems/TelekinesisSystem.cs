// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.ActionBlocker;
using Content.Shared.Administration;
using Content.Shared.Bed.Sleep;
using Content.Shared.Cuffs;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Mobs;
using Content.Shared.Popups;
using Content.Shared.Stunnable;
using Content.Shared.Throwing;
using Content.Shared.Weapons.Misc;
using Robust.Shared.Random;

namespace Content.Shared._Misfits.Interaction;

public sealed partial class TelekinesisSystem : EntitySystem
{
    [Dependency] private ActionBlockerSystem _blocker = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTetherGunSystem _tether = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private ThrowingSystem _throwing = default!;
    [Dependency] private EntityQuery<AdminFrozenComponent> _frozenQuery = default!;
    [Dependency] private EntityQuery<TelekineticInteractableComponent> _targetQuery = default!;
    [Dependency] private EntityQuery<TetherGunComponent> _tetherGunQuery = default!;

    public override void Initialize()
    {
        base.Initialize();

        // this is evil but preferable to making a new event to uncancel interaction attempts.
        // anything important that might accidentally get overriden (admin freeze) is already checked in CanUseTelekinesis
        SubscribeLocalEvent<TelekinesisComponent, InteractionAttemptEvent>(OnInteractionAttempt,
            after: new[] { typeof(SharedStunSystem), typeof(SharedCuffableSystem) });
        SubscribeLocalEvent<TelekinesisComponent, InRangeOverrideEvent>(OnRangeOverride,
            after: new[] { typeof(ExtraReachSystem) });
        SubscribeLocalEvent<TelekinesisComponent, TelekinesisActionEvent>(OnAction);
        SubscribeLocalEvent<TelekinesisComponent, SleepStateChangedEvent>(OnSleepStateChanged);
        SubscribeLocalEvent<TelekinesisComponent, MobStateChangedEvent>(OnMobStateChanged);
    }

    private void OnInteractionAttempt(Entity<TelekinesisComponent> ent, ref InteractionAttemptEvent args)
    {
        // overwrite previous cancel from stunned, cuffed etc
        args.Cancelled = !CanUseTelekinesis(ent);
    }

    private void OnRangeOverride(Entity<TelekinesisComponent> ent, ref InRangeOverrideEvent args)
    {
        args.Handled = true;
        // remote interaction: anything within telekinesis range can be interacted with without
        // touching it (open doors, pick up items, etc). TelekineticInteractable still marks
        // targets usable from any distance.
        // Only the InRangeUnobstructed overload that interactions use raises this event, so
        // melee still goes through its own range check and can't reach any further than normal.
        args.InRange = _targetQuery.HasComp(args.Target) ||
            IsInRange(args.User, args.Target, ent.Comp.Range);
    }

    private void OnAction(Entity<TelekinesisComponent> ent, ref TelekinesisActionEvent args)
    {
        if (!_tetherGunQuery.TryComp(ent, out var gun))
            return;

        args.Handled = true;
        var original = gun.Tethered;

        // using the action on another target while holding something hurls the held
        // object at them instead of switching the tether.
        if (original is { } held && args.Target != held && args.Target != ent.Owner)
        {
            _tether.StopTether(ent, gun, land: false);

            // chud shit doesnt predict anything :(
            if (_net.IsClient) return;

            // throw along the exact vector from the held object to the target so it
            // flies at them no matter where the tether was holding it
            var heldPos = _transform.GetMapCoordinates(held).Position;
            var targetPos = _transform.GetMapCoordinates(args.Target).Position;
            var direction = targetPos - heldPos;
            if (direction.LengthSquared() > 0.01f)
                _throwing.TryThrow(held, direction, ent.Comp.ThrowForce, user: args.Performer, playSound: false);
            return;
        }

        _tether.StopTether(ent, gun);

        // chud shit doesnt predict anything :(
        if (_net.IsClient) return;

        // don't tether if you use action on the same item twice, or if you use it on yourself (easy cancel)
        if (args.Target == original || args.Target == ent.Owner)
            return;

        // Rip it straight out of whoever's holding it, but only once we know the tether can
        // actually reach. Disarming first and tethering second meant a failed tether still
        // dropped the item, at the action's full range.
        if (!IsInRange(ent, args.Target, ent.Comp.Range))
            return;

        var holder = Transform(args.Target).ParentUid;
        var wasHeld = holder.IsValid() && _hands.IsHolding(holder, args.Target, out _);
        if (wasHeld)
        {
            // a held item is being actively gripped, so tearing it free isn't a sure thing
            if (_random.Prob(ent.Comp.DisarmFailChance))
            {
                _popup.PopupEntity(Loc.GetString("telekinesis-disarm-failed", ("item", args.Target)), ent, ent);
                _popup.PopupEntity(Loc.GetString("telekinesis-disarm-resisted", ("item", args.Target)), holder, holder);
                return;
            }

            if (!_hands.TryDrop(holder, args.Target, checkActionBlocker: false))
                return;
        }

        // if the tether still fails after we tore it loose, hand it back rather than
        // leaving it on the floor for free
        if (!_tether.TryTether(ent, args.Target, args.Performer, gun) && wasHeld)
            _hands.TryPickupAnyHand(holder, args.Target, checkActionBlocker: false);
    }

    // can't use your mind powers if you go eepy
    private void OnSleepStateChanged(Entity<TelekinesisComponent> ent, ref SleepStateChangedEvent args)
    {
        if (!args.FellAsleep)
            return;

        if (_tetherGunQuery.TryComp(ent, out var gun))
            _tether.StopTether(ent, gun);
    }

    // can't use your mind powers if you fucking die
    private void OnMobStateChanged(Entity<TelekinesisComponent> ent, ref MobStateChangedEvent args)
    {
        // the condition was inverted before: it dropped the tether when you were ALIVE
        // and kept it when you died/critted.
        if (args.NewMobState == MobState.Alive)
            return;

        if (_tetherGunQuery.TryComp(ent, out var gun))
            _tether.StopTether(ent, gun);
    }

    public bool CanUseTelekinesis(EntityUid uid)
    {
        // never let players bypass admin freeze
        if (_frozenQuery.HasComp(uid))
            return false;

        // can't use telekinesis if you are eepy
        return _blocker.CanConsciouslyPerformAction(uid);
    }

    public bool IsInRange(EntityUid user, EntityUid target, float range)
    {
        var xform = Transform(user);
        var targetXform = Transform(target);
        if (xform.MapUid != targetXform.MapUid)
            return false; // telekinetic not fucking god

        var pos = _transform.GetMapCoordinates(user, xform).Position;
        var targetPos = _transform.GetMapCoordinates(target, targetXform).Position;
        var dist2 = (pos - targetPos).LengthSquared();
        var r2 = range * range;
        return dist2 <= r2;
    }
}
