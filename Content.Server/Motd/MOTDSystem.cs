// #Misfits Add - Message of the Day window, its pictures and its poll, shown to players on join.
using System;
using System.Linq;
using System.Text.Json;
using Content.Server.Administration.Logs;
using Content.Server.Administration.Managers;
using Content.Server.GameTicking;
using Content.Shared.Administration;
using Content.Shared.CCVar;
using Content.Shared.Database;
using Content.Shared.Motd;
using Robust.Server.Player;
using Robust.Shared.ContentPack;
using Robust.Shared.Configuration;
using Robust.Shared.Console;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server.Motd;

/// The system that supplies the Message of the Day window to players, when they join the server, when the message changes, or when they ask for it. The window also holds the pictures and the poll.
public sealed class MOTDSystem : EntitySystem
{
    public const int MaxImages = 4;

    /// How often a single player may change their vote, so voting cannot be used to spam every client with updates.
    private static readonly TimeSpan VoteCooldown = TimeSpan.FromSeconds(1);

    /// Where the poll and its votes are stored, so they survive a server restart.
    private static readonly ResPath PollStoragePath = new("/motd_poll.json");

    private static readonly JsonSerializerOptions PollJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    [Dependency] private readonly IAdminLogManager _adminLogManager = default!;
    [Dependency] private readonly IAdminManager _adminManager = default!;
    [Dependency] private readonly IConfigurationManager _configurationManager = default!;
    [Dependency] private readonly INetManager _netManager = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly IResourceManager _resourceManager = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private static readonly TimeSpan PollSaveDelay = TimeSpan.FromSeconds(5);

    private readonly Dictionary<NetUserId, TimeSpan> _lastVote = new();
    private readonly List<NetUserId> _staleVoters = new();

    private bool _pollDirty;
    private TimeSpan _nextPollSave;

    private string _messageOfTheDay = "";

    private List<string> _images = new();

    private MotdPoll? _poll;

    private readonly HashSet<NetUserId> _shownThisConnection = new();

    public MotdPoll? Poll => _poll;

    public bool HasContent => !string.IsNullOrEmpty(_messageOfTheDay) || _poll != null;

    public override void Initialize()
    {
        base.Initialize();

        _netManager.RegisterNetMessage<MotdUpdateMessage>();
        _netManager.RegisterNetMessage<MotdVoteMessage>(OnVoteMessage);
        _netManager.RegisterNetMessage<MotdEditMessage>(OnEditMessage);

        Subs.CVar(_configurationManager, CCVars.MOTD, OnMOTDChanged, invokeImmediately: true);
        Subs.CVar(_configurationManager, CCVars.MOTDImages, OnImagesChanged, invokeImmediately: true);

        SubscribeLocalEvent<PlayerJoinedLobbyEvent>(OnPlayerJoinedLobby);
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawnComplete);

