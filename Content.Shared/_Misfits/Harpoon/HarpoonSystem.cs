using System.Numerics;
using Content.Shared.ActionBlocker;
using Content.Shared.Actions;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Physics;
using Content.Shared.Popups;
using Content.Shared.Projectiles;
using Content.Shared.Throwing;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Network;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.Shared._Misfits.Harpoon;

public sealed class HarpoonSystem : EntitySystem
{
    [Dependency] private INetManager _net = default!;
    [Dependency] private ActionBlockerSystem _blocker = default!;
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedMoverController _mover = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedProjectileSystem _projectile = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private const float ReelStopMargin = 0.15f;

    private const float StruggleDot = 0.5f;

    private static readonly TimeSpan MinFlightTime = TimeSpan.FromSeconds(ThrowingSystem.MinFlyTime);

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<HarpoonComponent, ThrownEvent>(OnThrown);
        SubscribeLocalEvent<HarpoonComponent, LandEvent>(OnLand);
        SubscribeLocalEvent<HarpoonComponent, EmbedEvent>(OnEmbed);
        SubscribeLocalEvent<HarpoonComponent, RemoveEmbedEvent>(OnRemoveEmbed);
        SubscribeLocalEvent<HarpoonComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<HarpoonComponent, EntGotInsertedIntoContainerMessage>(OnInserted);
        SubscribeLocalEvent<HarpoonComponent, EntGotRemovedFromContainerMessage>(OnRemoved);
        SubscribeLocalEvent<HarpoonComponent, HarpoonReelActionEvent>(OnReelAction);
        SubscribeLocalEvent<HarpoonComponent, HarpoonYankActionEvent>(OnYankAction);
    }

    private void OnThrown(Entity<HarpoonComponent> ent, ref ThrownEvent args)
    {
        var owner = args.User;
        if (ent.Comp.Hooked != null && ent.Comp.Thrower is { } existing && !TerminatingOrDeleted(existing))
            owner = existing;

        ent.Comp.Thrower = owner;

        if (TryComp<ThrownItemComponent>(ent, out var thrown)
            && thrown.ThrownTime is { } thrownTime
            && (thrown.LandTime == null || thrown.LandTime < thrownTime + MinFlightTime))
        {
            thrown.LandTime = thrownTime + MinFlightTime;
        }

        if (_net.IsClient || owner is not { } user || TerminatingOrDeleted(user))
            return;

        Attach(ent, user, ent);
    }

    private void OnLand(Entity<HarpoonComponent> ent, ref LandEvent args)
    {
        _physics.SetLinearVelocity(ent, Vector2.Zero);
    }

    private void OnEmbed(Entity<HarpoonComponent> ent, ref EmbedEvent args)
    {
        if (_net.IsClient)
            return;

        var thrower = args.Shooter ?? ent.Comp.Thrower;
        if (thrower is not { } user || user == args.Embedded || TerminatingOrDeleted(user))
        {
            Unhook(ent);
            return;
        }

        Attach(ent, user, args.Embedded);
        _popup.PopupEntity(Loc.GetString("harpoon-hooked", ("harpoon", ent)), args.Embedded, args.Embedded, PopupType.MediumCaution);
    }

    private void OnRemoveEmbed(Entity<HarpoonComponent> ent, ref RemoveEmbedEvent args)
    {
        if (_net.IsClient)
            return;

        if (ent.Comp.Thrower is { } thrower && !TerminatingOrDeleted(thrower))
            Attach(ent, thrower, ent);
        else
            Unhook(ent);
    }

    private void OnInserted(Entity<HarpoonComponent> ent, ref EntGotInsertedIntoContainerMessage args)
    {
        if (_net.IsClient || ent.Comp.Hooked == null || ent.Comp.Thrower is not { } thrower)
            return;

        if (!_container.TryGetOuterContainer(ent, Transform(ent), out var outer) || outer.Owner == thrower)
        {
            Unhook(ent);
            return;
        }

        Attach(ent, thrower, outer.Owner);
    }

    private void OnRemoved(Entity<HarpoonComponent> ent, ref EntGotRemovedFromContainerMessage args)
    {
        if (_net.IsClient
            || TerminatingOrDeleted(ent)
            || ent.Comp.Hooked == null
            || ent.Comp.Thrower is not { } thrower
            || TerminatingOrDeleted(thrower))
            return;

        if (_container.TryGetOuterContainer(ent, Transform(ent), out var outer))
            Attach(ent, thrower, outer.Owner);
        else
            Attach(ent, thrower, ent);
    }

    private bool IsAttachedTo(EntityUid uid, TransformComponent xform, EntityUid hooked)
    {
        if (_container.TryGetOuterContainer(uid, xform, out var outer))
            return outer.Owner == hooked;

        return hooked == uid || xform.ParentUid == hooked;
    }

    private void Free(Entity<HarpoonComponent> ent)
    {
        if (_container.IsEntityInContainer(ent))
            _container.TryRemoveFromContainer(ent.Owner);
        else if (TryComp<EmbeddableProjectileComponent>(ent, out var embed))
            _projectile.RemoveEmbed(ent, embed);
    }

    private void Attach(Entity<HarpoonComponent> ent, EntityUid user, EntityUid target)
    {
        ClearHooked(ent);

        if (TryComp<HarpoonedComponent>(target, out var existing)
            && existing.Harpoon != ent.Owner
            && TryComp<HarpoonComponent>(existing.Harpoon, out var oldHarpoon))
        {
            Unhook((existing.Harpoon.Value, oldHarpoon));
        }

        ent.Comp.Thrower = user;
        ent.Comp.Hooked = target;
        ent.Comp.Strain = 0f;
        ent.Comp.StrainWarned = false;

        var harpooned = EnsureComp<HarpoonedComponent>(target);
        harpooned.Harpoon = ent;
        harpooned.Thrower = user;
        harpooned.Reeling = false;
        harpooned.ReelSpeed = target == ent.Owner ? ent.Comp.LooseReelSpeed : ent.Comp.ReelSpeed;
        harpooned.StruggleModifier = ent.Comp.StruggleModifier;
        harpooned.MinDistance = ent.Comp.MinDistance;
        harpooned.MaxRopeLength = ent.Comp.MaxRopeLength;
        Dirty(target, harpooned);

        var visuals = EnsureComp<JointVisualsComponent>(ent);
        visuals.Sprite = ent.Comp.RopeSprite;
        visuals.Target = user;
        Dirty(ent, visuals);

        _actions.AddAction(user, ref ent.Comp.ReelActionEntity, ent.Comp.ReelAction, ent);
        _actions.SetToggled(ent.Comp.ReelActionEntity, false);

        if (target == ent.Owner)
            _actions.RemoveAction(ent.Comp.YankActionEntity);
        else
            _actions.AddAction(user, ref ent.Comp.YankActionEntity, ent.Comp.YankAction, ent);
    }

    private void ClearHooked(Entity<HarpoonComponent> ent)
    {
        if (!TryComp<HarpoonedComponent>(ent.Comp.Hooked, out var harpooned) || harpooned.Harpoon != ent.Owner)
            return;

        harpooned.ReelStream = _audio.Stop(harpooned.ReelStream);
        RemComp(ent.Comp.Hooked.Value, harpooned);
    }

    private void OnShutdown(Entity<HarpoonComponent> ent, ref ComponentShutdown args)
    {
        Unhook(ent);
    }

    private void OnReelAction(Entity<HarpoonComponent> ent, ref HarpoonReelActionEvent args)
    {
        if (args.Handled || _net.IsClient)
            return;

        if (ent.Comp.Hooked is not { } hooked
            || ent.Comp.Thrower != args.Performer
            || !TryComp<HarpoonedComponent>(hooked, out var harpooned))
            return;

        args.Handled = true;

        if (harpooned.Reeling)
        {
            SetReeling(ent, (hooked, harpooned), false);
            return;
        }

        if (!CanBeHauled(hooked) && !CanGrapple(hooked, args.Performer))
        {
            _popup.PopupEntity(Loc.GetString("harpoon-reel-stuck", ("target", hooked)), args.Performer, args.Performer);
            return;
        }

        SetReeling(ent, (hooked, harpooned), true);
    }

    private void OnYankAction(Entity<HarpoonComponent> ent, ref HarpoonYankActionEvent args)
    {
        if (args.Handled || _net.IsClient)
            return;

        if (ent.Comp.Thrower != args.Performer
            || ent.Comp.Hooked is not { } hooked
            || hooked == ent.Owner)
            return;

        args.Handled = true;
        _popup.PopupEntity(Loc.GetString("harpoon-yanked-free", ("harpoon", ent), ("target", hooked)), ent, PopupType.MediumCaution);
        Free(ent);
    }

    private bool CanGrapple(EntityUid hooked, EntityUid thrower)
    {
        return !_container.IsEntityOrParentInContainer(hooked) && CanBeHauled(thrower);
    }

    public bool CanBeHauled(EntityUid uid, TransformComponent? xform = null, PhysicsComponent? body = null)
    {
        if (!Resolve(uid, ref xform, ref body, false))
            return false;

        return !xform.Anchored
            && (body.BodyType & (BodyType.Dynamic | BodyType.KinematicController)) != 0x0
            && !_container.IsEntityOrParentInContainer(uid, xform: xform);
    }

    private void SetReeling(Entity<HarpoonComponent> ent, Entity<HarpoonedComponent> hooked, bool reeling)
    {
        if (hooked.Comp.Reeling == reeling)
            return;

        hooked.Comp.Reeling = reeling;
        Dirty(hooked);

        _actions.SetToggled(ent.Comp.ReelActionEntity, reeling);

        hooked.Comp.ReelStream = _audio.Stop(hooked.Comp.ReelStream);
        if (reeling && ent.Comp.Thrower is { } thrower)
            hooked.Comp.ReelStream = _audio.PlayPvs(ent.Comp.ReelSound, thrower)?.Entity;
    }

    private bool IsStruggling(EntityUid hooked, Vector2 away)
    {
        if (!TryComp<InputMoverComponent>(hooked, out var mover) || !mover.CanMove)
            return false;

        var (walk, sprint) = _mover.GetVelocityInput(mover);
        var input = _mover.GetParentGridAngle(mover).RotateVec(walk + sprint);
        if (input.LengthSquared() < 0.01f)
            return false;

        return Vector2.Dot(input.Normalized(), away.Normalized()) > StruggleDot;
    }

    private void Snap(Entity<HarpoonComponent> ent)
    {
        _popup.PopupEntity(Loc.GetString("harpoon-rope-snap"), ent, PopupType.MediumCaution);
        _audio.PlayPvs(ent.Comp.SnapSound, ent);
        Unhook(ent);
    }

    private void Dislodge(Entity<HarpoonComponent> ent)
    {
        if (ent.Comp.Hooked == ent.Owner)
        {
            Snap(ent);
            return;
        }

        _popup.PopupEntity(Loc.GetString("harpoon-torn-free", ("harpoon", ent)), ent, PopupType.MediumCaution);
        _audio.PlayPvs(ent.Comp.SnapSound, ent);
        Free(ent);
    }

    private void Unhook(Entity<HarpoonComponent> ent)
    {
        if (_net.IsClient)
            return;

        ClearHooked(ent);
        _actions.RemoveAction(ent.Comp.ReelActionEntity);
        _actions.RemoveAction(ent.Comp.YankActionEntity);
        RemComp<JointVisualsComponent>(ent);
        ent.Comp.Thrower = null;
        ent.Comp.Hooked = null;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_net.IsClient)
            return;

        var query = EntityQueryEnumerator<HarpoonComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var harpoon, out var xform))
        {
            if (harpoon.Hooked is not { } hooked || harpoon.Thrower is not { } thrower)
                continue;

            if (TerminatingOrDeleted(hooked)
                || TerminatingOrDeleted(thrower)
                || !IsAttachedTo(uid, xform, hooked)
                || !TryComp<HarpoonedComponent>(hooked, out var harpooned))
            {
                Unhook((uid, harpoon));
                continue;
            }

            var hookedXform = Transform(hooked);
            var throwerXform = Transform(thrower);
            if (hookedXform.MapID != throwerXform.MapID)
            {
                Snap((uid, harpoon));
                continue;
            }

            var away = _transform.GetWorldPosition(hookedXform) - _transform.GetWorldPosition(throwerXform);
            var distance = away.Length();
            var hauled = CanBeHauled(hooked, hookedXform);

            if (distance > harpoon.SnapLength)
            {
                Snap((uid, harpoon));
                continue;
            }

            if (!hauled && distance > harpoon.MaxRopeLength)
            {
                Dislodge((uid, harpoon));
                continue;
            }

            if (distance > 0.01f && IsStruggling(hooked, away))
            {
                harpoon.Strain += frameTime;
                if (harpoon.Strain >= harpoon.BreakStrain)
                {
                    Dislodge((uid, harpoon));
                    continue;
                }

                if (!harpoon.StrainWarned && harpoon.Strain >= harpoon.BreakStrain / 2f)
                {
                    harpoon.StrainWarned = true;
                    _popup.PopupEntity(Loc.GetString("harpoon-tearing", ("harpoon", uid)), hooked, PopupType.SmallCaution);
                }
            }
            else
            {
                harpoon.Strain = MathF.Max(0f, harpoon.Strain - harpoon.StrainRecovery * frameTime);
            }

            if (harpooned.Reeling
                && hooked == uid
                && distance <= harpoon.MinDistance + ReelStopMargin
                && _hands.TryPickupAnyHand(thrower, uid))
            {
                continue;
            }

            if (harpooned.Reeling
                && hooked != uid
                && !hauled
                && distance <= harpoon.MinDistance + ReelStopMargin
                && TryComp<EmbeddableProjectileComponent>(uid, out var embed)
                && embed.Target == hooked)
            {
                _projectile.RemoveEmbed(uid, embed, thrower);
                continue;
            }

            if (harpooned.Reeling
                && (!hauled && !CanGrapple(hooked, thrower)
                    || distance <= harpoon.MinDistance + ReelStopMargin
                    || !_blocker.CanInteract(thrower, hooked)))
            {
                SetReeling((uid, harpoon), (hooked, harpooned), false);
            }
        }
    }
}
