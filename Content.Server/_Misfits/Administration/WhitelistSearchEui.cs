// #Misfits Change - Server-side EUI for the Whitelist Search admin panel
using System.Linq;
using System.Threading.Tasks;
using Content.Server.Administration.Managers;
using Content.Server.Administration.Logs;
using Content.Server.Administration;
using Content.Server.Chat.Managers;
using Content.Server.Database;
using Content.Server.EUI;
using Content.Server.MoMMI;
using Content.Server.Players.JobWhitelist;
using Content.Server._Misfits.Administration.WhitelistLogs;
using Content.Shared._Misfits.Administration;
using Content.Shared.Administration;
using Content.Shared.Database;
using Content.Shared.Eui;
using Content.Shared.Roles;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Log;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Server._Misfits.Administration;

public sealed class WhitelistSearchEui : BaseEui
{
    [Dependency] private readonly IAdminManager _admin = default!;
    [Dependency] private readonly ILogManager _log = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IServerDbManager _db = default!;
    [Dependency] private readonly IEntityManager _entManager = default!;
    [Dependency] private readonly JobWhitelistManager _jobWhitelist = default!;
    [Dependency] private readonly IAdminLogManager _adminLog = default!;
    [Dependency] private readonly IChatManager _chat = default!;
    [Dependency] private readonly IMoMMILink _mommi = default!;

    private readonly ISawmill _sawmill;

    private List<WhitelistPlayerInfo> _searchResults = new();
    private NetUserId? _selectedPlayerId;
    private string? _selectedPlayerName;
    private HashSet<ProtoId<JobPrototype>>? _whitelists;

    public WhitelistSearchEui()
    {
        IoCManager.InjectDependencies(this);
        _sawmill = _log.GetSawmill("admin.whitelist_search_eui");
    }

    public override EuiStateBase GetNewState()
    {
        return new WhitelistSearchEuiState(
            _searchResults,
            _selectedPlayerName,
            _selectedPlayerId,
            _whitelists);
    }

    public override async void HandleMessage(EuiMessageBase msg)
    {
        base.HandleMessage(msg);

        try
        {
            // #Misfits Change - Gate on Admin flag so all admins can manage whitelists.
            if (!_admin.HasAdminFlag(Player, AdminFlags.Admin))
            {
                _sawmill.Warning($"{Player.Name} ({Player.UserId}) tried to use whitelist search without permission");
                return;
            }

            switch (msg)
            {
                case SearchPlayersMessage search:
                    await HandleSearch(search.Query);
                    break;
                case SelectPlayerMessage select:
                    await HandleSelectPlayer(select.PlayerId);
                    break;
                case SetWhitelistSearchJobMessage setJob:
                    HandleSetJobs(setJob.Jobs, setJob.Whitelisting);
                    break;
            }
        }
        catch (Exception e)
        {
            _sawmill.Error($"Error handling EUI message: {e}");
        }
    }