        LoadPoll();
    }

    /// Saves a burst of votes once it settles, instead of writing the file on every click.
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_pollDirty && _timing.CurTime >= _nextPollSave)
            WritePoll();
    }

    public override void Shutdown()
    {
        base.Shutdown();

        if (_pollDirty)
            WritePoll();
    }

    public void SendMotd(ICommonSession session, bool open = true)
    {
        SendMotd(session, open, BuildSnapshot());
    }

    /// Sends the current Message of the Day to every connected player, opening the window for changes players are meant to see while votes only refresh it.
    public void BroadcastMotd(bool open)
    {
        var snapshot = BuildSnapshot();
        foreach (var session in _playerManager.Sessions)
            SendMotd(session, open, snapshot);
    }

    private void SendMotd(ICommonSession session, bool open, MotdSnapshot snapshot)
    {
        _netManager.ServerSendMessage(BuildMessage(session, open, snapshot), session.Channel);
    }

    public void TrySendMOTD(IConsoleShell shell)
    {
        if (!HasContent)
        {
            shell.WriteLine(Loc.GetString("motd-none"));
            return;
        }

        if (shell.Player is { } player)
            SendMotd(player);

        foreach (var line in BuildText().Split('\n'))
            shell.WriteLine(line);
    }

    /// Sends only the current poll to the player behind a console shell, and prints its results to the console.
    public void TrySendPoll(IConsoleShell shell, bool sendWindow = true)
    {
        if (_poll is not { } poll)
        {
            shell.WriteLine(Loc.GetString("cmd-motd-vote-no-poll"));
            return;
        }

        if (sendWindow && shell.Player is { } player)
            SendMotd(player, open: false);

        foreach (var line in BuildPollText(poll).Split('\n'))
            shell.WriteLine(line);
    }

    public void SetPoll(string question, IReadOnlyList<string> options)
    {
        _poll = new MotdPoll(question, options);
        WritePoll();
        BroadcastMotd(open: true);
    }

    public void ClearPoll()
    {
        if (_poll == null)
            return;

        _poll = null;
        WritePoll();
        BroadcastMotd(open: true);
    }

    public MotdVoteResult Vote(ICommonSession voter, int option)
    {
        if (_poll is not { } poll)
            return MotdVoteResult.NoPoll;

        if (option < 1 || option > poll.Options.Count)
            return MotdVoteResult.InvalidOption;

        var previous = poll.GetVote(voter.UserId);
        poll.RecordVote(voter.UserId, option);

        MarkPollDirty();

        return previous != null && previous != option
            ? MotdVoteResult.Changed
            : MotdVoteResult.Voted;
    }

    /// Normalizes a picture path so it always resolves inside the client's textures, or returns null when it is not a valid resource path.
    public static string? NormalizeImagePath(string path)
    {
        var trimmed = path.Trim().Replace('\\', '/');
        if (trimmed.Length == 0)
            return null;

        if (!trimmed.StartsWith('/'))
        {
            trimmed = trimmed.StartsWith("Textures/", StringComparison.OrdinalIgnoreCase)
                ? "/" + trimmed
                : "/Textures/" + trimmed;
        }

        try
        {
            _ = new ResPath(trimmed);
        }
        catch (ArgumentException)
        {
            return null;
        }

        return trimmed;
    }

    private void WritePoll()
    {
        try
        {
            var stored = _poll == null
                ? null
                : new StoredMotdPoll
                {
                    Question = _poll.Question,
                    Options = _poll.Options.ToList(),
                    Votes = _poll.Votes.ToDictionary(pair => pair.Key.UserId, pair => pair.Value),
                };

            _resourceManager.UserData.WriteAllText(PollStoragePath, JsonSerializer.Serialize(stored, PollJsonOptions));
            _pollDirty = false;
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to save the MOTD poll: {ex}");
        }
    }

    /// Marks the poll as changed, so a burst of votes ends up in a single write.
    private void MarkPollDirty()
    {
        if (_pollDirty)
            return;

        _pollDirty = true;
        _nextPollSave = _timing.CurTime + PollSaveDelay;
    }

    /// Restores the poll saved by a previous server run, ignoring anything that is no longer valid.
    private void LoadPoll()
    {
        try
        {
            if (!_resourceManager.UserData.Exists(PollStoragePath))
                return;

            var stored = JsonSerializer.Deserialize<StoredMotdPoll>(
                _resourceManager.UserData.ReadAllText(PollStoragePath), PollJsonOptions);

            if (stored == null || string.IsNullOrWhiteSpace(stored.Question)
                || stored.Options.Count < 2 || stored.Options.Count > MotdPoll.MaxOptions)
                return;

            var votes = new Dictionary<NetUserId, int>();
            foreach (var (voter, option) in stored.Votes)
            {
                if (option < 1 || option > stored.Options.Count)
                    continue;

                votes[new NetUserId(voter)] = option;
            }

            _poll = new MotdPoll(stored.Question, stored.Options, votes);
            Log.Info($"Restored the MOTD poll \"{stored.Question}\" with {votes.Count} vote(s).");
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to load the MOTD poll: {ex}");
        }
    }

    /// Works out everything the window shows except who voted for what, so a broadcast only does it once.
    private MotdSnapshot BuildSnapshot()
    {
        var snapshot = new MotdSnapshot
        {
            Text = _messageOfTheDay,
            Images = _images.ToArray(),
        };

        if (_poll is not { } poll)
            return snapshot;

        snapshot.Question = poll.Question;
        snapshot.Options = poll.Options.ToArray();
        snapshot.Votes = poll.GetTallies();
        snapshot.TotalVotes = poll.TotalVotes;
        return snapshot;
    }

    private MotdUpdateMessage BuildMessage(ICommonSession viewer, bool open, MotdSnapshot snapshot)
    {
        var message = new MotdUpdateMessage
        {
            Open = open,
            Text = snapshot.Text,
            Images = snapshot.Images,
        };

        if (snapshot.Question == null)
            return message;

        message.HasPoll = true;
        message.Question = snapshot.Question;
        message.Options = snapshot.Options;
        message.Votes = snapshot.Votes;
        message.TotalVotes = snapshot.TotalVotes;
        message.YourVote = _poll?.GetVote(viewer.UserId) ?? 0;

        return message;
    }

    private string BuildText()
    {
        var lines = new List<string>();
        if (!string.IsNullOrEmpty(_messageOfTheDay))
            lines.Add(_messageOfTheDay);

        if (_poll is { } poll)
            lines.Add(BuildPollText(poll));

        return string.Join("\n", lines);
    }

    private string BuildPollText(MotdPoll poll)
    {
        var lines = new List<string>
        {
            Loc.GetString("motd-poll-header", ("question", poll.Question)),
        };

        var totalVotes = poll.TotalVotes;
        var tallies = poll.GetTallies();
        for (var i = 0; i < poll.Options.Count; i++)
        {
            var option = i + 1;
            var votes = tallies[i];
            var percent = totalVotes == 0 ? 0 : (int) Math.Round(votes * 100.0 / totalVotes);

            lines.Add(Loc.GetString("motd-poll-option",
                ("number", option),
                ("option", poll.Options[i]),
                ("votes", votes),
                ("percent", percent)));
        }

        lines.Add(Loc.GetString("motd-poll-total", ("total", totalVotes)));
        lines.Add(Loc.GetString("motd-poll-hint"));

        return string.Join("\n", lines);
    }

    #region Event Handlers

    private void OnPlayerJoinedLobby(PlayerJoinedLobbyEvent ev)
    {
        ShowMotd(ev.PlayerSession);
    }

    /// Covers the ways in that never pass through the lobby, such as a server with the lobby disabled or a player spawned straight into a running round.
    private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent ev)
    {
        ShowMotd(ev.Player);
    }

    /// Opens the window once per connection, so joining the lobby and then spawning does not show it twice.
    private void ShowMotd(ICommonSession session)
    {
        if (!HasContent)
            return;

        PruneDisconnected();

        if (!_shownThisConnection.Add(session.UserId))
            return;

        SendMotd(session);
    }

    /// Forgets players who have since disconnected, so their next connection opens the window again.
    private void PruneDisconnected()
    {
        _shownThisConnection.RemoveWhere(userId => !_playerManager.TryGetSessionById(userId, out _));

        _staleVoters.Clear();
        foreach (var voter in _lastVote.Keys)
        {
            if (!_playerManager.TryGetSessionById(voter, out _))
                _staleVoters.Add(voter);
        }

        foreach (var stale in _staleVoters)
            _lastVote.Remove(stale);
    }

    private void OnMOTDChanged(string val)
    {
        if (val == _messageOfTheDay)
            return;

        _messageOfTheDay = val;
        BroadcastMotd(open: true);
    }

    private void OnImagesChanged(string val)
    {
        var images = new List<string>();
        foreach (var entry in val.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var path = NormalizeImagePath(entry);
            if (path == null)
                continue;

            images.Add(path);
            if (images.Count >= MaxImages)
                break;
        }

        if (images.SequenceEqual(_images))
            return;

        _images = images;
        BroadcastMotd(open: true);
    }

    private void OnVoteMessage(MotdVoteMessage message)
    {
        if (_poll is not { } poll)
            return;

        if (!_playerManager.TryGetSessionByChannel(message.MsgChannel, out var session))
            return;

        if (message.Option < 1 || message.Option > poll.Options.Count)
            return;

        // Voting for the option you already picked changes nothing, so it should not inform anyone.
        if (poll.GetVote(session.UserId) == message.Option)
            return;

        var now = _timing.CurTime;
        if (_lastVote.TryGetValue(session.UserId, out var last) && now - last < VoteCooldown)
            return;

        _lastVote[session.UserId] = now;

        Vote(session, message.Option);
        BroadcastMotd(open: false);
    }

    private void OnEditMessage(MotdEditMessage message)
    {
        if (!_playerManager.TryGetSessionByChannel(message.MsgChannel, out var session))
            return;

        // The window hides the editor from non-admins, but that is only a courtesy: the server decides.
        if (!_adminManager.HasAdminFlag(session, AdminFlags.Admin))
            return;

        switch (message.Action)
        {
            case MotdEditAction.RequestState:
                break;
            case MotdEditAction.Save:
                TryApplyEdits(session, message);
                break;
            case MotdEditAction.ClearPoll:
                if (_poll != null)
                {
                    ClearPoll();
                    _adminLogManager.Add(LogType.Chat, LogImpact.Low,
                        $"{session.Name:Player} cleared the MOTD poll from the MOTD window.");
                }

                break;
        }

        // Reply with what was actually stored, so the editor shows the result of its own edit.
        SendMotd(session);
    }

    /// Applies the message, pictures and poll sent by the MOTD window, rejecting anything invalid as a whole.
    private void TryApplyEdits(ICommonSession session, MotdEditMessage message)
    {
        var images = new List<string>();
        foreach (var entry in message.Images)
        {
            if (string.IsNullOrWhiteSpace(entry))
                continue;

            var path = NormalizeImagePath(entry);
            if (path == null || images.Count >= MaxImages)
                return;

            images.Add(path);
        }

        var question = message.Question.Trim();
        var options = message.Options.Select(option => option.Trim()).Where(option => option.Length > 0).ToArray();
        var setPoll = question.Length > 0;

        if (setPoll && (options.Length < 2 || options.Length > MotdPoll.MaxOptions))
            return;

        var text = message.Text.Trim();

        // Both cvar writes broadcast on their own; the poll change broadcasts the final result.
        _configurationManager.SetCVar(CCVars.MOTD, text);
        _configurationManager.SetCVar(CCVars.MOTDImages, string.Join(';', images));

        if (setPoll)
            SetPoll(question, options);
        else
            ClearPoll();

        _adminLogManager.Add(LogType.Chat, LogImpact.Low,
            $"{session.Name:Player} edited the MOTD in the MOTD window: \"{text}\", {images.Count} picture(s), poll: {(setPoll ? question : "none")}.");
    }

    #endregion Event Handlers

    /// Everything the window shows that is the same for every player, so a broadcast only has to work it out once.
    private sealed class MotdSnapshot
    {
        public string Text = string.Empty;
        public string[] Images = Array.Empty<string>();
        public string? Question;
        public string[] Options = Array.Empty<string>();
        public int[] Votes = Array.Empty<int>();
        public int TotalVotes;
    }

    /// The on-disk shape of a poll. Votes are keyed by the Guid behind a NetUserId, which the serializer can handle.
    private sealed class StoredMotdPoll
    {
        public string Question { get; set; } = string.Empty;
        public List<string> Options { get; set; } = new();
        public Dictionary<Guid, int> Votes { get; set; } = new();
    }
}

