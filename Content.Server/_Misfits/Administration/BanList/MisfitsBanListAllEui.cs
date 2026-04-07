using System.Threading.Tasks;
using Content.Server.Administration;
using Content.Server.Administration.Managers;
using Content.Server.Database;
using Content.Server.EUI;
using Content.Shared._Misfits.Administration.BanList;
using Content.Shared.Administration;
using Content.Shared.Administration.BanList;
using Content.Shared.Eui;
using Robust.Shared.Network;

// #Misfits Add - Server EUI for the global ban list (banlistall command), shows all bans across all players
namespace Content.Server._Misfits.Administration.BanList;

public sealed class MisfitsBanListAllEui : BaseEui
{
    [Dependency] private readonly IAdminManager _admins = default!;
    [Dependency] private readonly IPlayerLocator _playerLocator = default!;
    [Dependency] private readonly IServerDbManager _db = default!;

    // Stored state fields — populated by LoadAllBans(), read by GetNewState()
    private List<MisfitsBanEntry> _activeBans = new();
    private List<MisfitsRoleBanEntry> _activeRoleBans = new();
    private List<MisfitsBanEntry> _allBans = new();
    private List<MisfitsRoleBanEntry> _allRoleBans = new();

    public MisfitsBanListAllEui()
    {
        IoCManager.InjectDependencies(this);
    }

    public override void Opened()
    {
        base.Opened();
        _admins.OnPermsChanged += OnPermsChanged;

        // Load all ban data on open; fire-and-forget, StateDirty() at end
        _ = LoadAllBans();
    }

    public override void Closed()
    {
        base.Closed();
        _admins.OnPermsChanged -= OnPermsChanged;
    }

    public override EuiStateBase GetNewState()
    {
        return new MisfitsBanListAllEuiState(_activeBans, _activeRoleBans, _allBans, _allRoleBans);
    }

    private void OnPermsChanged(AdminPermsChangedEventArgs args)
    {
        // Close if the opening admin loses ban permissions
        if (args.Player == Player && !_admins.HasAdminFlag(Player, AdminFlags.Ban))
            Close();
    }

    /// <summary>
    /// Loads all four ban lists from the database, resolves player names, then calls StateDirty().
    /// </summary>
    public async Task LoadAllBans()
    {
        var activeBanDefs = await _db.GetAllServerBansAsync(includeUnbanned: false);
        var allBanDefs = await _db.GetAllServerBansAsync(includeUnbanned: true);
        var activeRoleBanDefs = await _db.GetAllServerRoleBansAsync(includeUnbanned: false);
        var allRoleBanDefs = await _db.GetAllServerRoleBansAsync(includeUnbanned: true);

        // Collect unique user IDs across all lists to minimise DB round-trips
        var allUserIds = new HashSet<NetUserId>();
        foreach (var ban in allBanDefs)
            if (ban.UserId.HasValue) allUserIds.Add(ban.UserId.Value);
        foreach (var ban in allRoleBanDefs)
            if (ban.UserId.HasValue) allUserIds.Add(ban.UserId.Value);

        // Build a name cache: UserId -> resolved username
        var nameCache = new Dictionary<NetUserId, string>();
        foreach (var uid in allUserIds)
        {
            var record = await _playerLocator.LookupIdAsync(uid);
            nameCache[uid] = record?.Username ?? uid.ToString();
        }

        var showPii = _admins.HasAdminFlag(Player, AdminFlags.Pii);

        // Convert and store
        _activeBans = await ConvertBans(activeBanDefs, nameCache, showPii);
        _allBans = await ConvertBans(allBanDefs, nameCache, showPii);
        _activeRoleBans = await ConvertRoleBans(activeRoleBanDefs, nameCache, showPii);
        _allRoleBans = await ConvertRoleBans(allRoleBanDefs, nameCache, showPii);

        StateDirty();
    }

    private async Task<List<MisfitsBanEntry>> ConvertBans(
        List<ServerBanDef> defs,
        Dictionary<NetUserId, string> nameCache,
        bool showPii)
    {
        var result = new List<MisfitsBanEntry>(defs.Count);

        foreach (var ban in defs)
        {
            SharedServerUnban? unban = null;
            if (ban.Unban is { } unbanDef)
            {
                var unbannerName = unbanDef.UnbanningAdmin == null
                    ? null
                    : (await _playerLocator.LookupIdAsync(unbanDef.UnbanningAdmin.Value))?.Username;
                unban = new SharedServerUnban(unbannerName, ban.Unban.UnbanTime.UtcDateTime);
            }

            (string, int cidrMask)? ip = ("*Hidden*", 0);
            var hwid = "*Hidden*";
            if (showPii)
            {
                ip = ban.Address is { } address
                    ? (address.address.ToString(), address.cidrMask)
                    : null;
                hwid = ban.HWId?.ToString();
            }

            var bannerName = ban.BanningAdmin == null
                ? null
                : (await _playerLocator.LookupIdAsync(ban.BanningAdmin.Value))?.Username;

            var playerName = ban.UserId.HasValue && nameCache.TryGetValue(ban.UserId.Value, out var n)
                ? n
                : "Unknown";

            var sharedBan = new SharedServerBan(
                ban.Id,
                ban.UserId,
                ip,
                hwid,
                ban.BanTime.UtcDateTime,
                ban.ExpirationTime?.UtcDateTime,
                ban.Reason,
                bannerName,
                unban
            );

            result.Add(new MisfitsBanEntry(sharedBan, playerName));
        }

        return result;
    }

    private async Task<List<MisfitsRoleBanEntry>> ConvertRoleBans(
        List<ServerRoleBanDef> defs,
        Dictionary<NetUserId, string> nameCache,
        bool showPii)
    {
        var result = new List<MisfitsRoleBanEntry>(defs.Count);

        foreach (var ban in defs)
        {
            SharedServerUnban? unban = null;
            if (ban.Unban is { } unbanDef)
            {
                var unbannerName = unbanDef.UnbanningAdmin == null
                    ? null
                    : (await _playerLocator.LookupIdAsync(unbanDef.UnbanningAdmin.Value))?.Username;
                unban = new SharedServerUnban(unbannerName, ban.Unban.UnbanTime.UtcDateTime);
            }

            (string, int cidrMask)? ip = ("*Hidden*", 0);
            var hwid = "*Hidden*";
            if (showPii)
            {
                ip = ban.Address is { } address
                    ? (address.address.ToString(), address.cidrMask)
                    : null;
                hwid = ban.HWId?.ToString();
            }

            var bannerName = ban.BanningAdmin == null
                ? null
                : (await _playerLocator.LookupIdAsync(ban.BanningAdmin.Value))?.Username;

            var playerName = ban.UserId.HasValue && nameCache.TryGetValue(ban.UserId.Value, out var n)
                ? n
                : "Unknown";

            var sharedRoleBan = new SharedServerRoleBan(
                ban.Id,
                ban.UserId,
                ip,
                hwid,
                ban.BanTime.UtcDateTime,
                ban.ExpirationTime?.UtcDateTime,
                ban.Reason,
                bannerName,
                unban,
                ban.Role
            );

            result.Add(new MisfitsRoleBanEntry(sharedRoleBan, playerName));
        }

        return result;
    }
}
