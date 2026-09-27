using System.Numerics;
using Content.Shared.ActionBlocker;
using Content.Shared.Actions;
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

namespace Content.Shared._Misfits.Harpoon;

public sealed class HarpoonSystem : EntitySystem
{
    [Dependency] private INetManager _net = default!;
    [Dependency] private ActionBlockerSystem _blocker = default!;
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedMoverController _mover = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private const float ReelStopMargin = 0.15f;

    private const float StruggleDot = 0.5f;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<HarpoonComponent, ThrownEvent>(OnThrown);
        SubscribeLocalEvent<HarpoonComponent, EmbedEvent>(OnEmbed);
        SubscribeLocalEvent<HarpoonComponent, RemoveEmbedEvent>(OnRemoveEmbed);
        SubscribeLocalEvent<HarpoonComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<HarpoonComponent, HarpoonReelActionEvent>(OnReelAction);
    }

    private void OnThrown(Entity<HarpoonComponent> ent, ref ThrownEvent args)
    {
        ent.Comp.Thrower = args.User;
    }

    private void OnEmbed(Entity<HarpoonComponent> ent, ref EmbedEvent args)
    {
        if (_net.IsClient)
            return;

        var thrower = args.Shooter ?? ent.Comp.Thrower;
        if (ent.Comp.Hooked != null)
            Unhook(ent);

        if (thrower is not { } user || user == args.Embedded || TerminatingOrDeleted(user))
            return;

        if (TryComp<HarpoonedComponent>(args.Embedded, out var existing)
            && TryComp<HarpoonComponent>(existing.Harpoon, out var oldHarpoon))
        {
            Unhook((existing.Harpoon.Value, oldHarpoon));
        }

        ent.Comp.Thrower = user;
        ent.Comp.Hooked = args.Embedded;
        ent.Comp.Strain = 0f;
        ent.Comp.StrainWarned = false;

        var harpooned = EnsureComp<HarpoonedComponent>(args.Embedded);
        harpooned.Harpoon = ent;
        harpooned.Thrower = user;
        harpooned.Reeling = false;
        harpooned.ReelSpeed = ent.Comp.ReelSpeed;
        harpooned.StruggleModifier = ent.Comp.StruggleModifier;
        harpooned.MinDistance = ent.Comp.MinDistance;
        harpooned.MaxRopeLength = ent.Comp.MaxRopeLength;
        Dirty(args.Embedded, harpooned);

        var visuals = EnsureComp<JointVisualsComponent>(ent);
        visuals.Sprite = ent.Comp.RopeSprite;
        visuals.Target = user;
        Dirty(ent, visuals);

        _actions.AddAction(user, ref ent.Comp.ReelActionEntity, ent.Comp.ReelAction, ent);
        _actions.SetToggled(ent.Comp.ReelActionEntity, false);
        _popup.PopupEntity(Loc.GetString("harpoon-hooked", ("harpoon", ent)), args.Embedded, args.Embedded, PopupType.MediumCaution);
    }

    private void OnRemoveEmbed(Entity<HarpoonComponent> ent, ref RemoveEmbedEvent args)
    {
        Unhook(ent);
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

        if (!CanBeHauled(hooked))
        {
            _popup.PopupEntity(Loc.GetString("harpoon-reel-stuck", ("target", hooked)), args.Performer, args.Performer);
            return;
        }

        SetReeling(ent, (hooked, harpooned), true);
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

    private void Unhook(Entity<HarpoonComponent> ent)
    {
        if (_net.IsClient)
            return;

        if (TryComp<HarpoonedComponent>(ent.Comp.Hooked, out var harpooned) && harpooned.Harpoon == ent.Owner)
        {
            harpooned.ReelStream = _audio.Stop(harpooned.ReelStream);
            RemComp(ent.Comp.Hooked.Value, harpooned);
        }

        _actions.RemoveAction(ent.Comp.ReelActionEntity);
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
                || xform.ParentUid != hooked
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

            if (distance > harpoon.SnapLength || !hauled && distance > harpoon.MaxRopeLength)
            {
                Snap((uid, harpoon));
                continue;
            }

            if (distance > 0.01f && IsStruggling(hooked, away))
            {
                harpoon.Strain += frameTime;
                if (harpoon.Strain >= harpoon.BreakStrain)
                {
                    Snap((uid, harpoon));
                    continue;
                }

                if (!harpoon.StrainWarned && harpoon.Strain >= harpoon.BreakStrain / 2f)
                {
                    harpoon.StrainWarned = true;
                    _popup.PopupEntity(Loc.GetString("harpoon-rope-fraying"), hooked, PopupType.SmallCaution);
                }
            }
            else
            {
                harpoon.Strain = MathF.Max(0f, harpoon.Strain - harpoon.StrainRecovery * frameTime);
            }

            if (harpooned.Reeling
                && (!hauled
                    || distance <= harpoon.MinDistance + ReelStopMargin
                    || !_blocker.CanInteract(thrower, hooked)))
            {
                SetReeling((uid, harpoon), (hooked, harpooned), false);
            }
        }
    }
}
