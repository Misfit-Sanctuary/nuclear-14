// #Misfits Change - Server-side mentor help system with Discord webhook integration
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Content.Server.Administration;
using Content.Server.Administration.Logs;
using Content.Server.Administration.Managers;
using Content.Server.Database; // #Misfits Add — IServerDbManager for MHELP chat persistence
using Content.Server.Afk;
using Content.Server.Chat.Managers; // #Misfits Add — for IChatManager (ticket admin chat push)
using Content.Server.Discord;
using Content.Server.GameTicking;
using Content.Server.Players.RateLimiting;
using Content.Shared._Misfits.Administration;
using Content.Shared.Administration;
using Content.Shared.CCVar;
using Content.Shared.Database;
using Content.Shared.GameTicking;
using Content.Shared.Mind;
using Content.Shared.Players.RateLimiting;
using JetBrains.Annotations;
using Robust.Server.Player;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._Misfits.Administration.Systems;

[UsedImplicitly]
public sealed partial class MentorHelpSystem : SharedMentorHelpSystem
{
    private const string RateLimitKey = "MentorHelp";

    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly IAdminManager _adminManager = default!;
    [Dependency] private readonly IConfigurationManager _config = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPlayerLocator _playerLocator = default!;
    [Dependency] private readonly GameTicker _gameTicker = default!;
    [Dependency] private readonly SharedMindSystem _minds = default!;
    [Dependency] private readonly IAfkManager _afkManager = default!;
    [Dependency] private readonly PlayerRateLimitManager _rateLimit = default!;
    [Dependency] private readonly IChatManager _chatManager = default!; // #Misfits Add — push ticket events to admin chat
    [Dependency] private readonly IAdminLogManager _adminLog = default!;
    [Dependency] private readonly IServerDbManager _dbManager = default!; // #Misfits Add — persist MHELP chat messages for audit replay

    [GeneratedRegex(@"^https://(?:(?:canary|ptb)\.)?discord\.com/api/webhooks/(\d+)/((?!.*/).*)$")]
    private static partial Regex DiscordRegex();

    private string _webhookUrl = string.Empty;
    private WebhookData? _webhookData;

    private ISawmill _sawmill = default!;
    private readonly HttpClient _httpClient = new();

    private string _footerIconUrl = string.Empty;
    private string _avatarUrl = string.Empty;
    private string _serverName = string.Empty;

    private readonly Dictionary<NetUserId, DiscordRelayInteraction> _relayMessages = new();
    private Dictionary<NetUserId, string> _oldMessageIds = new();
    private readonly Dictionary<NetUserId, Queue<DiscordRelayedData>> _messageQueues = new();
    private readonly HashSet<NetUserId> _processingChannels = new();
    private readonly Dictionary<NetUserId, (TimeSpan Timestamp, bool Typing)> _typingUpdateTimestamps = new();
    private readonly Dictionary<NetUserId, DateTime> _activeConversations = new();

    // Max embed description length is 4096, per Discord docs. Keep small margin.
    private const ushort DescriptionMax = 4000;
    private const ushort MessageLengthCap = 3000;
    private const string TooLongText = "... **(too long)**";

    private int _maxAdditionalChars;

