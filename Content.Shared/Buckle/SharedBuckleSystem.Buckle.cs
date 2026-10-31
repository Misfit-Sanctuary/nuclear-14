using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using Content.Shared._NC.Mountable.Components; // Corvax-Change
using Content.Shared.Alert;
using Content.Shared.Buckle.Components;
using Content.Shared.Cuffs.Components;
using Content.Shared.Database;
using Content.Shared.DoAfter;
using Content.Shared.Hands.Components;
using Content.Shared.IdentityManagement;
using Content.Shared.Movement.Events;
using Content.Shared.Movement.Pulling.Events;
using Content.Shared.Popups;
using Content.Shared.Pulling.Events;
using Content.Shared.Rotation;
using Content.Shared.Standing;
using Content.Shared.Storage.Components;
using Content.Shared.Stunnable;
using Content.Shared.Throwing;
using Content.Shared._Misfits.Buckle;
using Content.Shared.Vehicles;
using Content.Shared._Misfits.Buckle.Events;
using Content.Shared.Mobs;
using Content.Shared.Vehicles;
using Content.Shared.Whitelist;
using Robust.Shared.Containers;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared.Buckle;

public abstract partial class SharedBuckleSystem
{
    public static ProtoId<AlertCategoryPrototype> BuckledAlertCategory = "Buckled";

    [Robust.Shared.IoC.Dependency] private readonly EntityWhitelistSystem _whitelistSystem = default!;

    private void InitializeBuckle()
    {
        SubscribeLocalEvent<BuckleComponent, ComponentShutdown>(OnBuckleComponentShutdown);
        SubscribeLocalEvent<BuckleComponent, MoveEvent>(OnBuckleMove);
        SubscribeLocalEvent<BuckleComponent, EntParentChangedMessage>(OnParentChanged);
        SubscribeLocalEvent<BuckleComponent, EntGotInsertedIntoContainerMessage>(OnInserted);

        SubscribeLocalEvent<BuckleComponent, StartPullAttemptEvent>(OnPullAttempt);
        SubscribeLocalEvent<BuckleComponent, BeingPulledAttemptEvent>(OnBeingPulledAttempt);
        SubscribeLocalEvent<BuckleComponent, PullAttemptEvent>(OnBucklePullAttempt); // Goobstation
        SubscribeLocalEvent<BuckleComponent, PullStartedMessage>(OnPullStarted);

        SubscribeLocalEvent<BuckleComponent, InsertIntoEntityStorageAttemptEvent>(OnBuckleInsertIntoEntityStorageAttempt);

        SubscribeLocalEvent<BuckleComponent, PreventCollideEvent>(OnBucklePreventCollide);
        SubscribeLocalEvent<BuckleComponent, MobStateChangedEvent>(OnRiderMobStateChanged);

        SubscribeLocalEvent<BuckleComponent, DownAttemptEvent>(OnBuckleDownAttempt);
        SubscribeLocalEvent<BuckleComponent, StandAttemptEvent>(OnBuckleStandAttempt);
        SubscribeLocalEvent<BuckleComponent, ThrowPushbackAttemptEvent>(OnBuckleThrowPushbackAttempt);
        SubscribeLocalEvent<BuckleComponent, UpdateCanMoveEvent>(OnBuckleUpdateCanMove);
        SubscribeLocalEvent<BuckleComponent, UnbuckleDoAfterEvent>(OnUnbuckleDoAfter); // WD EDIT

        SubscribeLocalEvent<BuckleComponent, BuckleDoAfterEvent>(OnBuckleDoafter);
        SubscribeLocalEvent<BuckleComponent, DoAfterAttemptEvent<BuckleDoAfterEvent>>((uid, comp, ev) =>
        {
            BuckleDoafterEarly((uid, comp), ev.Event, ev);
        });
    }

    // #Cythisiax Fixed - Revert PR #1103: projectiles no longer pass through a ridden vehicle's fixture,
    // so the bike blocks shots and takes full damage again.
    private void OnRiderMobStateChanged(Entity<BuckleComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Alive ||
            ent.Comp.BuckledTo is not { } strap ||
            !HasComp<VehicleComponent>(strap))
            return;

