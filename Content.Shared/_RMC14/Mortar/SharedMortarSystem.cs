using System.Numerics;
using Content.Shared.CombatMode;
using Content.Shared.Construction.Components;
using Content.Shared.Coordinates;
using Content.Shared.Damage;
using Content.Shared.Destructible;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Content.Shared.Maps;
using Content.Shared.Popups;
using Content.Shared.UserInterface;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Shared._RMC14.Mortar;

public abstract partial class SharedMortarSystem : EntitySystem
{
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private FixtureSystem _fixture = default!;
    [Dependency] protected SharedMapSystem _map = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private ISharedPlayerManager _player = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedRoofSystem _roof = default!;
    [Dependency] private ITileDefinitionManager _tile = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] protected SharedTransformSystem _transform = default!;

    private EntityQuery<TransformComponent> _transformQuery;

    public override void Initialize()
    {
        _transformQuery = GetEntityQuery<TransformComponent>();

        SubscribeLocalEvent<MortarComponent, UseInHandEvent>(OnMortarUseInHand, before: [typeof(ActivatableUISystem)]);
        SubscribeLocalEvent<MortarComponent, DeployMortarDoAfterEvent>(OnMortarDeployDoAfter);
        SubscribeLocalEvent<MortarComponent, TargetMortarDoAfterEvent>(OnMortarTargetDoAfter);
        SubscribeLocalEvent<MortarComponent, DialMortarDoAfterEvent>(OnMortarDialDoAfter);
        SubscribeLocalEvent<MortarComponent, InteractUsingEvent>(OnMortarInteractUsing);
        SubscribeLocalEvent<MortarComponent, LoadMortarShellDoAfterEvent>(OnMortarLoadDoAfter);
        SubscribeLocalEvent<MortarComponent, AnchorStateChangedEvent>(OnMortarAnchorStateChanged);
        SubscribeLocalEvent<MortarComponent, ExaminedEvent>(OnMortarExamined);
        SubscribeLocalEvent<MortarComponent, ActivatableUIOpenAttemptEvent>(OnMortarActivatableUIOpenAttempt);
        SubscribeLocalEvent<MortarComponent, CombatModeShouldHandInteractEvent>(OnMortarShouldInteract);
        SubscribeLocalEvent<MortarComponent, DestructionEventArgs>(OnMortarDestruction);
        SubscribeLocalEvent<MortarComponent, BeforeDamageChangedEvent>(OnMortarBeforeDamageChanged);

        Subs.BuiEvents<MortarComponent>(MortarUiKey.Key,
            subs =>
            {
                subs.Event<MortarTargetBuiMsg>(OnMortarTargetBui);
                subs.Event<MortarDialBuiMsg>(OnMortarDialBui);
            });
    }

    private void OnMortarBeforeDamageChanged(Entity<MortarComponent> ent, ref BeforeDamageChangedEvent args)
    {
        if (!ent.Comp.Deployed) // cannot destroy in item form
            args.Cancelled = true;
    }

    private void OnMortarDestruction(Entity<MortarComponent> mortar, ref DestructionEventArgs args)
    {
        if (!mortar.Comp.Deployed || _net.IsClient)
            return;

        SpawnAtPosition(mortar.Comp.Drop, mortar.Owner.ToCoordinates());
    }

    private void OnMortarUseInHand(Entity<MortarComponent> mortar, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;

        if (mortar.Comp.Deployed)
            return;

        var ev = new DeployMortarDoAfterEvent();
        var doAfter = new DoAfterArgs(EntityManager, args.User, mortar.Comp.DeployDelay, ev, mortar)
        {
            BreakOnMove = true,
            BreakOnHandChange = true,
        };

        // This handler runs predicted on the client, so the popup has to be the predicted variant or the user
        // sees it twice: once from their own prediction and once from the server.
        if (_doAfter.TryStartDoAfter(doAfter))
            _popup.PopupClient(Loc.GetString("rmc-mortar-deploy-start", ("mortar", mortar)), mortar.Owner, args.User);
    }

    private void OnMortarDeployDoAfter(Entity<MortarComponent> mortar, ref DeployMortarDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;

        if (_net.IsClient || mortar.Comp.Deployed)
            return;

        mortar.Comp.Deployed = true;
        Dirty(mortar);

        if (_fixture.GetFixtureOrNull(mortar.Owner, mortar.Comp.FixtureId) is { } fixture)
            _physics.SetHard(mortar.Owner, fixture, true);

        _appearance.SetData(mortar.Owner, MortarVisualLayers.State, MortarVisuals.Deployed);

        var xform = Transform(mortar);
        var coordinates = _transform.GetMoverCoordinates(mortar.Owner, xform);
        var rotation = Transform(args.User).LocalRotation.GetCardinalDir().ToAngle();
        _transform.SetCoordinates(mortar.Owner, xform, coordinates, rotation);
        _transform.AnchorEntity(mortar.Owner, xform);

        _audio.PlayPvs(mortar.Comp.DeploySound, mortar.Owner);
    }

    private void OnMortarTargetDoAfter(Entity<MortarComponent> mortar, ref TargetMortarDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;

        var user = args.User;
        var mortarCoordinates = _transform.GetMapCoordinates(mortar);

        // Checked here as well as when the aim starts, in case the ground changed during the do-after.
        if (RefuseRoofedTarget(mortar, args.Vector, user))
            return;

        var selfMsg = Loc.GetString("rmc-mortar-target-finish-self", ("mortar", mortar));
        var othersMsg = Loc.GetString("rmc-mortar-target-finish-others", ("user", user), ("mortar", mortar));
        _popup.PopupPredicted(selfMsg, othersMsg, mortar.Owner, user);

        if (_net.IsClient)
            return;

        // Less accurate the further out the target is.
        var target = args.Vector;
        var position = mortarCoordinates.Position;
        var tilesPer = MathF.Max(mortar.Comp.TilesPerOffset, 1);
        var xOffset = (int) MathF.Floor(MathF.Abs(target.X - position.X) / tilesPer);
        var yOffset = (int) MathF.Floor(MathF.Abs(target.Y - position.Y) / tilesPer);

        mortar.Comp.Target = target;
        mortar.Comp.HasTarget = true;
        mortar.Comp.Offset = (_random.Next(-xOffset, xOffset + 1), _random.Next(-yOffset, yOffset + 1));
        Dirty(mortar);
    }

    private void OnMortarDialDoAfter(Entity<MortarComponent> mortar, ref DialMortarDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;

        if (_net.IsClient)
            return;

        mortar.Comp.Dial = args.Vector;
        Dirty(mortar);

        var user = args.User;
        var selfMsg = Loc.GetString("rmc-mortar-dial-finish-self", ("mortar", mortar));
        var othersMsg = Loc.GetString("rmc-mortar-dial-finish-others", ("user", user), ("mortar", mortar));
        _popup.PopupPredicted(selfMsg, othersMsg, mortar.Owner, user);
    }

    private void OnMortarInteractUsing(Entity<MortarComponent> mortar, ref InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        var used = args.Used;
        var user = args.User;

        if (!TryComp(used, out MortarShellComponent? shell))
            return;

        if (_net.IsClient)
            return;

        args.Handled = true;

        if (!mortar.Comp.Deployed)
        {
            _popup.PopupEntity(Loc.GetString("rmc-mortar-not-deployed", ("mortar", mortar)), user, user, PopupType.SmallCaution);
            return;
        }

        if (!TryGetShot(mortar, shell, user, out _, out _))
            return;

        var ev = new LoadMortarShellDoAfterEvent();
        var doAfter = new DoAfterArgs(EntityManager, user, shell.LoadDelay, ev, mortar, mortar, used)
        {
            BreakOnMove = true,
            BreakOnHandChange = true,
        };

        if (!_doAfter.TryStartDoAfter(doAfter))
            return;

        var selfMsg = Loc.GetString("rmc-mortar-shell-load-start-self", ("mortar", mortar), ("shell", used));
        var othersMsg = Loc.GetString("rmc-mortar-shell-load-start-others",
            ("user", user),
            ("mortar", mortar),
            ("shell", used));
        PopupSelfAndOthers(selfMsg, othersMsg, user, mortar.Owner);

        _audio.PlayPvs(mortar.Comp.ReloadSound, mortar.Owner);
    }

    private void OnMortarLoadDoAfter(Entity<MortarComponent> mortar, ref LoadMortarShellDoAfterEvent args)
    {
        var user = args.User;
        if (args.Cancelled || args.Handled || args.Used is not { } shellId)
            return;

        args.Handled = true;

        if (_net.IsClient)
            return;

        if (!TryComp(shellId, out MortarShellComponent? shell))
            return;

        if (!mortar.Comp.Deployed)
            return;

        if (HasComp<ActiveMortarShellComponent>(shellId))
            return;

        if (!TryGetShot(mortar, shell, user, out var coordinates, out var travelTime))
            return;

        var container = _container.EnsureContainer<Container>(mortar.Owner, mortar.Comp.ContainerId);
        if (!_container.Insert(shellId, container))
            return;

        var time = _timing.CurTime;
        mortar.Comp.LastFiredAt = time;
        Dirty(mortar);

        AddComp(shellId,
            new ActiveMortarShellComponent
            {
                Coordinates = _transform.ToCoordinates(coordinates),
                WarnAt = time + travelTime,
                ImpactWarnAt = time + travelTime + shell.ImpactWarningDelay,
                LandAt = time + travelTime + shell.ImpactDelay,
            },
            true);

        var selfMsg = Loc.GetString("rmc-mortar-shell-load-finish-self", ("mortar", mortar), ("shell", shellId));
        var othersMsg = Loc.GetString("rmc-mortar-shell-load-finish-others", ("user", user), ("mortar", mortar), ("shell", shellId));
        PopupSelfAndOthers(selfMsg, othersMsg, user, mortar.Owner);

        othersMsg = Loc.GetString("rmc-mortar-shell-fire", ("mortar", mortar));
        _popup.PopupEntity(othersMsg, mortar.Owner, PopupType.MediumCaution);

        _audio.PlayPvs(mortar.Comp.FireSound, mortar.Owner);

        var fired = new MortarFiredEvent(GetNetEntity(mortar.Owner));
        RaiseNetworkEvent(fired, Filter.Pvs(mortar.Owner));
    }

    private void OnMortarAnchorStateChanged(Entity<MortarComponent> mortar, ref AnchorStateChangedEvent args)
    {
        if (args.Anchored)
            return;

        mortar.Comp.Deployed = false;
        Dirty(mortar);

        if (_fixture.GetFixtureOrNull(mortar.Owner, mortar.Comp.FixtureId) is { } fixture)
            _physics.SetHard(mortar.Owner, fixture, false);

        _appearance.SetData(mortar.Owner, MortarVisualLayers.State, MortarVisuals.Item);
    }

    private void OnMortarExamined(Entity<MortarComponent> ent, ref ExaminedEvent args)
    {
        using (args.PushGroup(nameof(MortarComponent)))
        {
            args.PushMarkup(Loc.GetString("rmc-mortar-less-accurate-with-range"));

            if (!ent.Comp.Deployed)
                args.PushMarkup(Loc.GetString("rmc-mortar-examine-not-deployed", ("mortar", ent)));
            else
                args.PushMarkup(Loc.GetString("rmc-mortar-examine-aimed",
                    ("mortar", ent),
                    ("x", ent.Comp.Target.X + ent.Comp.Offset.X + ent.Comp.Dial.X),
                    ("y", ent.Comp.Target.Y + ent.Comp.Offset.Y + ent.Comp.Dial.Y)));

            var position = _transform.GetMapCoordinates(ent).Position;
            args.PushMarkup(Loc.GetString("rmc-mortar-examine-position",
                ("mortar", ent),
                ("x", (int) MathF.Round(position.X)),
                ("y", (int) MathF.Round(position.Y))));
        }
    }

    private void OnMortarActivatableUIOpenAttempt(Entity<MortarComponent> ent, ref ActivatableUIOpenAttemptEvent args)
    {
        if (args.Cancelled)
            return;

        if (!ent.Comp.Deployed)
            args.Cancel();
    }

    private void OnMortarShouldInteract(Entity<MortarComponent> ent, ref CombatModeShouldHandInteractEvent args)
    {
        args.Cancelled = true;
    }

    private void OnMortarTargetBui(Entity<MortarComponent> mortar, ref MortarTargetBuiMsg args)
    {
        var target = new Vector2i(
            Math.Clamp(args.Target.X, -mortar.Comp.MaxTarget, mortar.Comp.MaxTarget),
            Math.Clamp(args.Target.Y, -mortar.Comp.MaxTarget, mortar.Comp.MaxTarget));

        var user = args.Actor;

        // Refuse the aim up front rather than letting them wind the mortar onto a roof they can't hit.
        if (RefuseRoofedTarget(mortar, target, user))
            return;

        var ev = new TargetMortarDoAfterEvent(target);
        var doAfter = new DoAfterArgs(EntityManager, user, mortar.Comp.TargetDelay, ev, mortar)
        {
            BreakOnMove = true,
        };

        if (!_doAfter.TryStartDoAfter(doAfter))
            return;

        var selfMsg = Loc.GetString("rmc-mortar-target-start-self", ("mortar", mortar));
        var othersMsg = Loc.GetString("rmc-mortar-target-start-others", ("user", user), ("mortar", mortar));
        _popup.PopupPredicted(selfMsg, othersMsg, mortar.Owner, user);
    }

    private void OnMortarDialBui(Entity<MortarComponent> mortar, ref MortarDialBuiMsg args)
    {
        var target = new Vector2i(
            Math.Clamp(args.Target.X, -mortar.Comp.MaxDial, mortar.Comp.MaxDial),
            Math.Clamp(args.Target.Y, -mortar.Comp.MaxDial, mortar.Comp.MaxDial));

        var user = args.Actor;
        var ev = new DialMortarDoAfterEvent(target);
        var doAfter = new DoAfterArgs(EntityManager, user, mortar.Comp.DialDelay, ev, mortar)
        {
            BreakOnMove = true,
        };

        if (!_doAfter.TryStartDoAfter(doAfter))
            return;

        var selfMsg = Loc.GetString("rmc-mortar-dial-start-self", ("mortar", mortar));
        var othersMsg = Loc.GetString("rmc-mortar-dial-start-others", ("user", user), ("mortar", mortar));
        _popup.PopupPredicted(selfMsg, othersMsg, mortar.Owner, user);
    }

    /// <summary>
    /// Shows one message to the user and another to everyone nearby. For handlers that only run on the server.
    /// </summary>
    private void PopupSelfAndOthers(string selfMsg, string othersMsg, EntityUid user, EntityUid subject)
    {
        _popup.PopupEntity(selfMsg, subject, user);
        _popup.PopupEntity(othersMsg, subject, Filter.PvsExcept(user, entityManager: EntityManager), false, PopupType.Medium);
    }

    /// <summary>
    ///     Refuses an aim that would come down under a roof, telling the user why. Returns true if the caller
    ///     should abandon the aim.
    /// </summary>
    private bool RefuseRoofedTarget(Entity<MortarComponent> mortar, Vector2i target, EntityUid user)
    {
        var coordinates = new MapCoordinates(new Vector2(target.X, target.Y), _transform.GetMapCoordinates(mortar).MapId);
        if (!IsRoofed(coordinates))
            return false;

        _popup.PopupClient(Loc.GetString("rmc-mortar-target-roofed"), mortar.Owner, user);
        return true;
    }

    /// <summary>
    /// Works out where the next shell would land and whether the shot is legal.
    /// </summary>
    private bool TryGetShot(
        Entity<MortarComponent> mortar,
        MortarShellComponent shell,
        EntityUid? user,
        out MapCoordinates impact,
        out TimeSpan travelTime)
    {
        impact = default;
        travelTime = shell.TravelDelay;

        if (!mortar.Comp.Deployed)
        {
            PopupError(user, "rmc-mortar-not-deployed", mortar);
            return false;
        }

        var time = _timing.CurTime;
        if (time < mortar.Comp.LastFiredAt + mortar.Comp.FireDelay)
        {
            PopupError(user, "rmc-mortar-fire-cooldown", mortar);
            return false;
        }

        if (!mortar.Comp.HasTarget)
        {
            PopupError(user, "rmc-mortar-not-aimed", mortar);
            return false;
        }

        // Target is an absolute map coordinate, not an offset from the mortar.
        var target = mortar.Comp.Target + mortar.Comp.Offset + mortar.Comp.Dial;

        if (mortar.Comp.FireRandomOffset is { Length: > 0 } spread)
        {
            target += new Vector2i(_random.Pick(spread), _random.Pick(spread));
        }

        var mortarCoordinates = _transform.GetMapCoordinates(mortar);
        impact = new MapCoordinates(new Vector2(target.X, target.Y), mortarCoordinates.MapId);

        var distance = (mortarCoordinates.Position - impact.Position).Length();
        if (distance < mortar.Comp.MinimumRange)
        {
            PopupError(user, "rmc-mortar-target-too-close", null);
            return false;
        }

        if (distance > mortar.Comp.MaximumRange)
        {
            PopupError(user, "rmc-mortar-target-too-far", null);
            return false;
        }

        if (!IsValidTargetTile(impact, out var targetError))
        {
            PopupError(user, targetError, null);
            return false;
        }

        if (_container.TryGetContainer(mortar.Owner, mortar.Comp.ContainerId, out var container) &&
            !_container.CanInsert(shell.Owner, container))
        {
            PopupError(user, "rmc-mortar-cant-insert", mortar);
            return false;
        }

        return true;
    }

    private void PopupError(EntityUid? user, LocId message, Entity<MortarComponent>? mortar)
    {
        if (user is not { } recipient)
            return;

        var text = mortar is { } m
            ? Loc.GetString(message, ("mortar", m))
            : Loc.GetString(message);

        _popup.PopupEntity(text, recipient, recipient, PopupType.SmallCaution);
    }

    /// <summary>
    ///     Checks a coordinate is somewhere a shell can actually come down, and reports why not if it isn't.
    /// </summary>
    private bool IsValidTargetTile(MapCoordinates coordinates, out LocId error)
    {
        error = "rmc-mortar-target-not-area";

        if (!_map.TryFindGridAt(coordinates, out var gridUid, out var grid))
            return false;

        var tile = _map.WorldToTile(gridUid, grid, coordinates.Position);
        if (!_map.TryGetTileRef(gridUid, grid, tile, out var tileRef))
            return false;

        if (tileRef.Tile.IsEmpty)
            return false;

        if (_tile[tileRef.Tile.TypeId] is ContentTileDefinition def &&
            def.ID == ContentTileDefinition.SpaceID)
        {
            return false;
        }

        // A roof stops the round: whatever is under it never gets hit, so it can't be a target either.
        if (IsRoofed(gridUid, grid, tile))
        {
            error = "rmc-mortar-target-roofed";
            return false;
        }

        return true;
    }

    /// <summary>
    ///     Whether this tile of this grid is roofed over.
    /// </summary>
    private bool IsRoofed(EntityUid gridUid, MapGridComponent grid, Vector2i tile)
    {
        if (!TryComp<RoofComponent>(gridUid, out var roof))
            return false;

        return _roof.IsRooved((gridUid, grid, roof), tile);
    }

    /// <summary>
    ///     Whether a map coordinate landed under a roof.
    /// </summary>
    protected bool IsRoofed(MapCoordinates coordinates)
    {
        if (!_map.TryFindGridAt(coordinates, out var gridUid, out var grid))
            return false;

        return IsRoofed(gridUid, grid, _map.WorldToTile(gridUid, grid, coordinates.Position));
    }

    public void PopupWarning(MapCoordinates coordinates, float range, LocId warning, LocId warningAbove)
    {
        foreach (var session in _player.NetworkedSessions)
        {
            if (session.AttachedEntity is not { } recipient ||
                !_transformQuery.TryComp(recipient, out var xform) ||
                xform.MapID != coordinates.MapId)
            {
                continue;
            }

            var sessionCoordinates = _transform.GetMapCoordinates(xform);
            var distance = (coordinates.Position - sessionCoordinates.Position).Length();
            if (distance > range)
                continue;

            var msg = distance < 1
                ? Loc.GetString(warningAbove)
                : Loc.GetString(warning);
            _popup.PopupEntity(msg, recipient, recipient, PopupType.LargeCaution);
        }
    }

    public override void Update(float frameTime)
    {
        if (_net.IsClient)
            return;

        var time = _timing.CurTime;
        var shells = EntityQueryEnumerator<ActiveMortarShellComponent>();
        while (shells.MoveNext(out var uid, out var active))
        {
            if (!active.Warned && time >= active.WarnAt)
            {
                active.Warned = true;
                var coordinates = _transform.ToMapCoordinates(active.Coordinates);
                PopupWarning(coordinates,
                    active.WarnRange,
                    "rmc-mortar-shell-warning",
                    "rmc-mortar-shell-warning-above");
                _audio.PlayPvs(active.WarnSound, active.Coordinates);
            }

            if (!active.ImpactWarned && time >= active.ImpactWarnAt)
            {
                active.ImpactWarned = true;
                var coordinates = _transform.ToMapCoordinates(active.Coordinates);
                PopupWarning(coordinates,
                    active.ImpactWarnRange,
                    "rmc-mortar-shell-impact-warning",
                    "rmc-mortar-shell-impact-warning-above");
            }

            if (time >= active.LandAt)
                ImpactMortarShell(uid, active);
        }
    }

    private void ImpactMortarShell(EntityUid shell, ActiveMortarShellComponent active)
    {
        if (_container.TryGetContainingContainer((shell, (TransformComponent?) null, null), out var container))
            _container.Remove(shell, container, force: true);

        // Drift can put a round over a roof even when the aim was fine. It breaks up there instead of
        // coming through, so nothing underneath is touched.
        if (IsRoofed(_transform.ToMapCoordinates(active.Coordinates)))
        {
            QueueDel(shell);
            return;
        }

        _transform.SetCoordinates(shell, active.Coordinates);

        var ev = new MortarShellLandEvent(active.Coordinates);
        RaiseLocalEvent(shell, ref ev);

        ExplodeShell(shell);

        if (!EntityManager.IsQueuedForDeletion(shell))
            QueueDel(shell);
    }

    /// <summary>
    /// Spawns the shell's payload. Implemented server side where the explosion system lives.
    /// </summary>
    protected virtual void ExplodeShell(EntityUid shell)
    {
    }
}