    // #Misfits Add — MHelp ticket tracking (per-round, in-memory)
    private int _nextTicketId = 1;
    private readonly Dictionary<NetUserId, HelpTicketInfo> _mhelpTickets = new();
    // #Misfits Add — 1-minute cooldown per player after ticket resolution
    private readonly Dictionary<NetUserId, DateTime> _mhelpCooldowns = new();

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_config, CCVars.DiscordMHelpWebhook, OnWebhookChanged, true);
        Subs.CVar(_config, CCVars.DiscordMHelpFooterIcon, OnFooterIconChanged, true);
        Subs.CVar(_config, CCVars.DiscordMHelpAvatar, OnAvatarChanged, true);
        Subs.CVar(_config, CVars.GameHostName, OnServerNameChanged, true);

        _sawmill = IoCManager.Resolve<ILogManager>().GetSawmill("MHELP");

        var defaultParams = new MHelpMessageParams(
            string.Empty,
            string.Empty,
            true,
            _gameTicker.RoundDuration().ToString("hh\\:mm\\:ss"),
            _gameTicker.RunLevel,
            playedSound: false
        );
        _maxAdditionalChars = GenerateMHelpMessage(defaultParams).Message.Length;

        _playerManager.PlayerStatusChanged += OnPlayerStatusChanged;

        SubscribeNetworkEvent<MentorHelpClientTypingUpdated>(OnClientTypingUpdated);
        SubscribeLocalEvent<GameRunLevelChangedEvent>(OnGameRunLevelChanged);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ =>
        {
            _activeConversations.Clear();
            // #Misfits Add — reset mentor tickets on round restart
            _mhelpTickets.Clear();
            _nextTicketId = 1;
            _mhelpCooldowns.Clear();
        });

        // #Misfits Add — mentor ticket system handlers
        SubscribeNetworkEvent<HelpTicketClaimMessage>(OnTicketClaim);
        SubscribeNetworkEvent<HelpTicketResolveMessage>(OnTicketResolve);
        SubscribeNetworkEvent<HelpTicketUnclaimMessage>(OnTicketUnclaim); // #Misfits Add
        SubscribeNetworkEvent<HelpTicketReopenMessage>(OnTicketReopen); // #Misfits Add
        SubscribeNetworkEvent<HelpTicketRequestListMessage>(OnTicketRequestList);

        _rateLimit.Register(
            RateLimitKey,
            new RateLimitRegistration(
                CCVars.AhelpRateLimitPeriod,
                CCVars.AhelpRateLimitCount,
                PlayerRateLimitedAction)
        );
    }

    private void PlayerRateLimitedAction(ICommonSession obj)
    {
        RaiseNetworkEvent(
            new MentorHelpTextMessage(obj.UserId, default, "Rate limited, please wait before sending another message.", playSound: false),
            obj.Channel);
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs e)
    {
        // #Misfits Add — push existing mentor ticket list to newly connected mentors/admins
        // so they see tickets created before they joined.
        if (e.NewStatus == SessionStatus.InGame
            && (_adminManager.GetAdminData(e.Session)?.HasFlag(AdminFlags.ViewNotes) ?? false))
        {
            var list = _mhelpTickets.Values.ToList();
            if (list.Count > 0)
                RaiseNetworkEvent(new HelpTicketListMessage(list, HelpTicketType.MentorHelp), e.Session.Channel);
        }

        if (e.NewStatus != SessionStatus.Connected && e.NewStatus != SessionStatus.Disconnected)
            return;

        if (!_activeConversations.ContainsKey(e.Session.UserId))
            return;

        // #Misfits Add — auto-resolve open/claimed tickets when player disconnects
        if (e.NewStatus == SessionStatus.Disconnected
            && _mhelpTickets.TryGetValue(e.Session.UserId, out var dcTicket)
            && dcTicket.Status != HelpTicketStatus.Resolved)
        {
            dcTicket.Status = HelpTicketStatus.Resolved;
            dcTicket.ResolvedByName = "System";
            dcTicket.ResolvedById = null;
            dcTicket.ResolvedAt = DateTime.UtcNow;
            BroadcastMentorTicketUpdate(dcTicket);
            // #Misfits Tweak — removed bwoink chat announcement for auto-resolve on disconnect
            LogMentorTicketEvent(dcTicket, "auto-resolved on disconnect", "System", LogImpact.Medium);
        }

        var statusText = e.NewStatus switch
        {
            SessionStatus.Connected => "reconnected.",
            SessionStatus.Disconnected => "disconnected.",
            _ => null
        };

        if (statusText == null)
            return;

        var color = e.NewStatus == SessionStatus.Connected ? Color.Green.ToHex() : Color.Yellow.ToHex();
        var inGameMessage = $"[color={color}]{e.Session.Name} {statusText}[/color]";

        var msg = new MentorHelpTextMessage(
            userId: e.Session.UserId,
            trueSender: SystemUserId,
            text: inGameMessage,
            sentAt: DateTime.Now,
            playSound: false
        );

        var mentors = GetTargetMentors();
        foreach (var mentor in mentors)
        {
            RaiseNetworkEvent(msg, mentor);
        }

        // Relay connection status to Discord webhook
        if (_webhookUrl != string.Empty)
        {
            var icon = e.NewStatus == SessionStatus.Connected ? ":green_circle:" : ":red_circle:";
            var roundTime = _gameTicker.RoundDuration().ToString("hh\\:mm\\:ss");
            var messageParams = new MHelpMessageParams(
                e.Session.Name,
                FormattedMessage.EscapeText(statusText),
                true,
                roundTime,
                _gameTicker.RunLevel,
                playedSound: false,
                icon: icon
            );
            var queue = _messageQueues.GetOrNew(e.Session.UserId);
            queue.Enqueue(GenerateMHelpMessage(messageParams));
        }
    }

    private void OnClientTypingUpdated(MentorHelpClientTypingUpdated msg, EntitySessionEventArgs args)
    {
        if (_typingUpdateTimestamps.TryGetValue(args.SenderSession.UserId, out var tuple) &&
            tuple.Typing == msg.Typing &&
            tuple.Timestamp + TimeSpan.FromSeconds(1) > _timing.RealTime)
        {
            return;
        }

        _typingUpdateTimestamps[args.SenderSession.UserId] = (_timing.RealTime, msg.Typing);

        var isMentor = _adminManager.GetAdminData(args.SenderSession)?.HasFlag(AdminFlags.ViewNotes) ?? false; // #Misfits Change — ViewNotes grants MHelp access
        var channel = isMentor ? msg.Channel : args.SenderSession.UserId;
        var update = new MentorHelpPlayerTypingUpdated(channel, args.SenderSession.Name, msg.Typing);

        foreach (var mentor in GetTargetMentors())
        {
            if (mentor.UserId == args.SenderSession.UserId)
                continue;

            RaiseNetworkEvent(update, mentor);
        }
    }

    private void OnGameRunLevelChanged(GameRunLevelChangedEvent args)
    {
        if (args.Old is GameRunLevel.PreRoundLobby ||
            args.New is not (GameRunLevel.PreRoundLobby or GameRunLevel.InRound))
        {
            return;
        }

        _oldMessageIds = new Dictionary<NetUserId, string>();
        foreach (var (user, interaction) in _relayMessages)
        {
            var id = interaction.Id;
            if (id == null)
                return;
            _oldMessageIds[user] = id;
        }

        _relayMessages.Clear();
    }

    private async void OnWebhookChanged(string url)
    {
        _webhookUrl = url;

        if (url == string.Empty)
            return;

        var match = DiscordRegex().Match(url);

        if (!match.Success)
        {
            Log.Warning("MHelp webhook URL does not appear to be a standard Discord webhook. Attempting to use anyway...");
            _webhookData = await GetWebhookData(url);
            return;
        }

        if (match.Groups.Count <= 2)
        {
            Log.Error("Could not get webhook ID or token for MHelp webhook URL.");
            return;
        }

        _webhookData = await GetWebhookData(url);
    }

    private async Task<WebhookData?> GetWebhookData(string url)
    {
        var response = await _httpClient.GetAsync(url);
        var content = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            _sawmill.Log(LogLevel.Error,
                $"MHelp webhook returned bad status code when trying to get webhook data: {response.StatusCode}\nResponse: {content}");
            return null;
        }

        return JsonSerializer.Deserialize<WebhookData>(content);
    }

    // #Misfits Add — mentor ticket system: create or reopen a ticket when a player sends a message
    private void EnsureMentorTicket(NetUserId playerId, string playerName)
    {
        if (_mhelpTickets.TryGetValue(playerId, out var existing))
        {
            if (existing.Status == HelpTicketStatus.Resolved)
            {
                var newTicket = new HelpTicketInfo
                {
                    TicketId = _nextTicketId++,
                    PlayerId = playerId,
                    PlayerName = playerName,
                    Status = HelpTicketStatus.Open,
                    Type = HelpTicketType.MentorHelp,
                    CreatedAt = DateTime.Now,
                };
                _mhelpTickets[playerId] = newTicket;
                BroadcastMentorTicketUpdate(newTicket);
                SendMentorTicketSystemMessage(playerId, Loc.GetString("ticket-system-created", ("id", newTicket.TicketId), ("player", playerName), ("type", "MHELP")));
                LogMentorTicketEvent(newTicket, "created (from previously resolved conversation)", playerName, LogImpact.Low);
            }
            return;
        }

        var ticket = new HelpTicketInfo
        {
            TicketId = _nextTicketId++,
            PlayerId = playerId,
            PlayerName = playerName,
            Status = HelpTicketStatus.Open,
            Type = HelpTicketType.MentorHelp,
            CreatedAt = DateTime.Now,
        };
        _mhelpTickets[playerId] = ticket;
        BroadcastMentorTicketUpdate(ticket);
        SendMentorTicketSystemMessage(playerId, Loc.GetString("ticket-system-created", ("id", ticket.TicketId), ("player", playerName), ("type", "MHELP")));
        LogMentorTicketEvent(ticket, "created", playerName, LogImpact.Low);
    }

    private void BroadcastMentorTicketUpdate(HelpTicketInfo ticket)
    {
        var msg = new HelpTicketUpdatedMessage(ticket);
        foreach (var mentor in GetTargetMentors())
        {
            RaiseNetworkEvent(msg, mentor);
        }
    }

    private void SendMentorTicketSystemMessage(NetUserId playerId, string text)
    {
        // #Misfits Fix — Escape text for markup to prevent parse errors in client
        var safeText = Robust.Shared.Utility.FormattedMessage.EscapeText(text);
        var sysMsg = new MentorHelpTextMessage(
            userId: playerId,
            trueSender: SystemUserId,
            text: $"[color=cyan]{safeText}[/color]",
            sentAt: DateTime.Now,
            playSound: false
        );

        foreach (var mentor in GetTargetMentors())
        {
            RaiseNetworkEvent(sysMsg, mentor);
        }

        // #Misfits Add — push mentor ticket events into admin chat, filtered to mentors with ViewNotes flag only
        // (mentors should NOT see these but the flag is for consistency; actual recipients filtered via GetTargetMentors)
        _chatManager.SendAdminAnnouncement(text, flagWhitelist: AdminFlags.ViewNotes);
    }

    // #Misfits Add — send a system message only to the player (not visible in mentor panel)
    private void SendPlayerMentorSystemMessage(NetUserId playerId, string text)
    {
        if (!_playerManager.TryGetSessionById(playerId, out var session))
            return;
        var safeText = Robust.Shared.Utility.FormattedMessage.EscapeText(text);
        var msg = new MentorHelpTextMessage(
            userId: playerId,
            trueSender: SystemUserId,
            text: $"[color=yellow]{safeText}[/color]",
            sentAt: DateTime.Now,
            playSound: false
        );
        RaiseNetworkEvent(msg, session.Channel);
    }

    private void OnTicketClaim(HelpTicketClaimMessage msg, EntitySessionEventArgs args)
    {
        if (msg.Type != HelpTicketType.MentorHelp)
            return;

        if (!(_adminManager.GetAdminData(args.SenderSession)?.HasFlag(AdminFlags.ViewNotes) ?? false))
            return;

        var ticket = _mhelpTickets.Values.FirstOrDefault(t => t.TicketId == msg.TicketId);
        if (ticket == null || ticket.Status == HelpTicketStatus.Resolved)
            return;

        ticket.Status = HelpTicketStatus.Claimed;
        ticket.ClaimedByName = args.SenderSession.Name;
        ticket.ClaimedById = args.SenderSession.UserId;
        BroadcastMentorTicketUpdate(ticket);
        // #Misfits Tweak — removed bwoink chat announcement; panel card now shows claimer directly
        LogMentorTicketEvent(ticket, "claimed", args.SenderSession.Name, LogImpact.Low);
    }

    private void OnTicketResolve(HelpTicketResolveMessage msg, EntitySessionEventArgs args)
    {
        if (msg.Type != HelpTicketType.MentorHelp)
            return;

        if (!(_adminManager.GetAdminData(args.SenderSession)?.HasFlag(AdminFlags.ViewNotes) ?? false))
            return;

        var ticket = _mhelpTickets.Values.FirstOrDefault(t => t.TicketId == msg.TicketId);
        if (ticket == null || ticket.Status == HelpTicketStatus.Resolved)
            return;

        ticket.Status = HelpTicketStatus.Resolved;
        // #Misfits Add — track which admin resolved the ticket and when
        ticket.ResolvedByName = args.SenderSession.Name;
        ticket.ResolvedById = args.SenderSession.UserId;
        ticket.ResolvedAt = DateTime.UtcNow;
        BroadcastMentorTicketUpdate(ticket);
        // #Misfits Tweak — send resolve notice only to the player; set 1-minute cooldown
        _mhelpCooldowns[ticket.PlayerId] = DateTime.Now.AddMinutes(1);
        SendPlayerMentorSystemMessage(ticket.PlayerId, Loc.GetString("ticket-system-resolved-with-cooldown"));
        var age = (DateTime.UtcNow - ticket.CreatedAt).TotalMinutes;
        LogMentorTicketEvent(ticket, $"resolved ({age:F1}m since created)", args.SenderSession.Name, LogImpact.Medium);
    }

    // #Misfits Add — unclaim handler returns a claimed ticket to Open
    private void OnTicketUnclaim(HelpTicketUnclaimMessage msg, EntitySessionEventArgs args)
    {
        if (msg.Type != HelpTicketType.MentorHelp)
            return;

        if (!(_adminManager.GetAdminData(args.SenderSession)?.HasFlag(AdminFlags.ViewNotes) ?? false))
            return;

        var ticket = _mhelpTickets.Values.FirstOrDefault(t => t.TicketId == msg.TicketId);
        if (ticket == null || ticket.Status != HelpTicketStatus.Claimed)
            return;

        ticket.Status = HelpTicketStatus.Open;
        ticket.ClaimedByName = null;
        ticket.ClaimedById = null;
        BroadcastMentorTicketUpdate(ticket);
        // #Misfits Tweak — removed bwoink chat announcement; panel card reflects new status
        LogMentorTicketEvent(ticket, "unclaimed", args.SenderSession.Name, LogImpact.Low);
    }

    // #Misfits Add — reopen handler returns a resolved ticket to Open
    private void OnTicketReopen(HelpTicketReopenMessage msg, EntitySessionEventArgs args)
    {
        if (msg.Type != HelpTicketType.MentorHelp)
            return;

        if (!(_adminManager.GetAdminData(args.SenderSession)?.HasFlag(AdminFlags.ViewNotes) ?? false))
            return;

        var ticket = _mhelpTickets.Values.FirstOrDefault(t => t.TicketId == msg.TicketId);
        if (ticket == null || ticket.Status != HelpTicketStatus.Resolved)
            return;

        ticket.Status = HelpTicketStatus.Open;
        ticket.ClaimedByName = null;
        ticket.ClaimedById = null;
        ticket.ResolvedByName = null;
        ticket.ResolvedById = null;
        ticket.ResolvedAt = null;
        BroadcastMentorTicketUpdate(ticket);
        // #Misfits Tweak — removed bwoink chat announcement; panel card reflects new status
        // #Misfits Tweak — clear cooldown when mentor explicitly reopens the ticket
        _mhelpCooldowns.Remove(ticket.PlayerId);
        LogMentorTicketEvent(ticket, "reopened", args.SenderSession.Name, LogImpact.Medium);
    }

    // Persist mentor ticket lifecycle events for long-term staffing audits.
    private void LogMentorTicketEvent(HelpTicketInfo ticket, string action, string actorName, LogImpact impact)
    {
        _adminLog.Add(LogType.AdminMessage, impact,
            $"MHELP ticket #{ticket.TicketId} {action} | player={ticket.PlayerName} ({ticket.PlayerId}) | actor={actorName} | status={ticket.Status}");
    }

    // Persist mentor-help message traffic for complete historical review.
    // #Misfits Add — playerId is the NetUserId of the player who owns the ticket (not the sender).
    // Needed so DB messages can be retrieved by player GUID from the Audit Log.
    private void LogMentorTicketMessage(int? ticketId, NetUserId playerId, string senderName, bool senderIsMentor, string messageText)
    {
        var ticketText = ticketId.HasValue ? $"#{ticketId.Value}" : "<none>";
        var direction = senderIsMentor ? "mentor->player" : "player->mentor";
        _adminLog.Add(LogType.AdminMessage, LogImpact.Low,
            $"MHELP message ticket={ticketText} direction={direction} sender={senderName}: {messageText}");

        // #Misfits Fix — DB chat message persistence patched out; re-enable when feature is revisited.
        // if (ticketId.HasValue)
        // {
        //     var record = new HelpTicketMessage
        //     {
        //         TicketId = ticketId.Value,
        //         TicketType = (int) HelpTicketType.MentorHelp,
        //         PlayerId = playerId.UserId,
        //         SenderName = senderName,
        //         SenderIsStaff = senderIsMentor,
        //         MessageText = messageText,
        //         SentAt = DateTime.UtcNow,
        //     };
        //     var task = _dbManager.AddHelpTicketMessageAsync(record);
        //     task.ContinueWith(t =>
        //     {
        //         if (t.IsFaulted)
        //             Log.Error($"[HelpTicket] Failed to persist MHELP chat message for ticket #{ticketId}: {t.Exception}");
        //     }, TaskScheduler.Default);
        // }
    }

    private void OnTicketRequestList(HelpTicketRequestListMessage msg, EntitySessionEventArgs args)
    {
        if (msg.Type != HelpTicketType.MentorHelp)
            return;

        if (!(_adminManager.GetAdminData(args.SenderSession)?.HasFlag(AdminFlags.ViewNotes) ?? false))
            return;

        var list = _mhelpTickets.Values.ToList();
        RaiseNetworkEvent(new HelpTicketListMessage(list, HelpTicketType.MentorHelp), args.SenderSession.Channel);
    }

    private void OnFooterIconChanged(string url) => _footerIconUrl = url;
    private void OnAvatarChanged(string url) => _avatarUrl = url;
    private void OnServerNameChanged(string obj) => _serverName = obj;

    protected override void OnMentorHelpTextMessage(MentorHelpTextMessage message, EntitySessionEventArgs eventArgs)
    {
        base.OnMentorHelpTextMessage(message, eventArgs);

        var senderSession = eventArgs.SenderSession;

        var personalChannel = senderSession.UserId == message.UserId;
        var senderAdmin = _adminManager.GetAdminData(senderSession);
        var senderMentor = senderAdmin?.HasFlag(AdminFlags.ViewNotes) ?? false; // #Misfits Change — ViewNotes grants MHelp access
        var authorized = personalChannel || senderMentor;
        if (!authorized)
            return;

        if (_rateLimit.CountAction(eventArgs.SenderSession, RateLimitKey) != RateLimitStatus.Allowed)
            return;

        _activeConversations[message.UserId] = DateTime.Now;

        // #Misfits Add — create ticket when a non-mentor player sends a message
        if (!senderMentor)
        {
            // #Misfits Add — enforce 1-minute cooldown after ticket resolution; block silently on mentor side
            if (_mhelpCooldowns.TryGetValue(message.UserId, out var cooldownEnd) && DateTime.Now < cooldownEnd)
            {
                SendPlayerMentorSystemMessage(message.UserId, Loc.GetString("ticket-system-cooldown-blocked"));
                return; // Don't forward the message or create a ticket
            }
            EnsureMentorTicket(message.UserId, senderSession.Name);
        }

        // #Misfits Add — auto-claim ticket on first mentor reply
        if (senderMentor
            && _mhelpTickets.TryGetValue(message.UserId, out var openTicket)
            && openTicket.Status == HelpTicketStatus.Open)
        {
            openTicket.Status = HelpTicketStatus.Claimed;
            openTicket.ClaimedByName = senderSession.Name;
            openTicket.ClaimedById = senderSession.UserId;
            BroadcastMentorTicketUpdate(openTicket);
            // #Misfits Tweak — removed auto-claim bwoink announcement; panel card shows claimer
        }

        var escapedText = FormattedMessage.EscapeText(message.Text);
        var mentorColor = "#9B59B6"; // Purple for mentors
        var senderText = $"{senderSession.Name}";

        if (senderAdmin is not null && senderMentor)
        {
            senderText = $"[color={mentorColor}]{senderSession.Name}[/color]";
        }

        senderText = $"{(message.PlaySound ? "" : "(S) ")}{senderText}: {escapedText}";

        var playSound = senderAdmin == null || message.PlaySound;
        var msg = new MentorHelpTextMessage(message.UserId, senderSession.UserId, senderText, playSound: playSound);

        int? ticketId = _mhelpTickets.TryGetValue(message.UserId, out var historyTicket) ? historyTicket.TicketId : null;
        LogMentorTicketMessage(ticketId, message.UserId, senderSession.Name, senderMentor, message.Text);

        // Notify all mentors
        var mentors = GetTargetMentors();
        foreach (var channel in mentors)
        {
            RaiseNetworkEvent(msg, channel);
        }

        // Notify the player if they are not a mentor
        if (_playerManager.TryGetSessionById(message.UserId, out var session))
        {
            if (!mentors.Contains(session.Channel))
            {
                RaiseNetworkEvent(msg, session.Channel);
            }
        }

        // Relay to Discord webhook
        var sendsWebhook = _webhookUrl != string.Empty;
        if (sendsWebhook)
        {
            if (!_messageQueues.ContainsKey(msg.UserId))
                _messageQueues[msg.UserId] = new Queue<DiscordRelayedData>();

            var str = message.Text;
            var unameLength = senderSession.Name.Length;

            if (unameLength + str.Length + _maxAdditionalChars > DescriptionMax)
                str = str[..(DescriptionMax - _maxAdditionalChars - unameLength)];

            var nonAfkMentors = GetNonAfkMentors();
            var messageParams = new MHelpMessageParams(
                senderSession.Name,
                str,
                senderMentor,
                _gameTicker.RoundDuration().ToString("hh\\:mm\\:ss"),
                _gameTicker.RunLevel,
                playedSound: playSound,
                noReceivers: nonAfkMentors.Count == 0
            );
            _messageQueues[msg.UserId].Enqueue(GenerateMHelpMessage(messageParams));
        }

        if (mentors.Count != 0 || sendsWebhook)
            return;

        // No mentors online and no webhook — let the player know
        if (senderSession.Channel != null)
        {
            var systemText = "[color=red]No mentors are currently online to receive your message. Please try again later or use admin help (F2).[/color]";
            var noMentorsMsg = new MentorHelpTextMessage(message.UserId, SystemUserId, systemText);
            RaiseNetworkEvent(noMentorsMsg, senderSession.Channel);
        }
    }

    // #Misfits Add — periodic reminder interval for unclaimed mentor tickets
    private const float TicketReminderInterval = 60f; // seconds between reminders
    private float _ticketReminderTimer;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        foreach (var userId in _messageQueues.Keys.ToArray())
        {
            if (_processingChannels.Contains(userId))
                continue;

            var queue = _messageQueues[userId];
            _messageQueues.Remove(userId);
            if (queue.Count == 0)
                continue;

            _processingChannels.Add(userId);
            ProcessQueue(userId, queue);
        }

        // #Misfits Add — periodically remind mentors about unclaimed tickets
        _ticketReminderTimer += frameTime;
        if (_ticketReminderTimer >= TicketReminderInterval)
        {
            _ticketReminderTimer = 0f;
            var openCount = _mhelpTickets.Values.Count(t => t.Status == HelpTicketStatus.Open);
            if (openCount > 0)
            {
                // #Misfits Change — use MHELP-specific reminder so mentors/admins can distinguish from AHELP
                _chatManager.SendAdminAnnouncement(Loc.GetString("ticket-system-reminder-mhelp", ("count", openCount)));
            }
        }
    }

    private async void ProcessQueue(NetUserId userId, Queue<DiscordRelayedData> messages)
    {
        var exists = _relayMessages.TryGetValue(userId, out var existingEmbed);

        var tooLong = exists && messages.Sum(m => Math.Min(m.Message.Length, MessageLengthCap) + "\n".Length)
            + existingEmbed?.Description.Length > DescriptionMax;

        if (!exists || tooLong)
        {
            var lookup = await _playerLocator.LookupIdAsync(userId);

            if (lookup == null)
            {
                _sawmill.Log(LogLevel.Error,
                    $"Unable to find player for NetUserId {userId} when sending MHelp Discord webhook.");
                _relayMessages.Remove(userId);
                _processingChannels.Remove(userId);
                return;
            }

            var linkToPrevious = string.Empty;

            if (_webhookData is { GuildId: { } guildId, ChannelId: { } channelId })
            {
                if (tooLong && existingEmbed?.Id != null)
                {
                    linkToPrevious =
                        $"**[Go to previous embed of this round](https://discord.com/channels/{guildId}/{channelId}/{existingEmbed.Id})**\n";
                }
                else if (_oldMessageIds.TryGetValue(userId, out var id) && !string.IsNullOrEmpty(id))
                {
                    linkToPrevious =
                        $"**[Go to last round's conversation with this player](https://discord.com/channels/{guildId}/{channelId}/{id})**\n";
                }
            }

            var characterName = _minds.GetCharacterName(userId);
            existingEmbed = new DiscordRelayInteraction
            {
                Id = null,
                CharacterName = characterName,
                Description = linkToPrevious,
                Username = lookup.Username,
                LastRunLevel = _gameTicker.RunLevel,
            };

            _relayMessages[userId] = existingEmbed;
        }

        if (existingEmbed!.LastRunLevel != _gameTicker.RunLevel)
        {
            existingEmbed.Description += _gameTicker.RunLevel switch
            {
                GameRunLevel.PreRoundLobby => "\n\n:arrow_forward: _**Pre-round lobby started**_\n",
                GameRunLevel.InRound => "\n\n:arrow_forward: _**Round started**_\n",
                GameRunLevel.PostRound => "\n\n:stop_button: _**Post-round started**_\n",
                _ => throw new ArgumentOutOfRangeException(nameof(_gameTicker.RunLevel),
                    $"{_gameTicker.RunLevel} was not matched."),
            };

            existingEmbed.LastRunLevel = _gameTicker.RunLevel;
        }

        while (messages.TryDequeue(out var message))
        {
            string text;
            if (message.Message.Length > MessageLengthCap)
                text = message.Message[..(MessageLengthCap - TooLongText.Length)] + TooLongText;
            else
                text = message.Message;

            existingEmbed.Description += $"\n{text}";
        }

        var payload = GeneratePayload(existingEmbed.Description, existingEmbed.Username, userId.UserId, existingEmbed.CharacterName);

        if (existingEmbed.Id == null)
        {
            var request = await _httpClient.PostAsync($"{_webhookUrl}?wait=true",
                new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"));

            var content = await request.Content.ReadAsStringAsync();
            if (!request.IsSuccessStatusCode)
            {
                _sawmill.Log(LogLevel.Error,
                    $"Discord returned bad status code when posting MHelp message: {request.StatusCode}\nResponse: {content}");
                _relayMessages.Remove(userId);
                _processingChannels.Remove(userId);
                return;
            }

            var id = JsonNode.Parse(content)?["id"];
            if (id == null)
            {
                _sawmill.Log(LogLevel.Error,
                    $"Could not find id in json-content returned from Discord MHelp webhook: {content}");
                _relayMessages.Remove(userId);
                _processingChannels.Remove(userId);
                return;
            }

            existingEmbed.Id = id.ToString();
        }
        else
        {
            var request = await _httpClient.PatchAsync($"{_webhookUrl}/messages/{existingEmbed.Id}",
                new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"));

            if (!request.IsSuccessStatusCode)
            {
                var content = await request.Content.ReadAsStringAsync();
                _sawmill.Log(LogLevel.Error,
                    $"Discord returned bad status code when patching MHelp message: {request.StatusCode}\nResponse: {content}");
                _relayMessages.Remove(userId);
                _processingChannels.Remove(userId);
                return;
            }
        }

        _relayMessages[userId] = existingEmbed;
        _processingChannels.Remove(userId);
    }

    private WebhookPayload GeneratePayload(string messages, string username, Guid userId, string? characterName = null)
    {
        if (characterName != null)
            username += $" ({characterName})";

        // Purple color for mentor help to distinguish from ahelp (green)
        var color = GetNonAfkMentors().Count > 0 ? 0x9B59B6 : 0xFF0000;

        var serverName = _serverName[..Math.Min(_serverName.Length, 1500)];

        var round = _gameTicker.RunLevel switch
        {
            GameRunLevel.PreRoundLobby => _gameTicker.RoundId == 0
                ? "pre-round lobby after server restart"
                : $"pre-round lobby for round {_gameTicker.RoundId + 1}",
            GameRunLevel.InRound => $"round {_gameTicker.RoundId}",
            GameRunLevel.PostRound => $"post-round {_gameTicker.RoundId}",
            _ => throw new ArgumentOutOfRangeException(nameof(_gameTicker.RunLevel),
                $"{_gameTicker.RunLevel} was not matched."),
        };

        return new WebhookPayload
        {
            Username = username,
            UserID = userId,
            AvatarUrl = string.IsNullOrWhiteSpace(_avatarUrl) ? null : _avatarUrl,
            Embeds = new List<WebhookEmbed>
            {
                new()
                {
                    Description = messages,
                    Color = color,
                    Footer = new WebhookEmbedFooter
                    {
                        Text = $"{serverName} ({round})",
                        IconUrl = string.IsNullOrWhiteSpace(_footerIconUrl) ? null : _footerIconUrl,
                    },
                },
            },
        };
    }

    private static DiscordRelayedData GenerateMHelpMessage(MHelpMessageParams parameters)
    {
        var sb = new StringBuilder();

        if (parameters.Icon != null)
            sb.Append(parameters.Icon);
        else if (parameters.IsMentor)
            sb.Append(":outbox_tray:");
        else if (parameters.NoReceivers)
            sb.Append(":sos:");
        else
            sb.Append(":inbox_tray:");

        if (parameters.RoundTime != string.Empty && parameters.RoundState == GameRunLevel.InRound)
            sb.Append($" **{parameters.RoundTime}**");
        if (!parameters.PlayedSound)
            sb.Append(" **(S)**");

        if (parameters.Icon == null)
            sb.Append($" **{parameters.Username}:** ");
        else
            sb.Append($" **{parameters.Username}** ");

        sb.Append(parameters.Message);

        return new DiscordRelayedData
        {
            Receivers = !parameters.NoReceivers,
            Message = sb.ToString(),
        };
    }

    private IList<INetChannel> GetNonAfkMentors()
    {
        return _adminManager.ActiveAdmins
            .Where(p => (_adminManager.GetAdminData(p)?.HasFlag(AdminFlags.ViewNotes) ?? false) && // #Misfits Change — ViewNotes grants MHelp access
                        !_afkManager.IsAfk(p))
            .Select(p => p.Channel)
            .ToList();
    }

    private IList<INetChannel> GetTargetMentors()
    {
        return _adminManager.ActiveAdmins
            .Where(p => _adminManager.GetAdminData(p)?.HasFlag(AdminFlags.ViewNotes) ?? false) // #Misfits Change — ViewNotes grants MHelp access
            .Select(p => p.Channel)
            .ToList();
    }

    private record struct DiscordRelayedData
    {
        public bool Receivers;
        public string Message;
    }

    private sealed class DiscordRelayInteraction
    {
        public string? Id;
        public string Username = string.Empty;
        public string? CharacterName;
        public string Description = string.Empty;
        public GameRunLevel LastRunLevel;
    }
}

public sealed class MHelpMessageParams
{
    public string Username { get; set; }
    public string Message { get; set; }
    public bool IsMentor { get; set; }
    public string RoundTime { get; set; }
    public GameRunLevel RoundState { get; set; }
    public bool PlayedSound { get; set; }
    public bool NoReceivers { get; set; }
    public string? Icon { get; set; }

    public MHelpMessageParams(
        string username,
        string message,
        bool isMentor,
        string roundTime,
        GameRunLevel roundState,
        bool playedSound,
        bool noReceivers = false,
        string? icon = null)
    {
        Username = username;
        Message = message;
        IsMentor = isMentor;
        RoundTime = roundTime;
        RoundState = roundState;
        PlayedSound = playedSound;
        NoReceivers = noReceivers;
        Icon = icon;
    }
}