    private async Task HandleSearch(string query)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length < 2)
        {
            _searchResults = new List<WhitelistPlayerInfo>();
            StateDirty();
            return;
        }

        try
        {
            var records = await _db.SearchPlayersByName(query);
            _searchResults = records
                .Select(r => new WhitelistPlayerInfo(r.UserId, r.LastSeenUserName))
                .ToList();
        }
        catch (Exception e)
        {
            _sawmill.Error($"Error searching players by name '{query}': {e}");
            _searchResults = new List<WhitelistPlayerInfo>();
        }

        StateDirty();
    }

    private async Task HandleSelectPlayer(NetUserId playerId)
    {
        var record = await _db.GetPlayerRecordByUserId(playerId);
        if (record == null)
        {
            _sawmill.Warning($"Admin {Player.Name} tried to select non-existent player {playerId}");
            return;
        }

        _selectedPlayerId = playerId;
        _selectedPlayerName = record.LastSeenUserName;

        _whitelists = new HashSet<ProtoId<JobPrototype>>();
        var jobs = await _db.GetJobWhitelists(playerId.UserId);
        foreach (var id in jobs)
        {
            if (_proto.HasIndex<JobPrototype>(id))
                _whitelists.Add(id);
        }

        StateDirty();
    }

    private void HandleSetJobs(List<ProtoId<JobPrototype>> jobs, bool whitelisting)
    {
        if (_selectedPlayerId == null || _whitelists == null)
            return;

        var validJobs = jobs
            .Where(job => _proto.HasIndex<JobPrototype>(job))
            .Distinct()
            .ToList();

        if (validJobs.Count == 0)
            return;

        if (whitelisting)
        {
            PromptWhitelistGrant(validJobs);
        }
        else
        {
            foreach (var job in validJobs)
            {
                _jobWhitelist.RemoveWhitelist(_selectedPlayerId.Value, job);
                _whitelists.Remove(job);
            }

            var removedJobs = string.Join(", ", validJobs);
            _sawmill.Info($"{Player.Name} ({Player.UserId}) removed job whitelist(s) [{removedJobs}] from player {_selectedPlayerName} ({_selectedPlayerId.Value.UserId})");
            _entManager.System<WhitelistLogSystem>().AddEntry(
                "Removed",
                Player.Name,
                _selectedPlayerName ?? _selectedPlayerId.Value.ToString(),
                removedJobs);
        }

        StateDirty();
    }

    private void PromptWhitelistGrant(List<ProtoId<JobPrototype>> jobs)
    {
        _entManager.System<QuickDialogSystem>().OpenDialog<string, string, string>(
            Player,
            "Job Whitelist Justification",
            "Reason:",
            "Discord Username:",
            "Application - YES | NO",
            (reason, discordUsername, applicationResponse) =>
            {
                reason = reason.Trim();
                discordUsername = discordUsername.Trim();
                applicationResponse = applicationResponse.Trim();

                var wasApplication = ParseYesNo(applicationResponse);

                if (string.IsNullOrWhiteSpace(reason) ||
                    string.IsNullOrWhiteSpace(discordUsername) ||
                    wasApplication == null)
                {
                    _chat.DispatchServerMessage(Player, "Reason, Discord username, and YES or NO for application are all required.");
                    StateDirty();
                    PromptWhitelistGrant(jobs);
                    return;
                }

                ApplyWhitelistGrant(jobs, reason, discordUsername, wasApplication.Value);
            },
            () => StateDirty());
    }

    private void ApplyWhitelistGrant(List<ProtoId<JobPrototype>> jobs, string reason, string discordUsername, bool wasApplication)
    {
        if (_selectedPlayerId == null || _whitelists == null || _selectedPlayerName == null)
            return;

        var addedJobs = new List<ProtoId<JobPrototype>>();
        foreach (var job in jobs)
        {
            if (_whitelists.Contains(job))
                continue;

            _jobWhitelist.AddWhitelist(_selectedPlayerId.Value, job);
            _whitelists.Add(job);
            addedJobs.Add(job);
        }

        if (addedJobs.Count == 0)
        {
            StateDirty();
            return;
        }

        var jobList = string.Join(", ", addedJobs);
        var applicationText = wasApplication ? "Yes" : "No";

        _sawmill.Info(
            $"{Player.Name} ({Player.UserId}) added job whitelist(s) [{jobList}] to player {_selectedPlayerName} ({_selectedPlayerId.Value.UserId}) | reason={reason} | was_application={applicationText} | discord={discordUsername}");

        _adminLog.Add(
            LogType.AdminMessage,
            LogImpact.Medium,
            $"{Player:actor} granted job whitelist(s) [{jobList}] to {_selectedPlayerName:subject}. Reason: {reason}. Was application: {applicationText}. Discord: {discordUsername}");

        var adminNotice =
            $"Admin {Player.Name} has given {_selectedPlayerName} job whitelist(s) for {jobList}. Reason: {reason}. Was application: {applicationText}. Discord: {discordUsername}.";

        _chat.SendAdminAnnouncement(adminNotice);
        _mommi.SendAdminChatMessage(Player.Name, adminNotice);
        _entManager.System<WhitelistLogSystem>().AddEntry(
            "Granted",
            Player.Name,
            _selectedPlayerName,
            jobList,
            reason,
            discordUsername,
            applicationText);

        StateDirty();
    }

    private static bool? ParseYesNo(string input)
    {
        return input.ToUpperInvariant() switch
        {
            "YES" or "Y" => true,
            "NO" or "N" => false,
            _ => null,
        };
    }
}
