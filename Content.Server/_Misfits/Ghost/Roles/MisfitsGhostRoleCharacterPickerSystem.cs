// #Misfits Add - Ghost roles where the ghost picks which body they take.
using System.Linq;
using Content.Server.Administration.Managers;
using Content.Server.Chat.Managers;
using Content.Server.EUI;
using Content.Server.Ghost.Roles;
using Content.Server.Ghost.Roles.Components;
using Content.Server.Ghost.Roles.Events;
using Content.Server.Mind;
using Content.Server.Paper;
using Content.Server.Preferences.Managers;
using Content.Server.Station.Systems;
using Content.Shared._Misfits.Ghost.Roles;
using Content.Shared.Ghost;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Inventory;
using Content.Shared.Mind.Components;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Misfits.Ghost.Roles;

public sealed class MisfitsGhostRoleCharacterPickerSystem : EntitySystem
{
    [Dependency] private readonly IBanManager _bans = default!;
    [Dependency] private readonly IChatManager _chat = default!;
    [Dependency] private readonly EuiManager _eui = default!;
    [Dependency] private readonly GhostRoleSystem _ghostRoles = default!;
    [Dependency] private readonly StationSpawningSystem _spawning = default!;
    [Dependency] private readonly IServerPreferencesManager _preferences = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly MindSystem _minds = default!;
    [Dependency] private readonly MetaDataSystem _metaData = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly PaperSystem _paper = default!;
    [Dependency] private readonly SharedStorageSystem _storage = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;

