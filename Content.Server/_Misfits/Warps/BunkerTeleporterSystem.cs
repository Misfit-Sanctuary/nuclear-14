// #Misfits Add - Bunker tunnel teleporter: surface hatches that lead down to the Enclave base (or,
// for outsiders, into the mines), and a sealed vault door down there that takes the Enclave back up.
using System.Linq;
using Content.Server.Popups;
using Content.Server.Warps;
using Content.Shared._Misfits.Warps;
using Content.Shared.Examine;
using Content.Shared.Ghost;
using Content.Shared.Interaction;
using Content.Shared.Mind;
using Content.Shared.Roles;
using Content.Shared.Roles.Jobs;
using Robust.Server.GameObjects;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._Misfits.Warps;

/// <summary>
/// Decides where the bunker hatches and the tunnel door send people.
/// <list type="bullet">
/// <item>Enclave member, down a hatch (locked or not): arrives at the tunnel door, and the hatch
/// locks behind them.</item>
/// <item>Outsider, down an open hatch: usually lands at a random mine marker
/// (<see cref="BunkerTunnelExitComponent"/>), sometimes gets lost in the tunnels for a while and
/// climbs back out of the same hatch (<see cref="BunkerTunnelLostSystem"/>). Outsiders never reach
/// the base on their own; only being dragged or carried by an Enclave member gets them in.
/// A locked hatch keeps them out until they pry it.</item>
/// <item>Enclave member at the tunnel door: a menu lists the hatches with a camera view, and they
/// pick where to come out. That hatch locks behind them.</item>
/// <item>Outsider at the tunnel door: it doesn't open.</item>
/// </list>
/// Nothing is stored on disk and no ids are matched up, so admin-spawned pieces work immediately.
/// </summary>
public sealed class BunkerTeleporterSystem : EntitySystem
{
    private static readonly ProtoId<DepartmentPrototype> EnclaveDepartment = "Enclave";

    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly PopupSystem _popupSystem = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly WarperSystem _warper = default!;
    [Dependency] private readonly SharedMindSystem _mind = default!;
    [Dependency] private readonly SharedJobSystem _jobs = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly BunkerHatchLockSystem _lock = default!;
    [Dependency] private readonly BunkerHatchViewSystem _view = default!;
    [Dependency] private readonly BunkerTunnelLostSystem _lost = default!;

