// #Cythisiax Added - Faction rank system, replaces NCRRankPinSpawnSystem and RankTitleSystem.
// Every rank is set in the job yaml under rank.
//  - Pin factions (NCR, Enclave) list the pins the job works up through and the hours each takes.
//    The pin is the rank, whoever wears it (on the neck or pinned to their uniform or armor) gets
//    its prefix. Each character has a saved row per job with the pin they chose to wear, plus
//    whatever leadership set through the roster. The highest they can wear is what playtime earns,
//    or what leadership granted if that's higher. A leadership demotion is a hard cap until
//    leadership changes it again. Nobody gets moved up on their own. The first time they spawn they
//    wear the highest they've earned, after that they stay put and get told when a higher rank is
//    open, and they pick it in the lobby if they want it.
//  - Factions without pins (Rangers, Legion, BoS) write the rank right in the job, and it's stamped
//    on their ID tag at spawn. Whoever has that ID tag gets its prefix.
// A worn pin wins over the ID tag. The ID's job title stays as it is either way.
// In chat and radio the prefix goes in front of their name, like 1SG John Doe, but only when
// they're talking as themselves. Masked, disguised or no-ID speakers keep whatever name they show as.

using System.Linq;
using System.Threading.Tasks;
using Content.Server.Access.Systems;
using Content.Server.Chat.Managers;
using Content.Server.Database;
using Content.Server.GameTicking;
using Content.Server.IdentityManagement;
using Content.Server.Inventory;
using Content.Server.Players.PlayTimeTracking;
using Content.Shared._Misfits.Clothing.Pins;
using Content.Shared._Misfits.FactionRank;
using Content.Shared.Chat;
using Content.Shared.Examine;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Humanoid;
using Content.Shared.IdentityManagement;
using Content.Shared.Inventory;
using Content.Shared.Roles;
using Robust.Shared.Containers;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server._Misfits.FactionRank;

public sealed class FactionRankSystem : EntitySystem
{
    [Dependency] private readonly IdCardSystem _idCard = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly PlayTimeTrackingManager _playTime = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IComponentFactory _factory = default!;
    [Dependency] private readonly IServerDbManager _db = default!;
    [Dependency] private readonly IChatManager _chat = default!;

    private const string InsigniaSlot = "neck";

