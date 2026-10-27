using Content.Shared.Coordinates;
using Content.Shared.Coordinates.Helpers;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared._Misfits.Rangefinder;

/// <summary>
///     Handheld laser rangefinder. Pointing it at a spot and holding the laser there logs that
///     spot's map coordinates, which is how a mortar crew gets something to dial into a mortar.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(LaserRangefinderSystem))]
public sealed partial class LaserRangefinderComponent : Component
{
    /// <summary>
    ///     Furthest spot the laser can be ranged, in tiles.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Range = 30f;

    /// <summary>
    ///     How long the laser has to be held on a spot before it reports a reading.
    /// </summary>
    [DataField, AutoNetworkedField]
    public TimeSpan TargetDelay = TimeSpan.FromSeconds(0.75);

    /// <summary>
    ///     Played when the laser gets its reading.
    /// </summary>
    [DataField, AutoNetworkedField]
    public SoundSpecifier? LaserSound = new SoundPathSpecifier("/Audio/Weapons/Guns/Gunshots/laser.ogg", AudioParams.Default.WithVolume(-4f));

    /// <summary>
    ///     Last reading taken, kept so it can be looked up again by examining the rangefinder.
    /// </summary>
    [DataField, AutoNetworkedField]
    public Vector2i? LastTarget;
}

[Serializable, NetSerializable]
public sealed partial class LaserRangefinderDoAfterEvent : SimpleDoAfterEvent
{
    public readonly NetCoordinates Coordinates;

    public LaserRangefinderDoAfterEvent(NetCoordinates coordinates)
    {
        Coordinates = coordinates;
    }
}

/// <summary>
///     Runs the rangefinder: aim at a spot, wait out the do-after, get the coordinates back.
/// </summary>
public sealed partial class LaserRangefinderSystem : EntitySystem
{
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<LaserRangefinderComponent, AfterInteractEvent>(OnRangefinderAfterInteract);
        SubscribeLocalEvent<LaserRangefinderComponent, LaserRangefinderDoAfterEvent>(OnRangefinderDoAfter);
        SubscribeLocalEvent<LaserRangefinderComponent, ExaminedEvent>(OnRangefinderExamined);
    }

    private void OnRangefinderAfterInteract(Entity<LaserRangefinderComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled)
            return;

        // The user is deliberately clicking somewhere they cannot reach, so CanReach is ignored.
        var coordinates = args.ClickLocation.SnapToGrid(EntityManager);
        if (!coordinates.IsValid(EntityManager))
            return;

        args.Handled = true;

        var user = args.User;
        var userCoordinates = _transform.GetMapCoordinates(user);
        var targetCoordinates = _transform.ToMapCoordinates(coordinates);
        if (targetCoordinates.MapId != userCoordinates.MapId)
            return;

        var distance = (targetCoordinates.Position - userCoordinates.Position).Length();
        if (distance > ent.Comp.Range)
        {
            _popup.PopupClient(Loc.GetString("laser-rangefinder-out-of-range", ("range", (int) ent.Comp.Range)),
                user,
                PopupType.SmallCaution);
            return;
        }

        var ev = new LaserRangefinderDoAfterEvent(GetNetCoordinates(coordinates));
        var doAfter = new DoAfterArgs(EntityManager, user, ent.Comp.TargetDelay, ev, ent.Owner)
        {
            BreakOnMove = true,
            NeedHand = true,
        };

        if (_doAfter.TryStartDoAfter(doAfter))
            _popup.PopupClient(Loc.GetString("laser-rangefinder-start"), user, PopupType.Medium);
    }

    private void OnRangefinderDoAfter(Entity<LaserRangefinderComponent> ent, ref LaserRangefinderDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;

        var coordinates = GetCoordinates(args.Coordinates);
        if (!coordinates.IsValid(EntityManager))
            return;

        var mapCoordinates = _transform.ToMapCoordinates(coordinates);
        var target = mapCoordinates.Position.Floored();

        // Measured from the rangefinder itself, so the readout matches the distance the mortar's
        // own range check will work out for the same coordinates.
        var distance = (mapCoordinates.Position - _transform.GetMapCoordinates(ent.Owner).Position).Length();

        // Shown locally by the client's predicted run; the server side of this call does nothing.
        _popup.PopupClient(Loc.GetString("laser-rangefinder-reading",
                ("x", target.X),
                ("y", target.Y),
                ("distance", (int) MathF.Round(distance))),
            args.User,
            PopupType.Large);

        if (_net.IsClient)
            return;

        ent.Comp.LastTarget = target;
        Dirty(ent);
        _audio.PlayPvs(ent.Comp.LaserSound, ent.Owner);
    }

    private void OnRangefinderExamined(Entity<LaserRangefinderComponent> ent, ref ExaminedEvent args)
    {
        using (args.PushGroup(nameof(LaserRangefinderComponent)))
        {
            args.PushMarkup(ent.Comp.LastTarget is { } target
                ? Loc.GetString("laser-rangefinder-examine", ("x", target.X), ("y", target.Y))
                : Loc.GetString("laser-rangefinder-no-reading"));
        }
    }
}