    public override void Initialize()
    {
        base.Initialize();

        // Same three events WarperSystem handles, so this feels identical to a normal ladder.
        SubscribeLocalEvent<BunkerTeleporterComponent, InteractHandEvent>(OnInteractHand);
        SubscribeLocalEvent<BunkerTeleporterComponent, ActivateInWorldEvent>(OnActivateInWorld);
        SubscribeLocalEvent<BunkerTeleporterComponent, ExaminedEvent>(OnExamined);

        SubscribeLocalEvent<BunkerTeleporterComponent, BunkerTunnelDoorPreviewMessage>(OnDoorPreview);
        SubscribeLocalEvent<BunkerTeleporterComponent, BunkerTunnelDoorGoMessage>(OnDoorGo);

        SubscribeLocalEvent<BunkerHatchLockChangedEvent>(_ => RefreshDoorMenus());
        SubscribeLocalEvent<BunkerTeleporterComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnInteractHand(EntityUid uid, BunkerTeleporterComponent component, InteractHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = TryTraverse(uid, component, args.User);
    }

    private void OnActivateInWorld(EntityUid uid, BunkerTeleporterComponent component, ActivateInWorldEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = TryTraverse(uid, component, args.User);
    }

    private void OnExamined(EntityUid uid, BunkerTeleporterComponent component, ExaminedEvent args)
    {
        if (component.IsSurface && component.Label != null)
            args.PushMarkup(Loc.GetString("bunker-hatch-examine-label", ("label", component.Label)));

        // People on the surface can tell someone is wandering around lost down there.
        if (component.IsSurface && _lost.IsAnyoneLostIn(uid))
            args.PushMarkup(Loc.GetString("bunker-tunnel-lost-examine"));

        if (!args.IsInDetailsRange)
            return;

        // Regular ghosts cannot hand-interact, so close-range examine is how they travel. Admin
        // ghosts interact normally and are already covered by the events above.
        if (!TryComp(args.Examiner, out GhostComponent? ghost) || ghost.CanGhostInteract)
            return;

        TryTraverse(uid, component, args.Examiner);
    }

    private void OnShutdown(EntityUid uid, BunkerTeleporterComponent component, ComponentShutdown args)
    {
        // A hatch disappearing (round-start reroll, admin delete) changes what the door menu lists.
        if (component.IsSurface)
            RefreshDoorMenus(except: uid);
    }

    /// <summary>
    /// Handles someone using a hatch or the door. Returns true if the use was dealt with (moved them,
    /// opened the menu, or told them why not), so nothing else acts on the same click.
    /// </summary>
    private bool TryTraverse(EntityUid uid, BunkerTeleporterComponent component, EntityUid user)
    {
        // Someone lost in the tunnels can't use anything until they find their way out; that way
        // they can never skip the wait by clicking the hatch they are held in.
        if (HasComp<BunkerTunnelLostComponent>(user))
            return true;

        return component.IsSurface
            ? UseHatch(uid, component, user)
            : UseDoor(uid, component, user);
    }

    private bool UseHatch(EntityUid hatch, BunkerTeleporterComponent component, EntityUid user)
    {
        // Ghosts ignore locks and factions and just go down to the base.
        if (HasComp<GhostComponent>(user))
            return WarpOrComplain(user, hatch, FindDoor(component.Channel) ?? PickExit(component.Channel));

        if (IsEnclave(user))
        {
            if (!WarpOrComplain(user, hatch, FindDoor(component.Channel)))
                return true;

            // Enclave close the hatch behind them.
            _lock.SetLocked(hatch, true);
            return true;
        }

        if (_lock.IsLocked(hatch))
        {
            _popupSystem.PopupEntity(Loc.GetString("bunker-hatch-sealed"), hatch, user);
            return true;
        }

        // Outsiders: usually the mines. Sometimes (or always, if no mine markers are placed) they get
        // lost in the tunnels and wander back out of this same hatch. Never the Enclave base.
        // Someone who was just lost can't be lost again for a while: they go to the mines.
        var mineExit = PickExit(component.Channel);
        var canGetLost = mineExit == null || !_lost.IsOnCooldown(user);
        if (canGetLost && (mineExit == null || _random.Prob(Math.Clamp(component.OutsiderLostChance, 0f, 1f))))
        {
            if (_lost.TryLose(user, hatch, component.OutsiderLostTime, component.OutsiderLostCooldown))
                return true;
        }

        return WarpOrComplain(user, hatch, mineExit);
    }

    private bool UseDoor(EntityUid door, BunkerTeleporterComponent component, EntityUid user)
    {
        // Ghosts have no menu; send them up a random hatch.
        if (HasComp<GhostComponent>(user))
        {
            var hatches = GetHatches(component.Channel);
            return WarpOrComplain(user, door, hatches.Count > 0 ? _random.Pick(hatches) : null);
        }

        if (!IsEnclave(user))
        {
            _popupSystem.PopupEntity(Loc.GetString("bunker-door-no-budge"), door, user);
            return true;
        }

        if (!_ui.HasUi(door, BunkerTunnelDoorUiKey.Key))
            return WarpOrComplain(user, door, null);

        _ui.OpenUi(door, BunkerTunnelDoorUiKey.Key, user);
        _ui.SetUiState(door, BunkerTunnelDoorUiKey.Key, new BunkerTunnelDoorUiState(BuildHatchEntries(component.Channel)));
        return true;
    }

    private void OnDoorPreview(EntityUid uid, BunkerTeleporterComponent component, BunkerTunnelDoorPreviewMessage args)
    {
        if (args.Hatch is not { } netHatch)
        {
            _view.StopViewing(args.Actor);
            return;
        }

        var hatch = GetEntity(netHatch);
        if (!IsHatchOnChannel(hatch, component.Channel) || !IsEnclave(args.Actor))
            return;

        _view.StartViewing(args.Actor, hatch, uid, BunkerTunnelDoorUiKey.Key);
    }

    private void OnDoorGo(EntityUid uid, BunkerTeleporterComponent component, BunkerTunnelDoorGoMessage args)
    {
        var user = args.Actor;
        var hatch = GetEntity(args.Hatch);
        if (component.IsSurface || !IsHatchOnChannel(hatch, component.Channel) || !IsEnclave(user))
            return;

        _view.StopViewing(user);
        _ui.CloseUi(uid, BunkerTunnelDoorUiKey.Key, user);

        if (WarpOrComplain(user, uid, hatch))
            _lock.SetLocked(hatch, true);
    }

    private bool WarpOrComplain(EntityUid user, EntityUid from, EntityUid? destination)
    {
        if (destination is { } dest && _warper.WarpEntityTo(user, dest))
            return true;

        _popupSystem.PopupEntity(Loc.GetString("warper-goes-nowhere", ("warper", from)), user, Filter.Entities(user), true);
        return false;
    }

    private EntityUid? PickExit(string channel)
    {
        var exits = new List<EntityUid>();
        var query = EntityQueryEnumerator<BunkerTunnelExitComponent>();
        while (query.MoveNext(out var exit, out var exitComponent))
        {
            if (exitComponent.Channel == channel)
                exits.Add(exit);
        }

        return exits.Count > 0 ? _random.Pick(exits) : null;
    }

    /// <summary>
    /// The tunnel door on this channel. There is normally just the one, in the Enclave base.
    /// </summary>
    private EntityUid? FindDoor(string channel)
    {
        var query = EntityQueryEnumerator<BunkerTeleporterComponent>();
        while (query.MoveNext(out var uid, out var component))
        {
            if (!component.IsSurface && component.Channel == channel && !TerminatingOrDeleted(uid))
                return uid;
        }

        return null;
    }

    /// <summary>
    /// Every surface hatch on this channel. Only entities carrying
    /// <see cref="BunkerTeleporterComponent"/> count, so ordinary ladders are never picked up.
    /// </summary>
    public List<EntityUid> GetHatches(string? channel)
    {
        var hatches = new List<EntityUid>();
        var query = EntityQueryEnumerator<BunkerTeleporterComponent>();
        while (query.MoveNext(out var uid, out var component))
        {
            if (component.IsSurface
                && (channel == null || component.Channel == channel)
                && !TerminatingOrDeleted(uid))
            {
                hatches.Add(uid);
            }
        }

        return hatches;
    }

    /// <summary>
    /// The hatches as the door menu and tac map show them, sorted by label.
    /// </summary>
    public BunkerHatchEntry[] BuildHatchEntries(string? channel)
    {
        var entries = new List<BunkerHatchEntry>();
        foreach (var hatch in GetHatches(channel))
        {
            var component = Comp<BunkerTeleporterComponent>(hatch);
            var position = _transform.GetMapCoordinates(hatch).Position;
            var label = component.Label ?? Loc.GetString("bunker-hatch-label-unnamed");
            entries.Add(new BunkerHatchEntry(GetNetEntity(hatch), label, _lock.IsLocked(hatch), position.X, position.Y));
        }

        return entries.OrderBy(e => e.Label, StringComparer.Ordinal).ToArray();
    }

    private bool IsHatchOnChannel(EntityUid hatch, string channel)
    {
        return TryComp<BunkerTeleporterComponent>(hatch, out var component)
            && component.IsSurface
            && component.Channel == channel
            && !TerminatingOrDeleted(hatch);
    }

    /// <summary>
    /// True if the user's job is any job in the Enclave department.
    /// </summary>
    public bool IsEnclave(EntityUid user)
    {
        if (!_mind.TryGetMind(user, out var mindId, out _)
            || !_jobs.MindTryGetJob(mindId, out _, out var job)
            || !_prototype.TryIndex(EnclaveDepartment, out var department))
        {
            return false;
        }

        return department.Roles.Any(role => role.Id == job.ID);
    }

    /// <summary>
    /// Pushes a fresh hatch list to every open tunnel door menu.
    /// </summary>
    public void RefreshDoorMenus(EntityUid? except = null)
    {
        var query = EntityQueryEnumerator<BunkerTeleporterComponent, UserInterfaceComponent>();
        while (query.MoveNext(out var uid, out var component, out var ui))
        {
            if (component.IsSurface || !_ui.IsUiOpen((uid, ui), BunkerTunnelDoorUiKey.Key))
                continue;

            var entries = BuildHatchEntries(component.Channel);
            if (except is { } skip)
                entries = entries.Where(e => e.Hatch != GetNetEntity(skip)).ToArray();

            _ui.SetUiState((uid, ui), BunkerTunnelDoorUiKey.Key, new BunkerTunnelDoorUiState(entries));
        }
    }
}