    // Picker reserving each spawner, so it can be closed on timeout or when the player leaves ghost.
    private readonly Dictionary<EntityUid, MisfitsGhostRoleCharacterPickerEui> _openPickers = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MisfitsGhostRoleCharacterPickerComponent, TakeGhostRoleEvent>(OnTakeRole);
        SubscribeLocalEvent<PlayerAttachedEvent>(OnPlayerAttached);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_openPickers.Count == 0)
            return;

        foreach (var spawner in _openPickers.Keys.ToList())
        {
            TryExpirePicker(spawner);
        }
    }

    private void OnTakeRole(EntityUid uid, MisfitsGhostRoleCharacterPickerComponent component,
        ref TakeGhostRoleEvent args)
    {
        args.TookRole = false;

        if (args.Cancelled)
            return;

        if (!TryComp<GhostRoleComponent>(uid, out var role) || MetaData(uid).EntityPaused)
            return;

        // #Misfits Add - a player in the lobby has no body yet; an attached one has to be a ghost.
        if (args.Player.AttachedEntity is { Valid: true } mob && !HasComp<GhostComponent>(mob))
            return;

        // Null while a player's bans are still loading; the spawner's prototype id is the ban key.
        if (_bans.GetRoleBans(args.Player.UserId) is { } bans &&
            MetaData(uid).EntityPrototype?.ID is { } roleId && bans.Contains(roleId))
        {
            args.Cancelled = true;
            Log.Info($"{args.Player.Name} is rolebanned from {ToPrettyString(uid)}.");
            _chat.DispatchServerMessage(args.Player, Loc.GetString("misfits-ghost-role-banned"));
            return;
        }

        // Clicking again gets this player a fresh picker; anyone else waits for it to expire.
        if (_openPickers.TryGetValue(uid, out var existing))
        {
            if (existing.Player != args.Player && !TryExpirePicker(uid))
                return;

            existing.Close();
        }

        var picker = new MisfitsGhostRoleCharacterPickerEui(this, uid, role.RoleName);
        _openPickers[uid] = picker;
        component.BusyUntil = _timing.CurTime + component.PickerTimeout;

        try
        {
            _eui.OpenEui(picker, args.Player);
        }
        catch (Exception e)
        {
            // Could not hand the client a picker, so release the role instead of reserving it.
            Log.Error($"Could not open the ghost role character picker for {args.Player.Name}: {e}");
            _openPickers.Remove(uid);
            component.BusyUntil = null;
            return;
        }

        args.TookRole = true;
    }

    // Closes the picker of a player who is no longer a ghost, so they cannot spawn a body.
    private void OnPlayerAttached(PlayerAttachedEvent args)
    {
        if (_openPickers.Count == 0 || HasComp<GhostComponent>(args.Entity))
            return;

        foreach (var (_, picker) in _openPickers.ToList())
        {
            if (picker.Player == args.Player)
                picker.Close();
        }
    }

    // The random entry if allowed, plus every saved character that passes the species gate.
    public MisfitsGhostRoleCharacterOption[] GetOptions(ICommonSession player, EntityUid uid)
    {
        if (!TryComp<MisfitsGhostRoleCharacterPickerComponent>(uid, out var component))
            return Array.Empty<MisfitsGhostRoleCharacterOption>();

        var options = new List<MisfitsGhostRoleCharacterOption>();

        if (component.AllowRandom)
        {
            options.Add(new MisfitsGhostRoleCharacterOption
            {
                Index = -1,
                Name = Loc.GetString("misfits-ghost-role-picker-random"),
            });
        }

        foreach (var (index, profile) in GetCharacters(player, component))
        {
            options.Add(new MisfitsGhostRoleCharacterOption
            {
                Index = index,
                Name = profile.Name,
            });
        }

        return options.ToArray();
    }

    // Spawns the chosen character and hands over the role; false when the choice is invalid.
    public bool TrySpawn(ICommonSession player, EntityUid uid, int index)
    {
        if (!TryComp<MisfitsGhostRoleCharacterPickerComponent>(uid, out var component))
            return false;

        if (!TryComp<GhostRoleComponent>(uid, out var role))
            return false;

        // #Misfits Add - a player in the lobby has no body yet; an attached one has to be a ghost.
        if (player.AttachedEntity is { Valid: true } ghost && !HasComp<GhostComponent>(ghost))
            return false;

        // #Misfits Add - no body at this point means the role was picked from the lobby.
        var fromLobby = player.AttachedEntity is not { Valid: true };

        if (ResolveProfile(player, component, index) is not { } profile)
        {
            Log.Warning($"{player.Name} tried to take a ghost role with an ineligible character (index {index}).");
            return false;
        }

        var mob = _spawning.SpawnPlayerMob(Transform(uid).Coordinates, null, profile, null);
        _transform.AttachToGridOrMap(mob);

        // Dress the body from YAML before the ghost takes it.
        if (component.Gear is { } gear)
            _spawning.EquipStartingGear(mob, gear);

        // #Misfits Add - event roles arrive in their own faction, with whatever abilities the role gives them.
        EntityManager.AddComponents(mob, component.AddComponents);

        // A team role arrives with its net, so a body taken later still finds the rest of the crew.
        if (component.Frequency > 0)
            GiveFrequencyNote(mob, component.Frequency);

        EnsureComp<MindContainerComponent>(mob);

        var spawnedEvent = new GhostRoleSpawnerUsedEvent(uid, mob);
        RaiseLocalEvent(mob, spawnedEvent);

        _ghostRoles.GhostRoleInternalCreateMindAndTransfer(player, uid, mob, role);

        // #Misfits Add - picked from the lobby, the player now has a body and belongs in the round.
        if (fromLobby)
            _ghostRoles.JoinRoundFromLobby(player);

        // #Misfits Add - the running event's briefing, e.g. the crime this role is wanted for, goes to the taker.
        if (component.Briefing is { } briefing)
            _chat.DispatchServerMessage(player, briefing);

        if (component.Objective is { } objective)
            GiveObjective(mob, objective, DescribeObjective(component, component.Briefing));

        // The spawner is a one-shot marker: removing it unregisters the role.
        QueueDel(uid);
        return true;
    }

    // The team's frequency on a note, into the backpack; left on the ground if the role has no bag.
    // The radio itself comes from the role's kit, so the note is what tells them where to tune it.
    private void GiveFrequencyNote(EntityUid mob, int frequency)
    {
        var note = Spawn("Paper", Transform(mob).Coordinates);
        _paper.SetContent(note, Loc.GetString("misfits-event-radio-note", ("frequency", frequency)));

        if (_inventory.TryGetSlotEntity(mob, "back", out var back))
            _storage.Insert(back.Value, note, out _, storageComp: CompOrNull<StorageComponent>(back.Value));
    }

    // Hands the role's freeform objective to the mind it just created.
    private void GiveObjective(EntityUid mob, string title, string? description)
    {
        if (!_minds.TryGetMind(mob, out var mindId, out var mind))
            return;

        var objective = Spawn("LoreMasterCustomObjective");
        _metaData.SetEntityName(objective, Loc.GetString(title));

        if (description is { } text)
            _metaData.SetEntityDescription(objective, text);

        _minds.AddObjective(mindId, mind, objective);
    }

    // The role's own objective text, with the run's briefing underneath it.
    private string? DescribeObjective(MisfitsGhostRoleCharacterPickerComponent component, string? briefing)
    {
        var description = component.ObjectiveDescription is { } id ? Loc.GetString(id) : null;

        if (briefing == null)
            return description;

        return description == null ? briefing : $"{description}\n\n{briefing}";
    }

    // Called by the picker EUI on close, whatever the reason.
    public void OnPickerClosed(EntityUid uid, MisfitsGhostRoleCharacterPickerEui picker, bool tookRole)
    {
        if (_openPickers.TryGetValue(uid, out var open) && open == picker)
            _openPickers.Remove(uid);

        if (tookRole)
            return;

        if (Exists(uid) && TryComp<MisfitsGhostRoleCharacterPickerComponent>(uid, out var component))
            component.BusyUntil = null;
    }

    // False while a picker still holds the role; closes it and frees the role on timeout.
    private bool TryExpirePicker(EntityUid uid)
    {
        if (!_openPickers.TryGetValue(uid, out var picker))
            return true;

        if (!TryComp<MisfitsGhostRoleCharacterPickerComponent>(uid, out var component))
        {
            picker.Close();
            return true;
        }

        if (component.BusyUntil is { } until && _timing.CurTime < until)
            return false;

        Log.Debug($"Ghost role character picker for {ToPrettyString(uid)} timed out, releasing the role.");
        picker.Close();
        return true;
    }

    // The player's saved characters this role accepts, with their slot; the only species gate.
    private List<(int Index, HumanoidCharacterProfile Profile)> GetCharacters(ICommonSession player,
        MisfitsGhostRoleCharacterPickerComponent component)
    {
        var characters = new List<(int, HumanoidCharacterProfile)>();

        if (_preferences.GetPreferencesOrNull(player.UserId) is not { } prefs)
            return characters;

        foreach (var (index, character) in prefs.Characters)
        {
            if (character is not HumanoidCharacterProfile profile)
                continue;

            if (!Allowed(component, profile.Species))
                continue;

            characters.Add((index, profile));
        }

        return characters;
    }

    private HumanoidCharacterProfile? ResolveProfile(ICommonSession player,
        MisfitsGhostRoleCharacterPickerComponent component, int index)
    {
        if (index < 0)
        {
            if (!component.AllowRandom)
                return null;

            // Never roll a random race: a role that lists species only gets those, and anything else is human.
            return HumanoidCharacterProfile.RandomWithSpecies(component.AllowedSpecies.Count > 0
                ? _random.Pick(component.AllowedSpecies).Id
                : "Human");
        }

        foreach (var (slot, profile) in GetCharacters(player, component))
        {
            if (slot == index)
                return profile;
        }

        return null;
    }

    private static bool Allowed(MisfitsGhostRoleCharacterPickerComponent component, string species)
    {
        return component.AllowedSpecies.Count == 0 ||
               component.AllowedSpecies.Any(allowed => allowed.Id == species);
    }
}

