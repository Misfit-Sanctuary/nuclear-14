using System.Numerics;
using Content.Shared.CombatMode;
using Content.Shared.Chemistry.Components;
using Content.Shared.Construction.Components;
using Content.Shared.Containers.ItemSlots;
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
using Content.Shared.Payload.Components;
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
    [Dependency] private SharedEyeSystem _eye = default!;
    [Dependency] private FixtureSystem _fixture = default!;
    [Dependency] private ItemSlotsSystem _itemSlots = default!;
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

    /// Seconds a camera feed stays up before the view returns on its own.

    private static readonly TimeSpan CameraViewTime = TimeSpan.FromSeconds(5);

    /// Body drift, in tiles, that counts as walking away from the feed.

    private const float CameraViewMoveTolerance = 0.1f;

    private readonly Dictionary<EntityUid, CameraView> _cameraViews = new();

    private EntityQuery<TransformComponent> _transformQuery;

    public override void Initialize()
    {
        _transformQuery = GetEntityQuery<TransformComponent>();

        SubscribeLocalEvent<MortarComponent, UseInHandEvent>(OnMortarUseInHand, before: [typeof(ActivatableUISystem)]);
        SubscribeLocalEvent<MortarComponent, DeployMortarDoAfterEvent>(OnMortarDeployDoAfter);
        SubscribeLocalEvent<MortarComponent, SetMortarDoAfterEvent>(OnMortarSetDoAfter);
        SubscribeLocalEvent<MortarComponent, InteractUsingEvent>(OnMortarInteractUsing);
        SubscribeLocalEvent<MortarComponent, LoadMortarShellDoAfterEvent>(OnMortarLoadDoAfter);
        SubscribeLocalEvent<MortarComponent, AnchorStateChangedEvent>(OnMortarAnchorStateChanged);
        SubscribeLocalEvent<MortarComponent, ExaminedEvent>(OnMortarExamined);
        SubscribeLocalEvent<MortarComponent, ActivatableUIOpenAttemptEvent>(OnMortarActivatableUIOpenAttempt);
        SubscribeLocalEvent<MortarComponent, CombatModeShouldHandInteractEvent>(OnMortarShouldInteract);
        SubscribeLocalEvent<MortarComponent, DestructionEventArgs>(OnMortarDestruction);
        SubscribeLocalEvent<MortarComponent, BeforeDamageChangedEvent>(OnMortarBeforeDamageChanged);
        SubscribeLocalEvent<MortarImpactCameraComponent, ComponentShutdown>(OnImpactCameraShutdown);
        SubscribeLocalEvent<MortarComponent, BoundUIClosedEvent>(OnMortarUiClosed);
        SubscribeLocalEvent<MortarChemicalShellComponent, ComponentInit>(OnChemicalShellInit);
        SubscribeLocalEvent<MortarChemicalShellComponent, ComponentRemove>(OnChemicalShellRemove);
        SubscribeLocalEvent<MortarChemicalShellComponent, EntInsertedIntoContainerMessage>(OnChemicalShellBeakerInserted);
        SubscribeLocalEvent<MortarChemicalShellComponent, EntRemovedFromContainerMessage>(OnChemicalShellBeakerRemoved);

        Subs.BuiEvents<MortarComponent>(MortarUiKey.Key,
            subs =>
            {
                subs.Event<MortarSetBuiMsg>(OnMortarSetBui);
                subs.Event<MortarCameraBuiMsg>(OnMortarCameraBui);
            });
    }

    /// Beakers load through the slots a chemical payload grenade uses.

    private void OnChemicalShellInit(Entity<MortarChemicalShellComponent> shell, ref ComponentInit args)
    {
        _itemSlots.AddItemSlot(shell.Owner, MortarChemicalShellComponent.BeakerSlotAId, shell.Comp.BeakerSlotA);
        _itemSlots.AddItemSlot(shell.Owner, MortarChemicalShellComponent.BeakerSlotBId, shell.Comp.BeakerSlotB);
    }

    private void OnChemicalShellRemove(Entity<MortarChemicalShellComponent> shell, ref ComponentRemove args)
    {
        _itemSlots.RemoveItemSlot(shell.Owner, shell.Comp.BeakerSlotA);
        _itemSlots.RemoveItemSlot(shell.Owner, shell.Comp.BeakerSlotB);
    }

    private void OnChemicalShellBeakerInserted(Entity<MortarChemicalShellComponent> shell, ref EntInsertedIntoContainerMessage args)
    {
        UpdateChemicalShellAppearance(shell);
    }

    private void OnChemicalShellBeakerRemoved(Entity<MortarChemicalShellComponent> shell, ref EntRemovedFromContainerMessage args)
    {
        UpdateChemicalShellAppearance(shell);
    }

    /// Shows how full the shell is, so a round with both beakers in it reads as armed.

    private void UpdateChemicalShellAppearance(Entity<MortarChemicalShellComponent> shell)
    {
        var filled = ChemicalPayloadFilledSlots.None;

        if (shell.Comp.BeakerSlotA.HasItem)
            filled |= ChemicalPayloadFilledSlots.Left;

        if (shell.Comp.BeakerSlotB.HasItem)
            filled |= ChemicalPayloadFilledSlots.Right;

        _appearance.SetData(shell.Owner, ChemicalPayloadVisuals.Slots, filled);
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

        // Predicted on the client, so the popup has to be the predicted variant.
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

    private void OnMortarSetDoAfter(Entity<MortarComponent> mortar, ref SetMortarDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || _net.IsClient)
            return;

        args.Handled = true;

        var user = args.User;

        // Checked again here, in case the ground changed during the do-after.
        if (!CanFireAt(mortar, args.Target + args.Dial, user))
            return;

        mortar.Comp.Target = args.Target;
        mortar.Comp.Dial = args.Dial;
        mortar.Comp.HasTarget = true;
        Dirty(mortar);

        var selfMsg = Loc.GetString("rmc-mortar-target-finish-self", ("mortar", mortar));
        var othersMsg = Loc.GetString("rmc-mortar-target-finish-others", ("user", user), ("mortar", mortar));
        PopupSelfAndOthers(selfMsg, othersMsg, user, mortar.Owner);
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
            PopupRefusal(user, "rmc-mortar-not-deployed", mortar);
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

        // Folding wipes everything the mortar was holding on to.
        mortar.Comp.HasTarget = false;
        mortar.Comp.Target = Vector2i.Zero;
        mortar.Comp.Dial = Vector2i.Zero;
        mortar.Comp.ImpactCamera = null;
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
                    ("x", ent.Comp.Target.X + ent.Comp.Dial.X),
                    ("y", ent.Comp.Target.Y + ent.Comp.Dial.Y)));

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

    private void OnMortarSetBui(Entity<MortarComponent> mortar, ref MortarSetBuiMsg args)
    {
        var target = new Vector2i(
            Math.Clamp(args.Target.X, -mortar.Comp.MaxTarget, mortar.Comp.MaxTarget),
            Math.Clamp(args.Target.Y, -mortar.Comp.MaxTarget, mortar.Comp.MaxTarget));

        var dial = new Vector2i(
            Math.Clamp(args.Dial.X, -mortar.Comp.MaxDial, mortar.Comp.MaxDial),
            Math.Clamp(args.Dial.Y, -mortar.Comp.MaxDial, mortar.Comp.MaxDial));

        var user = args.Actor;

        // Refuse a bad aim up front, not once the do-after has run or a shell is loaded.
        if (!CanFireAt(mortar, target + dial, user))
            return;

        var ev = new SetMortarDoAfterEvent(target, dial);
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

    /// Shows one message to the user and another to everyone nearby.

    private void PopupSelfAndOthers(string selfMsg, string othersMsg, EntityUid user, EntityUid subject)
    {
        _popup.PopupEntity(selfMsg, subject, user);
        _popup.PopupEntity(othersMsg, subject, Filter.PvsExcept(user, entityManager: EntityManager), false, PopupType.Medium);
    }

    /// Refuses an aim the mortar can't fire at, popping up the reason. True means take it.

    private bool CanFireAt(Entity<MortarComponent> mortar, Vector2i aim, EntityUid? user)
    {
        if (AimError(mortar, aim) is not { } error)
            return true;

        PopupRefusal(user, error, null);
        return false;
    }

    /// Why the mortar can't fire at a coordinate, or null if it can.

    private LocId? AimError(Entity<MortarComponent> mortar, Vector2i aim)
    {
        var mortarCoordinates = _transform.GetMapCoordinates(mortar);
        var aimPosition = new Vector2(aim.X, aim.Y);
        var coordinates = new MapCoordinates(aimPosition, mortarCoordinates.MapId);

        if (!_map.TryFindGridAt(coordinates, out var gridUid, out var grid))
            return "rmc-mortar-target-not-area";

        var tile = _map.WorldToTile(gridUid, grid, coordinates.Position);
        if (!_map.TryGetTileRef(gridUid, grid, tile, out var tileRef) || tileRef.Tile.IsEmpty)
            return "rmc-mortar-target-not-area";

        if (_tile[tileRef.Tile.TypeId] is ContentTileDefinition def && def.ID == ContentTileDefinition.SpaceID)
            return "rmc-mortar-target-not-area";

        // A roof stops the round, so nothing under it can be a target.
        if (TryComp<RoofComponent>(gridUid, out var roof) && _roof.IsRooved((gridUid, grid, roof), tile))
            return "rmc-mortar-target-roofed";

        var distance = (mortarCoordinates.Position - aimPosition).Length();

        if (distance < mortar.Comp.MinimumRange)
            return "rmc-mortar-target-too-close";

        if (distance > mortar.Comp.MaximumRange)
            return "rmc-mortar-target-too-far";

        return null;
    }

    /// Works out where the next shell would land and whether the shot is legal.

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
            PopupRefusal(user, "rmc-mortar-not-deployed", mortar);
            return false;
        }

        if (!mortar.Comp.HasTarget)
        {
            PopupRefusal(user, "rmc-mortar-not-aimed", mortar);
            return false;
        }

        var mortarCoordinates = _transform.GetMapCoordinates(mortar);

        // Target is an absolute map coordinate, not an offset from the mortar.
        var aim = mortar.Comp.Target + mortar.Comp.Dial;

        // Checked again here, since the ground can change after the mortar was set.
        if (!CanFireAt(mortar, aim, user))
            return false;

        var aimPosition = new Vector2(aim.X, aim.Y);

        // Less accurate the further out: each axis rolls its own deflection.
        var profile = mortar.Comp.Scatter;
        var target = aim + new Vector2i(
            Scatter(profile, profile.BucketFor(aimPosition.X - mortarCoordinates.Position.X)),
            Scatter(profile, profile.BucketFor(aimPosition.Y - mortarCoordinates.Position.Y)));

        impact = new MapCoordinates(new Vector2(target.X, target.Y), mortarCoordinates.MapId);

        if (_container.TryGetContainer(mortar.Owner, mortar.Comp.ContainerId, out var container) &&
            !_container.CanInsert(shell.Owner, container))
        {
            PopupRefusal(user, "rmc-mortar-cant-insert", mortar);
            return false;
        }

        return true;
    }

    /// Refuses the user's action; the client predicts these checks but says nothing.

    private void PopupRefusal(EntityUid? user, LocId message, Entity<MortarComponent>? mortar)
    {
        if (user is not { } recipient)
            return;

        if (_net.IsClient)
            return;

        var text = mortar is { } m
            ? Loc.GetString(message, ("mortar", m))
            : Loc.GetString(message);

        _popup.PopupEntity(text, recipient, recipient, PopupType.SmallCaution);
    }

    /// A miss for one axis: a magnitude by weight, then a coin for which side of the aim.

    private int Scatter(MortarScatterProfile profile, int bucket)
    {
        var magnitude = profile.MagnitudeFor(bucket, _random.NextFloat());
        return magnitude * (_random.Next(2) == 0 ? -1 : 1);
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

    /// Moves the user's view to their last impact site, or back to their body.

    private void OnMortarCameraBui(Entity<MortarComponent> mortar, ref MortarCameraBuiMsg args)
    {
        var user = args.Actor;
        if (!TryComp(user, out EyeComponent? eye))
            return;

        // Pressing an unwatched feed switches to it; pressing the watched one returns.
        if (mortar.Comp.ImpactCamera is { } camera &&
            HasComp<MortarImpactCameraComponent>(camera) &&
            eye.Target != camera)
        {
            _eye.SetTarget(user, camera, eye);

            // Step away and the feed drops, so nobody walks around blind.
            var xform = Transform(user);
            _cameraViews[user] = new CameraView(camera, _timing.CurTime + CameraViewTime, xform.ParentUid, xform.LocalPosition);

            _popup.PopupEntity(Loc.GetString("rmc-mortar-camera-start"), user, user);
            return;
        }

        if (eye.Target is { } watching && HasComp<MortarImpactCameraComponent>(watching))
        {
            StopCameraView(user);
            _popup.PopupEntity(Loc.GetString("rmc-mortar-camera-stop"), user, user);
        }
    }

    private void StopCameraView(EntityUid user)
    {
        _cameraViews.Remove(user);

        if (TryComp(user, out EyeComponent? eye) && eye.Target is { } target && HasComp<MortarImpactCameraComponent>(target))
            _eye.SetTarget(user, null, eye);
    }

    /// Returns the view of anyone whose feed ended, lost its camera or walked off.

    private void UpdateCameraViews(TimeSpan time)
    {
        List<(EntityUid User, bool Expired, bool Moved)>? finished = null;
        foreach (var (user, view) in _cameraViews)
        {
            var expired = time >= view.EndsAt;
            var lost = !TryComp(user, out EyeComponent? eye) || eye.Target != view.Camera;
            var moved = !_transformQuery.TryComp(user, out var xform) ||
                        xform.ParentUid != view.Parent ||
                        (xform.LocalPosition - view.BodyPosition).LengthSquared() >
                        CameraViewMoveTolerance * CameraViewMoveTolerance;

            if (expired || lost || moved)
                (finished ??= new()).Add((user, expired, moved));
        }

        if (finished == null)
            return;

        foreach (var (user, expired, moved) in finished)
        {
            StopCameraView(user);

            if (expired)
                _popup.PopupEntity(Loc.GetString("rmc-mortar-camera-end"), user, user);
            else if (moved)
                _popup.PopupEntity(Loc.GetString("rmc-mortar-camera-moved"), user, user);
        }
    }

    /// Closing the window ends the feed, since the window is the only way back.

    private void OnMortarUiClosed(Entity<MortarComponent> mortar, ref BoundUIClosedEvent args)
    {
        if (_net.IsClient || !_cameraViews.ContainsKey(args.Actor))
            return;

        StopCameraView(args.Actor);
    }

    private void OnImpactCameraShutdown(Entity<MortarImpactCameraComponent> camera, ref ComponentShutdown args)
    {
        // A camera that expires or is destroyed must not leave someone's view pointed at nothing.
        var eyes = EntityQueryEnumerator<EyeComponent>();
        while (eyes.MoveNext(out var uid, out var eye))
        {
            if (eye.Target == camera.Owner)
                _eye.SetTarget(uid, null, eye);
        }

        var mortars = EntityQueryEnumerator<MortarComponent>();
        while (mortars.MoveNext(out var uid, out var mortar))
        {
            if (mortar.ImpactCamera != camera.Owner)
                continue;

            mortar.ImpactCamera = null;
            Dirty(uid, mortar);
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

        if (_cameraViews.Count > 0)
            UpdateCameraViews(time);
    }

    private void ImpactMortarShell(EntityUid shell, ActiveMortarShellComponent active)
    {
        // Unreachable today, since the query skips deleted shells, but a payload could kill one mid-loop.
        if (TerminatingOrDeleted(shell))
            return;

        // The tube it was fired from is the only thing a payload is allowed to report back to.
        EntityUid? mortar = null;
        if (_container.TryGetContainingContainer((shell, (TransformComponent?) null, null), out var container))
        {
            if (HasComp<MortarComponent>(container.Owner))
                mortar = container.Owner;

            _container.Remove(shell, container, force: true);
        }

        // No grid or map left to land on, so the round dies with the ground it was aimed at.
        if (TerminatingOrDeleted(active.Coordinates.EntityId))
        {
            QueueDel(shell);
            return;
        }

        _transform.SetCoordinates(shell, active.Coordinates);

        TryComp(shell, out MortarShellComponent? landing);
        if (landing?.LandSound is { } landSound)
            _audio.PlayPvs(landSound, shell);

        var ev = new MortarShellLandEvent(active.Coordinates, mortar);
        RaiseLocalEvent(shell, ref ev);

        ExplodeShell(shell);

        // A reusable casing is left where it struck, ready to be collected and loaded again.
        if (landing is { Reusable: true })
        {
            RemComp<ActiveMortarShellComponent>(shell);
            return;
        }

        if (!EntityManager.IsQueuedForDeletion(shell))
            QueueDel(shell);
    }

    /// Spawns the shell's payload. Implemented server side where the explosion system lives.

    protected virtual void ExplodeShell(EntityUid shell)
    {
    }

    /// One player watching one camera, and the spot their body was standing when the feed started.

    private readonly record struct CameraView(
        EntityUid Camera,
        TimeSpan EndsAt,
        EntityUid Parent,
        Vector2 BodyPosition);
}