/// A single-choice poll displayed in the Message of the Day window. The question, its options and every vote are also stored on disk, so a poll outlives a server restart.
public sealed class MotdPoll
{
    public const int MaxOptions = 10;

    private readonly Dictionary<NetUserId, int> _votes = new();

    public MotdPoll(string question, IReadOnlyList<string> options, IReadOnlyDictionary<NetUserId, int>? votes = null)
    {
        Question = question;
        Options = options;

        if (votes == null)
            return;

        foreach (var (voter, option) in votes)
            _votes[voter] = option;
    }

    public string Question { get; }

    public IReadOnlyList<string> Options { get; }

    public IReadOnlyDictionary<NetUserId, int> Votes => _votes;

    public int TotalVotes => _votes.Count;

    /// Counts the votes for every option in a single pass, since the whole window is filled at once.
    public int[] GetTallies()
    {
        var tallies = new int[Options.Count];
        foreach (var option in _votes.Values)
        {
            if (option >= 1 && option <= tallies.Length)
                tallies[option - 1]++;
        }

        return tallies;
    }

    public int? GetVote(NetUserId voter)
    {
        return _votes.TryGetValue(voter, out var option) ? option : null;
    }

    public void RecordVote(NetUserId voter, int option)
    {
        _votes[voter] = option;
    }
}

/// The outcome of a player trying to vote in the Message of the Day poll.
public enum MotdVoteResult : byte
{
    /// There is no poll running.
    NoPoll,

    /// The given option does not exist in the poll.
    InvalidOption,

    /// The vote was recorded.
    Voted,

    /// The vote was recorded, replacing the option the player voted for previously.
    Changed,
}