// Ghost role spawner where the ghost picks which body to take.
[RegisterComponent]
public sealed partial class MisfitsGhostRoleCharacterPickerComponent : Component
{
    // Species a character must be to be offered; empty means any.
    [DataField]
    public List<ProtoId<SpeciesPrototype>> AllowedSpecies = new();

    // Whether the "randomly generated character" option is offered.
    [DataField]
    public bool AllowRandom = true;

    // startingGear prototype the spawned body is dressed with; see the startingGear entries.
    [DataField]
    public ProtoId<StartingGearPrototype>? Gear;

    // Components put on the spawned body, e.g. the role's faction or an ability only it has.
    [DataField]
    public ComponentRegistry AddComponents = new();

    // Radio team this role belongs to; every role on a team shares one frequency per event run.
    [DataField]
    public string? RadioTeam;

    // Frequency the event rolled for this role's team; zero means the role gets no note.
    [ViewVariables]
    public int Frequency;

    // Loc string for an objective the taker gets, e.g. "bring the criminal to justice"; null for none.
    [DataField]
    public string? Objective;

    // Optional loc string for that objective's detail line.
    [DataField]
    public string? ObjectiveDescription;

    // Text the running event rolled for this role, e.g. the crime the law event is about; null for none.
    [ViewVariables]
    public string? Briefing;


    // How long a picker may hold the role before it is offered again.
    [DataField]
    public TimeSpan PickerTimeout = TimeSpan.FromMinutes(2);

    // When the open picker stops reserving this role; null when nobody is picking.
    [ViewVariables]
    public TimeSpan? BusyUntil;
}
