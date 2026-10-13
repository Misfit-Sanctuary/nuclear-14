using System.Linq;
using System.Threading.Tasks;
using Content.Server.Administration.Managers;
using Content.Server.Administration.Logs;
using Content.Server.Chat.Managers;
using Content.Server.Database;
using Content.Server.EUI;
using Content.Server.MoMMI;
using Content.Server.Players.JobWhitelist;
using Content.Server._Misfits.Administration.WhitelistLogs;
using Content.Shared.Administration;
using Content.Shared.Database;
using Content.Shared.Eui;
using Content.Shared.Roles;
using Robust.Shared.GameObjects;
using Robust.Shared.Log;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Server.Administration;

public sealed class JobWhitelistsEui : BaseEui
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

    public NetUserId PlayerId;
    public string PlayerName;

    public HashSet<ProtoId<JobPrototype>> Whitelists = new();

    public JobWhitelistsEui(NetUserId playerId, string playerName)
    {
        IoCManager.InjectDependencies(this);

        _sawmill = _log.GetSawmill("admin.job_whitelists_eui");

        PlayerId = playerId;
        PlayerName = playerName;
    }

    public async void LoadWhitelists()
    {
        var jobs = await _db.GetJobWhitelists(PlayerId.UserId);
        foreach (var id in jobs)
        {
            if (_proto.HasIndex<JobPrototype>(id))
                Whitelists.Add(id);
        }

        StateDirty();
    }

    public override EuiStateBase GetNewState()
    {
        return new JobWhitelistsEuiState(PlayerName, Whitelists);
    }

    public override void HandleMessage(EuiMessageBase msg)
    {
        base.HandleMessage(msg);

        if (msg is not SetJobWhitelistedMessage args)
            return;

        if (!_admin.HasAdminFlag(Player, AdminFlags.Whitelist))
        {
            _sawmill.Warning($"{Player.Name} ({Player.UserId}) tried to change role whitelists for {PlayerName} without whitelists flag");
            return;
        }

        var jobs = args.Jobs
            .Where(job => _proto.HasIndex<JobPrototype>(job))
            .Distinct()
            .ToList();

        if (jobs.Count == 0)
            return;

        if (args.Whitelisting)
        {
            PromptWhitelistGrant(jobs);
        }
        else
        {
            foreach (var job in jobs)
            {
                _jobWhitelist.RemoveWhitelist(PlayerId, job);
                Whitelists.Remove(job);
            }

            var removedJobs = string.Join(", ", jobs);
            _sawmill.Info($"{Player.Name} ({Player.UserId}) removed job whitelist(s) [{removedJobs}] from player {PlayerName} ({PlayerId.UserId})");
            _entManager.System<WhitelistLogSystem>().AddEntry(
                "Removed",
                Player.Name,
                PlayerName,
                removedJobs);
            StateDirty();
        }
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
                    wasApplication == null ||
                    string.IsNullOrWhiteSpace(discordUsername))
                {
                    _chat.DispatchServerMessage(Player, "Reason, YES or NO for application, and Discord username are all required.");
                    StateDirty();
                    PromptWhitelistGrant(jobs);
                    return;
                }

                ApplyWhitelistGrant(jobs, reason, wasApplication.Value, discordUsername);
            },
            () => StateDirty());
    }

    private void ApplyWhitelistGrant(
        List<ProtoId<JobPrototype>> jobs,
        string reason,
        bool wasApplication,
        string discordUsername)
    {
        var addedJobs = new List<ProtoId<JobPrototype>>();
        foreach (var job in jobs)
        {
            if (Whitelists.Contains(job))
                continue;

            _jobWhitelist.AddWhitelist(PlayerId, job);
            Whitelists.Add(job);
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
            $"{Player.Name} ({Player.UserId}) added job whitelist(s) [{jobList}] to player {PlayerName} ({PlayerId.UserId}) | reason={reason} | was_application={applicationText} | discord={discordUsername}");

        _adminLog.Add(
            LogType.AdminMessage,
            LogImpact.Medium,
            $"{Player:actor} granted job whitelist(s) [{jobList}] to {PlayerName:subject}. Reason: {reason}. Was application: {applicationText}. Discord: {discordUsername}");

        var adminNotice =
            $"Admin {Player.Name} has given {PlayerName} job whitelist(s) for {jobList}. Reason: {reason}. Was application: {applicationText}. Discord: {discordUsername}.";

        _chat.SendAdminAnnouncement(adminNotice);
        _mommi.SendAdminChatMessage(Player.Name, adminNotice);
        _entManager.System<WhitelistLogSystem>().AddEntry(
            "Granted",
            Player.Name,
            PlayerName,
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