    public override void Initialize()
    {
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawnComplete);
        SubscribeLocalEvent<HumanoidAppearanceComponent, TransformSpeakerNameEvent>(OnTransformSpeakerName,
            after: new[] { typeof(IdentitySystem), typeof(ServerInventorySystem) });
        SubscribeLocalEvent<InventoryComponent, ExaminedEvent>(OnExamined);
        SubscribeNetworkEvent<RequestFactionRankOptionsEvent>(OnRequestOptions);
        SubscribeNetworkEvent<SetFactionRankEvent>(OnSetRank);
    }

    #region Spawn

    private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent args)
    {
        if (args.JobId == null || !_proto.TryIndex<JobPrototype>(args.JobId, out var job) || job.Rank is not { } rank)
            return;

        if (rank.Pins.Count > 0)
        {
            var holder = EnsureComp<FactionRankHolderComponent>(args.Mob);
            holder.Job = job.ID;
            holder.UserId = args.Player.UserId;
            holder.CharacterName = args.Profile.Name;
            LoadAndApplyPin(args.Mob, args.Player, job);
            return;
        }

        if (rank.Prefix == null || !_idCard.TryFindIdCard(args.Mob, out var idCard))
            return;

        var comp = EnsureComp<FactionRankComponent>(idCard);
        comp.Name = rank.Name ?? rank.Prefix;
        comp.Prefix = rank.Prefix;
        comp.Paygrade = rank.Paygrade;
        comp.Color = rank.Color;
        Dirty(idCard, comp);
    }

    private async void LoadAndApplyPin(EntityUid mob, ICommonSession player, JobPrototype job)
    {
        var characterName = Comp<FactionRankHolderComponent>(mob).CharacterName;
        FactionCharacterRank? row = null;

        try
        {
            var rows = await _db.GetFactionCharacterRanksAsync(player.UserId.UserId, characterName);
            row = rows.FirstOrDefault(r => r.Ladder == job.ID);
        }
        catch (Exception e)
        {
            Log.Error($"Failed to load faction rank for {player.Name} ({characterName}): {e}");
        }

        if (TerminatingOrDeleted(mob) || !TryComp<FactionRankHolderComponent>(mob, out var holder))
            return;

        holder.GrantedRank = row?.GrantedRank;
        holder.GrantCapped = row?.GrantCapped ?? false;

        var pins = job.Rank!.Pins;
        var max = MaxIndex(pins, EarnedIndex(player, job), holder.GrantedRank, holder.GrantCapped);
        if (max < 0)
            return;

        var held = PinIndex(pins, row?.HeldRank);
        var save = false;
        if (held < 0 || held > max)
        {
            held = max;
            save = true;
        }

        holder.HeldRank = pins[held].Pin.Id;
        if (save)
            SaveHolder(holder);

        ApplyPin(mob, pins, held);

        if (held < max)
        {
            _chat.DispatchServerMessage(player,
                Loc.GetString("faction-rank-available", ("rank", PinName(pins[max].Pin))));
        }
    }

    #endregion

    #region Ladders

    private static int PinIndex(List<JobRankPin> pins, string? pin)
    {
        return pin == null ? -1 : pins.FindIndex(p => p.Pin.Id == pin);
    }

    private int EarnedIndex(ICommonSession player, JobPrototype job)
    {
        var hours = 0.0;
        if (_playTime.TryGetTrackerTimes(player, out var times))
            hours = GetTrackedTime(times, job).TotalHours;

        var pins = job.Rank!.Pins;
        var index = -1;
        for (var i = 0; i < pins.Count; i++)
        {
            if (hours >= pins[i].Hours)
                index = i;
        }

        return index;
    }

    private static int MaxIndex(List<JobRankPin> pins, int earned, string? granted, bool capped)
    {
        var grant = PinIndex(pins, granted);
        if (grant < 0)
            return earned;

        return capped ? grant : Math.Max(grant, earned);
    }

    private TimeSpan GetTrackedTime(Dictionary<string, TimeSpan> times, JobPrototype job)
    {
        if (job.Rank?.Department is not { } deptId)
            return times.GetValueOrDefault(job.PlayTimeTracker.Id);

        if (!_proto.TryIndex(deptId, out var dept))
            return TimeSpan.Zero;

        var total = TimeSpan.Zero;
        foreach (var role in dept.Roles)
        {
            var trackerId = _proto.TryIndex<JobPrototype>(role, out var roleJob)
                ? roleJob.PlayTimeTracker.Id
                : role.Id;
            total += times.GetValueOrDefault(trackerId);
        }

        return total;
    }

    private string PinName(EntProtoId pin)
    {
        return _proto.TryIndex(pin, out var proto) && proto.TryGetComponent<FactionRankComponent>(out var rank, _factory)
            ? rank.Name
            : pin.Id;
    }

    #endregion

    #region Roster

    public bool TryGetPinStanding(EntityUid body, out string rankName, out int index, out int count)
    {
        rankName = string.Empty;
        index = -1;
        count = 0;

        if (!TryComp<FactionRankHolderComponent>(body, out var holder) ||
            !_proto.TryIndex(holder.Job, out var job) ||
            job.Rank is not { } rank)
        {
            return false;
        }

        index = PinIndex(rank.Pins, holder.HeldRank);
        if (index < 0)
            return false;

        rankName = PinName(rank.Pins[index].Pin);
        count = rank.Pins.Count;
        return true;
    }

    public bool TryStepPinRank(EntityUid body, ICommonSession session, bool promote, out string newRankName)
    {
        newRankName = string.Empty;

        if (!TryComp<FactionRankHolderComponent>(body, out var holder) ||
            !_proto.TryIndex(holder.Job, out var job) ||
            job.Rank is not { } rank)
        {
            return false;
        }

        var pins = rank.Pins;
        var held = PinIndex(pins, holder.HeldRank);
        var next = promote ? held + 1 : held - 1;
        if (held < 0 || next < 0 || next >= pins.Count)
            return false;

        holder.GrantedRank = pins[next].Pin.Id;
        holder.GrantCapped = !promote && next < EarnedIndex(session, job);
        holder.HeldRank = pins[next].Pin.Id;
        SaveHolder(holder);
        ApplyPin(body, pins, next);

        newRankName = PinName(pins[next].Pin);
        return true;
    }

    private async void SaveHolder(FactionRankHolderComponent holder)
    {
        try
        {
            await _db.UpsertFactionCharacterRankAsync(new FactionCharacterRank
            {
                PlayerId = holder.UserId.UserId,
                CharacterName = holder.CharacterName,
                Ladder = holder.Job.Id,
                HeldRank = holder.HeldRank,
                GrantedRank = holder.GrantedRank,
                GrantCapped = holder.GrantCapped,
            });
        }
        catch (Exception e)
        {
            Log.Error($"Failed to save faction rank for {holder.CharacterName}: {e}");
        }
    }

    #endregion

    #region Lobby

    private async void OnRequestOptions(RequestFactionRankOptionsEvent ev, EntitySessionEventArgs args)
    {
        var session = args.SenderSession;
        var rows = await LoadRows(session, ev.CharacterName);
        RaiseNetworkEvent(new FactionRankOptionsEvent(ev.CharacterName, BuildOptions(session, rows)), session);
    }

    private async void OnSetRank(SetFactionRankEvent ev, EntitySessionEventArgs args)
    {
        var session = args.SenderSession;
        if (!_proto.TryIndex<JobPrototype>(ev.Job, out var job) || job.Rank is not { Pins.Count: > 0 } rank)
            return;

        var rows = await LoadRows(session, ev.CharacterName);
        var row = rows.FirstOrDefault(r => r.Ladder == job.ID);

        var max = MaxIndex(rank.Pins, EarnedIndex(session, job), row?.GrantedRank, row?.GrantCapped ?? false);
        var pick = PinIndex(rank.Pins, ev.Pin);
        if (pick < 0 || pick > max)
            return;

        row ??= new FactionCharacterRank
        {
            PlayerId = session.UserId.UserId,
            CharacterName = ev.CharacterName,
            Ladder = job.ID,
        };
        row.HeldRank = ev.Pin;

        try
        {
            await _db.UpsertFactionCharacterRankAsync(row);
        }
        catch (Exception e)
        {
            Log.Error($"Failed to save faction rank pick for {session.Name} ({ev.CharacterName}): {e}");
            return;
        }

        rows.RemoveAll(r => r.Ladder == job.ID);
        rows.Add(row);
        RaiseNetworkEvent(new FactionRankOptionsEvent(ev.CharacterName, BuildOptions(session, rows)), session);
    }

    private async Task<List<FactionCharacterRank>> LoadRows(ICommonSession session, string characterName)
    {
        try
        {
            return await _db.GetFactionCharacterRanksAsync(session.UserId.UserId, characterName);
        }
        catch (Exception e)
        {
            Log.Error($"Failed to load faction ranks for {session.Name} ({characterName}): {e}");
            return new List<FactionCharacterRank>();
        }
    }

    private List<FactionRankJobOptions> BuildOptions(ICommonSession session, List<FactionCharacterRank> rows)
    {
        var result = new List<FactionRankJobOptions>();
        foreach (var job in _proto.EnumeratePrototypes<JobPrototype>())
        {
            if (job.Rank is not { Pins.Count: > 0 } rank)
                continue;

            var row = rows.FirstOrDefault(r => r.Ladder == job.ID);
            var capped = row?.GrantCapped ?? false;
            var earned = EarnedIndex(session, job);
            var max = MaxIndex(rank.Pins, earned, row?.GrantedRank, capped);

            var held = PinIndex(rank.Pins, row?.HeldRank);
            if (held < 0 || held > max)
                held = max;

            var options = new List<FactionRankOption>();
            for (var i = 0; i < rank.Pins.Count; i++)
            {
                var pin = rank.Pins[i];
                options.Add(new FactionRankOption(
                    pin.Pin.Id,
                    PinName(pin.Pin),
                    i <= max,
                    pin.Hours,
                    capped && i > max && i <= earned));
            }

            result.Add(new FactionRankJobOptions(job.ID, options, held >= 0 ? rank.Pins[held].Pin.Id : null));
        }

        return result;
    }

    #endregion

    #region Prefix

    public FactionRankComponent? GetRank(EntityUid wearer)
    {
        foreach (var pin in WornPins(wearer))
        {
            if (!TerminatingOrDeleted(pin) && TryComp<FactionRankComponent>(pin, out var pinRank))
                return pinRank;
        }

        if (_idCard.TryFindIdCard(wearer, out var idCard) &&
            TryComp<FactionRankComponent>(idCard, out var idRank))
        {
            return idRank;
        }

        return null;
    }

    private void OnTransformSpeakerName(Entity<HumanoidAppearanceComponent> ent, ref TransformSpeakerNameEvent args)
    {
        if (args.VoiceName != Name(ent))
            return;

        var rank = GetRank(ent);
        if (rank == null || string.IsNullOrWhiteSpace(rank.Prefix))
            return;

        args.VoiceName = $"{rank.Prefix} {args.VoiceName}";
        args.RankPrefix = rank.Prefix;
        args.RankColor = rank.Color;
    }

    private void OnExamined(Entity<InventoryComponent> ent, ref ExaminedEvent args)
    {
        var rank = GetRank(ent);
        if (rank == null)
            return;

        var rankText = string.IsNullOrEmpty(rank.Paygrade)
            ? rank.Name
            : $"{rank.Name} ({rank.Paygrade})";

        using (args.PushGroup(nameof(FactionRankSystem)))
        {
            args.PushMarkup(Loc.GetString("faction-rank-examine",
                ("user", Identity.Entity(ent, EntityManager)),
                ("rank", FormattedMessage.EscapeText(rankText))));
        }
    }

    #endregion

    #region Pins

    private void ApplyPin(EntityUid mob, List<JobRankPin> pins, int index)
    {
        var insignia = pins[index].Pin.Id;
        var jobPins = pins.Select(p => p.Pin.Id).ToHashSet();
        foreach (var pin in WornPins(mob).ToList())
        {
            var id = MetaData(pin).EntityPrototype?.ID;
            if (id != null && id != insignia && jobPins.Contains(id))
                Del(pin);
        }

        if (WornPins(mob).Any(p => MetaData(p).EntityPrototype?.ID == insignia))
            return;

        var newPin = Spawn(insignia, Transform(mob).Coordinates);

        if (!_inventory.TryGetSlotEntity(mob, InsigniaSlot, out _) &&
            _inventory.TryEquip(mob, newPin, InsigniaSlot, silent: true, force: true))
        {
            return;
        }

        _hands.TryPickupAnyHand(mob, newPin, checkActionBlocker: false);
    }

    private IEnumerable<EntityUid> WornPins(EntityUid wearer)
    {
        if (_inventory.TryGetSlotEntity(wearer, InsigniaSlot, out var neckItem))
            yield return neckItem.Value;

        var slots = _inventory.GetSlotEnumerator(wearer, SlotFlags.INNERCLOTHING | SlotFlags.OUTERCLOTHING);
        while (slots.MoveNext(out var slot))
        {
            if (slot.ContainedEntity is not { } clothing ||
                !TryComp<ClothingPinHolderComponent>(clothing, out var holder) ||
                !_containers.TryGetContainer(clothing, holder.ContainerId, out var container))
            {
                continue;
            }

            foreach (var pin in container.ContainedEntities)
                yield return pin;
        }
    }

    #endregion
}