        Unbuckle((ent.Owner, ent.Comp), null);
    }
    private void OnBuckleComponentShutdown(Entity<BuckleComponent> ent, ref ComponentShutdown args)
        => Unbuckle(ent!, null);

    #region Pulling

    private void OnPullAttempt(Entity<BuckleComponent> ent, ref StartPullAttemptEvent args)
    {
        // Prevent people pulling the chair they're on, etc.
        if (ent.Comp.BuckledTo == args.Pulled && !ent.Comp.PullStrap)
            args.Cancel();
    }

    private void OnBeingPulledAttempt(Entity<BuckleComponent> ent, ref BeingPulledAttemptEvent args)
    {
        if (args.Cancelled || !ent.Comp.Buckled)
            return;

        if (!CanUnbuckle(ent!, args.Puller, false))
        {
            args.Cancel();
            return;
        }

        // Goobstation - cancel pull entirely if others are not allowed to unbuckle
        if (args.Puller != ent.Owner
            && TryComp<StrapComponent>(ent.Comp.BuckledTo, out var strap)
            && !strap.AllowOthersToUnbuckle)
        {
            args.Cancel();

        }
        // Goobstation
    }
    // Goobstation - start unbuckle do-after only on actual pull initiation, not on CanPull checks
    private void OnBucklePullAttempt(Entity<BuckleComponent> ent, ref PullAttemptEvent args)
    {
        if (args.Cancelled || !ent.Comp.Buckled || args.PullerUid == ent.Owner)
            return;

        if (!TryComp<StrapComponent>(ent.Comp.BuckledTo, out var strap) || strap.UnbuckleDoafterTime <= 0)
            return;

        if (!CanUnbuckle(ent!, args.PullerUid, false))
            return;

        args.Cancelled = true;
        var doAfter = new DoAfterArgs(EntityManager, args.PullerUid, TimeSpan.FromSeconds(strap.UnbuckleDoafterTime), new UnbuckleDoAfterEvent(), ent.Owner, target: ent.Owner)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
        };
        _doAfter.TryStartDoAfter(doAfter);
    }
    // Goobstation

    private void OnPullStarted(Entity<BuckleComponent> ent, ref PullStartedMessage args)
    {
        Unbuckle(ent!, args.PullerUid);
    }

    #endregion

    #region Transform

    private void OnParentChanged(Entity<BuckleComponent> ent, ref EntParentChangedMessage args)
    {
        BuckleTransformCheck(ent, args.Transform);
    }

    private void OnInserted(Entity<BuckleComponent> ent, ref EntGotInsertedIntoContainerMessage args)
    {
        BuckleTransformCheck(ent, Transform(ent));
    }

    private void OnBuckleMove(Entity<BuckleComponent> ent, ref MoveEvent ev)
    {
        BuckleTransformCheck(ent, ev.Component);
    }

    /// <summary>
    /// Check if the entity should get unbuckled as a result of transform or container changes.
    /// </summary>
    private void BuckleTransformCheck(Entity<BuckleComponent> buckle, TransformComponent xform)
    {
        if (_gameTiming.ApplyingState)
            return;

        if (buckle.Comp.BuckledTo is not { } strapUid)
            return;

        if (!TryComp<StrapComponent>(strapUid, out var strapComp))
        {
            Log.Error($"Encountered buckle entity {ToPrettyString(buckle)} without a valid strap entity {ToPrettyString(strapUid)}");
            SetBuckledTo(buckle, null);
            return;
        }

        if (xform.ParentUid != strapUid || _container.IsEntityInContainer(buckle))
        {
            Unbuckle(buckle, (strapUid, strapComp), null);
            return;
        }

        // #Misfits Fix — skip position-drift check for mountable straps.
        // The mount system manages rider positioning with direction-dependent
        // offsets that intentionally deviate from BuckleOffset.
        if (HasComp<DirectionalBuckleOffsetComponent>(strapUid))
            return;

        var delta = (xform.LocalPosition - strapComp.BuckleOffset).LengthSquared();
        if (delta > 1e-5)
        {
            if (strapComp.LockToBuckleOffset)
            {
                _transform.SetLocalPosition(buckle.Owner, strapComp.BuckleOffset, xform);
                return;
            }

            Unbuckle(buckle, (strapUid, strapComp), null);
        }
    }

    #endregion

    private void OnBuckleInsertIntoEntityStorageAttempt(EntityUid uid, BuckleComponent component, ref InsertIntoEntityStorageAttemptEvent args)
    {
        if (component.Buckled)
            args.Cancelled = true;
    }

    private void OnBucklePreventCollide(EntityUid uid, BuckleComponent component, ref PreventCollideEvent args)
    {
        if (args.OtherEntity == component.BuckledTo && component.DontCollide)
            args.Cancelled = true;

        // Misfits Riders are pinned at an offset on mounts, so only mounts body should block, non hard contacts still hit rider.
        if (args.OurFixture.Hard && args.OtherFixture.Hard && component.BuckledTo is { } strap
            && (HasComp<MountableComponent>(strap) || HasComp<VehicleComponent>(strap)))
            args.Cancelled = true;
    }

    private void OnBuckleDownAttempt(EntityUid uid, BuckleComponent component, DownAttemptEvent args)
    {
        if (component.Buckled)
            args.Cancel();
    }

    private void OnBuckleStandAttempt(EntityUid uid, BuckleComponent component, StandAttemptEvent args)
    {
        if (component.Buckled)
            args.Cancel();
    }

    private void OnBuckleThrowPushbackAttempt(EntityUid uid, BuckleComponent component, ThrowPushbackAttemptEvent args)
    {
        if (component.Buckled)
            args.Cancel();
    }

    private void OnBuckleUpdateCanMove(EntityUid uid, BuckleComponent component, UpdateCanMoveEvent args)
    {
        if (component.Buckled)
            args.Cancel();
    }

    private void OnUnbuckleDoAfter(EntityUid uid, BuckleComponent component, UnbuckleDoAfterEvent args)
    {
        if (args.Cancelled || !CanUnbuckle((uid, component), args.User, true, out var strap)) // Goobstation
            return;

        Unbuckle((uid, component), strap, args.User); // Goobstation
    }
    public bool IsBuckled(EntityUid uid, BuckleComponent? component = null)
    {
        return Resolve(uid, ref component, false) && component.Buckled;
    }

    private void SetBuckledTo(Entity<BuckleComponent> buckle, Entity<StrapComponent?>? strap)
    {
        if (TryComp(buckle.Comp.BuckledTo, out StrapComponent? old))
        {
            old.BuckledEntities.Remove(buckle);
            Dirty(buckle.Comp.BuckledTo.Value, old);
        }

        if (strap is {} strapEnt && Resolve(strapEnt.Owner, ref strapEnt.Comp))
        {
            strapEnt.Comp.BuckledEntities.Add(buckle);
            Dirty(strapEnt);
            _alerts.ShowAlert(buckle, strapEnt.Comp.BuckledAlertType);
        }
        else
        {
            _alerts.ClearAlertCategory(buckle, BuckledAlertCategory);
        }

        buckle.Comp.BuckledTo = strap;
        buckle.Comp.BuckleTime = _gameTiming.CurTime;
        ActionBlocker.UpdateCanMove(buckle);
        Appearance.SetData(buckle, StrapVisuals.State, buckle.Comp.Buckled);
        Dirty(buckle);
    }

    /// <summary>
    /// Checks whether or not buckling is possible
    /// </summary>
    /// <param name="buckleUid"> Uid of the owner of BuckleComponent </param>
    /// <param name="user">
    ///     Uid of a third party entity,
    ///     i.e, the uid of someone else you are dragging to a chair.
    ///     Can equal buckleUid sometimes
    /// </param>
    /// <param name="strapUid"> Uid of the owner of strap component </param>
    /// <param name="strapComp"></param>
    /// <param name="buckleComp"></param>
    private bool CanBuckle(EntityUid buckleUid,
        EntityUid? user,
        EntityUid strapUid,
        bool popup,
        [NotNullWhen(true)] out StrapComponent? strapComp,
        BuckleComponent buckleComp)
    {
        strapComp = null;
        if (!Resolve(strapUid, ref strapComp, false))
            return false;

        if (HasComp<MountableComponent>(buckleUid)) // Corvax-Change hack to prevent buckling a brado
            return false;

        // Does it pass the Whitelist
        if (_whitelistSystem.IsWhitelistFail(strapComp.Whitelist, buckleUid) ||
            _whitelistSystem.IsBlacklistPass(strapComp.Blacklist, buckleUid))
        {
            if (popup)
                _popup.PopupClient(Loc.GetString("buckle-component-cannot-fit-message"), user, PopupType.Medium);

            return false;
        }

        if (!_interaction.InRangeUnobstructed(buckleUid,
                strapUid,
                buckleComp.Range,
                predicate: entity => entity == buckleUid || entity == user || entity == strapUid,
                popup: true))
        {
            return false;
        }

        if (!_container.IsInSameOrNoContainer((buckleUid, null, null), (strapUid, null, null)))
            return false;

        if (user != null && !HasComp<HandsComponent>(user))
        {
            if (popup)
                _popup.PopupClient(Loc.GetString("buckle-component-no-hands-message"), user);

            return false;
        }

        if (buckleComp.Buckled && !TryUnbuckle(buckleUid, user, buckleComp))
        {
            if (popup)
            {
                var message = Loc.GetString(buckleUid == user
                    ? "buckle-component-already-buckled-message"
                    : "buckle-component-other-already-buckled-message",
                ("owner", Identity.Entity(buckleUid, EntityManager)));

                _popup.PopupClient(message, user);
            }

            return false;
        }

        // Check whether someone is attempting to buckle something to their own child
        var parent = Transform(strapUid).ParentUid;
        while (parent.IsValid())
        {
            if (parent != buckleUid)
            {
                parent = Transform(parent).ParentUid;
                continue;
            }

            if (popup)
            {
                var message = Loc.GetString(buckleUid == user
                    ? "buckle-component-cannot-buckle-message"
                    : "buckle-component-other-cannot-buckle-message",
                ("owner", Identity.Entity(buckleUid, EntityManager)));

                _popup.PopupClient(message, user);
            }

            return false;
        }

        if (!StrapHasSpace(strapUid, buckleComp, strapComp))
        {
            if (popup)
            {
                var message = Loc.GetString(buckleUid == user
                    ? "buckle-component-cannot-buckle-message"
                    : "buckle-component-other-cannot-buckle-message",
                ("owner", Identity.Entity(buckleUid, EntityManager)));

                _popup.PopupClient(message, user);
            }

            return false;
        }

        var buckleAttempt = new BuckleAttemptEvent((strapUid, strapComp), (buckleUid, buckleComp), user, popup);
        RaiseLocalEvent(buckleUid, ref buckleAttempt);
        if (buckleAttempt.Cancelled)
            return false;

        var strapAttempt = new StrapAttemptEvent((strapUid, strapComp), (buckleUid, buckleComp), user, popup);
        RaiseLocalEvent(strapUid, ref strapAttempt);
        if (strapAttempt.Cancelled)
            return false;

        return true;
    }

    /// <summary>
    /// Attempts to buckle an entity to a strap
    /// </summary>
    /// <param name="buckle"> Uid of the owner of BuckleComponent </param>
    /// <param name="user">
    /// Uid of a third party entity,
    /// i.e, the uid of someone else you are dragging to a chair.
    /// Can equal buckleUid sometimes
    /// </param>
    /// <param name="strap"> Uid of the owner of strap component </param>
    public bool TryBuckle(EntityUid buckle, EntityUid? user, EntityUid strap, BuckleComponent? buckleComp = null, bool popup = true)
    {
        if (!Resolve(buckle, ref buckleComp, false))
            return false;

        if (!CanBuckle(buckle, user, strap, popup, out var strapComp, buckleComp))
            return false;

        Buckle((buckle, buckleComp), (strap, strapComp), user);
        return true;
    }

    public void Buckle(Entity<BuckleComponent> buckle, Entity<StrapComponent> strap, EntityUid? user)
    {
        if (user == buckle.Owner)
            _adminLogger.Add(LogType.Action, LogImpact.Low, $"{ToPrettyString(user):player} buckled themselves to {ToPrettyString(strap)}");
        else if (user != null)
            _adminLogger.Add(LogType.Action, LogImpact.Low, $"{ToPrettyString(user):player} buckled {ToPrettyString(buckle)} to {ToPrettyString(strap)}");

        var strapEv = new StrappedEvent(strap, buckle);
        RaiseLocalEvent(strap, ref strapEv);

        var misfitsEv = new ActorStrappedEvent(strap, buckle, user);
        RaiseLocalEvent(strap, misfitsEv);

        var buckleEv = new BuckledEvent(strap, buckle);
        RaiseLocalEvent(buckle, ref buckleEv);

        var xform = Transform(buckle);
        var coords = new EntityCoordinates(strap, strap.Comp.BuckleOffset);
        _transform.SetCoordinates(buckle, xform, coords, rotation: strap.Comp.Rotation);
        DebugTools.AssertEqual(xform.ParentUid, strap.Owner);


        BuckleRequiredSetup(buckle, strap);
        BuckleVisual(buckle,strap);
        SetBuckledTo(buckle, strap!);

        // incase whatever is buckled had physics
        // we reset their velocity ect... to 0 so they dont slide off the buckle
        if (TryComp<PhysicsComponent>(buckle, out var physics))
            _physics.ResetDynamics(buckle, physics);

    }

    /// <summary>
    /// Tries to unbuckle the Owner of this component from its current strap.
    /// </summary>
    /// <param name="buckleUid">The entity to unbuckle.</param>
    /// <param name="user">The entity doing the unbuckling.</param>
    /// <param name="buckleComp">The buckle component of the entity to unbuckle.</param>
    /// <returns>
    ///     true if the owner was unbuckled, otherwise false even if the owner
    ///     was previously already unbuckled.
    /// </returns>
    public bool TryUnbuckle(EntityUid buckleUid,
        EntityUid? user = null,
        BuckleComponent? buckleComp = null,
        bool popup = true)
    {
        return TryUnbuckle((buckleUid, buckleComp), user, popup);
    }

    public bool TryUnbuckle(Entity<BuckleComponent?> buckle, EntityUid? user, bool popup)
    {
        if (!Resolve(buckle.Owner, ref buckle.Comp))
            return false;

        if (!CanUnbuckle(buckle, user, popup, out var strap))
            return false;

        // WD EDIT START
        if (buckle.Owner == user && strap.Comp.SelfUnBuckleDelay != TimeSpan.Zero)
        {
            var doAfter = new DoAfterArgs(EntityManager, buckle.Owner, strap.Comp.SelfUnBuckleDelay, new UnbuckleDoAfterEvent(), buckle.Owner);
            return _doAfter.TryStartDoAfter(doAfter);
        }
        // WD EDIT END

        // Goobstation - doafter for unbuckle by others
        if (user != null && buckle.Owner != user && strap.Comp.UnbuckleDoafterTime > 0)
        {
            var doAfter = new DoAfterArgs(EntityManager, user.Value, TimeSpan.FromSeconds(strap.Comp.UnbuckleDoafterTime), new UnbuckleDoAfterEvent(), buckle.Owner, target: buckle.Owner)
            {
                BreakOnMove = true,
                BreakOnDamage = true,
            };
            return _doAfter.TryStartDoAfter(doAfter);
        }
        // Goobstation

        Unbuckle(buckle!, strap, user);
        return true;
    }



    public void Unbuckle(Entity<BuckleComponent?> buckle, EntityUid? user)
    {
        if (!Resolve(buckle.Owner, ref buckle.Comp, false))
            return;

        if (buckle.Comp.BuckledTo is not { } strap)
            return;

        if (!TryComp(strap, out StrapComponent? strapComp))
        {
            Log.Error($"Encountered buckle {ToPrettyString(buckle.Owner)} with invalid strap entity {ToPrettyString(strap)}");
            SetBuckledTo(buckle!, null);
            return;
        }

        Unbuckle(buckle!, (strap, strapComp), user);
    }

    public void Unbuckle(Entity<BuckleComponent> buckle, Entity<StrapComponent> strap, EntityUid? user)
    {
        if (user == buckle.Owner)
            _adminLogger.Add(LogType.Action, LogImpact.Low, $"{user} unbuckled themselves from {strap}");
        else if (user != null)
            _adminLogger.Add(LogType.Action, LogImpact.Low, $"{user} unbuckled {buckle} from {strap}");

        var buckleEv = new UnbuckledEvent(strap, buckle);
        RaiseLocalEvent(buckle, ref buckleEv);

        var strapEv = new UnstrappedEvent(strap, buckle);
        RaiseLocalEvent(strap, ref strapEv);

        var misfitsEv = new ActorUnstrappedEvent(strap, buckle, user);
        RaiseLocalEvent(strap, misfitsEv);

        UnbuckleCoord(buckle, strap);
        UnbuckleRequiredSetup(buckle, strap);
        UnbuckleKnockdown(buckle, strap);
        UnbuckleVisuals(buckle, strap);

    }
    public bool CanUnbuckle(Entity<BuckleComponent?> buckle, EntityUid user, bool popup)
    {
        return CanUnbuckle(buckle, user, popup, out _);
    }

    private bool CanUnbuckle(Entity<BuckleComponent?> buckle, EntityUid? user, bool popup, out Entity<StrapComponent> strap)
    {
        strap = default;
        if (!Resolve(buckle.Owner, ref buckle.Comp))
            return false;

        if (buckle.Comp.BuckledTo is not { } strapUid)
            return false;

        if (!TryComp(strapUid, out StrapComponent? strapComp))
        {
            Log.Error($"Encountered buckle {ToPrettyString(buckle.Owner)} with invalid strap entity {ToPrettyString(strap)}");
            SetBuckledTo(buckle!, null);
            return false;
        }

        strap = (strapUid, strapComp);
        if (_gameTiming.CurTime < buckle.Comp.BuckleTime + buckle.Comp.Delay)
            return false;

        if (user != null && !_interaction.InRangeUnobstructed(user.Value, strap.Owner, buckle.Comp.Range, popup: popup))
            return false;

        var unbuckleAttempt = new UnbuckleAttemptEvent(strap, buckle!, user, popup);
        RaiseLocalEvent(buckle, ref unbuckleAttempt);
        if (unbuckleAttempt.Cancelled)
            return false;

        var unstrapAttempt = new UnstrapAttemptEvent(strap, buckle!, user, popup);
        RaiseLocalEvent(strap, ref unstrapAttempt);
        return !unstrapAttempt.Cancelled;
    }
    private void OnBuckleDoafter(Entity<BuckleComponent> entity, ref BuckleDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || args.Target == null || args.Used == null)
            return;

        args.Handled = TryBuckle(args.Target.Value, args.User, args.Used.Value, popup: false);
    }

    /// <summary>
    /// If the target being buckled to a chair/bed goes crit or is cuffed
    /// Cancel the do-after time and try to buckle the target immediately
    /// </summary>
    /// <param name="args.Target"> The person being put in the chair/bed</param>
    /// <param name="args.User"> The person putting a person in a chair/bed</param>
    /// <param name="args.Used"> The chair/bed </param>
    private void BuckleDoafterEarly(Entity<BuckleComponent> entity, BuckleDoAfterEvent args, CancellableEntityEventArgs ev)
    {
        if (args.Target == null || args.Used == null)
            return;

        if (TryComp<CuffableComponent>(args.Target, out var targetCuffableComp) && targetCuffableComp.CuffedHandCount > 0
            || _mobState.IsIncapacitated(args.Target.Value))
        {
            ev.Cancel();
            TryBuckle(args.Target.Value, args.User, args.Used.Value, popup: false);
        }
    }

}
