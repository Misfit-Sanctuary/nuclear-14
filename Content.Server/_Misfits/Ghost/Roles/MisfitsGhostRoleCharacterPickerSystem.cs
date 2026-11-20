// #Misfits Add - Ghost roles where the ghost picks which body they take.
using System.Linq;
using System.Text;
using Content.Server._Misfits.Events;
using Content.Server.Administration.Managers;
using Content.Server.Chat.Managers;
using Content.Server.Clothing.Systems;
using Content.Server.EUI;
using Content.Server.Ghost.Roles;
using Content.Server.Ghost.Roles.Components;
using Content.Server.Ghost.Roles.Events;
using Content.Server.Mind;
using Content.Server.Paper;
using Content.Server.Preferences.Managers;
using Content.Server.Station.Systems;
using Content.Shared._Misfits.Ghost.Roles;
using Content.Shared.Dataset;
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
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly LoadoutSystem _loadouts = default!;
    [Dependency] private readonly MisfitsEventSystem _events = default!;

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

        // Dress the body from YAML before the ghost takes it; a role that lists several kits rolls one per body.
        if (component.Gear.Count > 0)
            _spawning.EquipStartingGear(mob, _random.Pick(component.Gear));

        // #Misfits Add - a job role arrives in that job's own kit, with its job's specials (faction, abilities, traits),
        // then in whatever the player picked in the customize menu.
        if (component.Job is { } jobId)
        {
            if (_prototypes.TryIndex<JobPrototype>(jobId, out var job))
            {
                if (job.StartingGear is { } jobGear)
                    _spawning.EquipStartingGear(mob, jobGear);

                _spawning.ApplyJobSpecials(mob, job);
            }

            _loadouts.ApplyEventLoadout(mob, jobId, profile, player);
        }

        // #Misfits Add - event roles arrive in their own faction, with whatever abilities the role gives them.
        // Last, so a role's own components win over anything its job set.
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

        // #Misfits Add - the run files this body's name, so a later role's briefing can name it, e.g. the ranger's warrant.
        if (component.RoleKey is { } roleKey && component.Rule is { } rule && Exists(rule))
            RaiseLocalEvent(rule, new MisfitsEventRoleTakenEvent(roleKey, MetaData(mob).EntityName));

        // #Misfits Add - the role's own line and the run's briefing, e.g. the crime this role is wanted for.
        var briefing = ResolveBriefing(component);

        if (briefing is { } text)
            _chat.DispatchServerMessage(player, text);

        if (component.Objective is { } objective)
            GiveObjective(mob, objective, DescribeObjective(component, briefing));

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

    // The role's own line first, then the run's shared briefing; null when the role has neither.
    private string? ResolveBriefing(MisfitsGhostRoleCharacterPickerComponent component)
    {
        var own = OwnBriefing(component);
        var run = FillTokens(component.RunBriefing, component.Rule);

        if (own == null)
            return run;

        return run == null ? own : $"{own}\n\n{run}";
    }

    // One line from the role's own dataset, already filled in; null when it has none or its token is not known yet.
    private string? OwnBriefing(MisfitsGhostRoleCharacterPickerComponent component)
    {
        if (component.Briefing is not { } id ||
            !_prototypes.TryIndex<LocalizedDatasetPrototype>(id, out var dataset) ||
            dataset.Values.Count == 0)
        {
            return null;
        }

        return FillTokens(Loc.GetString(_random.Pick(dataset.Values)), component.Rule);
    }

    // Replaces $tokens with what the run knows, e.g. $criminal with the name of the body that took that role.
    // Null when the line needs a token the run cannot fill, so the line is dropped rather than shown half-written.
    private string? FillTokens(string? text, EntityUid? rule)
    {
        if (text == null || !text.Contains('$'))
            return text;

        if (rule is not { } owner)
            return null;

        var filled = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '$')
            {
                filled.Append(text[i]);
                continue;
            }

            var start = i + 1;
            var end = start;
            while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] == '_'))
                end++;

            if (end == start || !_events.TryGetRoleName(owner, text[start..end], out var name))
                return null;

            filled.Append(name);
            i = end - 1;
        }

        return filled.ToString();
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

    // startingGear prototypes one of which the spawned body is dressed with at random, so a role that spawns as a
    // crowd can vary its kits by listing more than one; see the startingGear entries.
    [DataField]
    public List<ProtoId<StartingGearPrototype>> Gear = new();

    // Job whose kit the role arrives in, and whose loadouts the player picked in the customize menu.
    [DataField]
    public ProtoId<JobPrototype>? Job;

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

    // Dataset rolled for this role alone, shown before the run's briefing, e.g. the ranger's warrant. Its $tokens are
    // filled with what the run knows, e.g. $criminal with the name of the body that took the role declaring
    // roleKey: criminal.
    [DataField]
    public ProtoId<LocalizedDatasetPrototype>? Briefing;

    // Key this role is filed under once taken, so another role's briefing can name its body; e.g. "criminal".
    [DataField]
    public string? RoleKey;

    // The run's own briefing line, stamped when the event spawns this role; shown under the role's own line.
    [ViewVariables]
    public string? RunBriefing;

    // The running event that placed this role; null when it was placed by hand. Needed to look tokens up.
    [ViewVariables]
    public EntityUid? Rule;


    // How long a picker may hold the role before it is offered again.
    [DataField]
    public TimeSpan PickerTimeout = TimeSpan.FromMinutes(2);

    // When the open picker stops reserving this role; null when nobody is picking.
    [ViewVariables]
    public TimeSpan? BusyUntil;
}
