// #Misfits Change - Client-side mentor help system
using System.Linq; // #Misfits Add — for LINQ ticket filtering
using Content.Client._Misfits.Administration.UI; // #Misfits Add — for TicketToastPopup
using Content.Client._Misfits.UserInterface.Systems.MentorHelp;
using Content.Shared._Misfits.Administration;
using JetBrains.Annotations;
using Robust.Client.Audio;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Client._Misfits.Administration.Systems;

[UsedImplicitly]
public sealed class MentorHelpSystem : SharedMentorHelpSystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly AudioSystem _audio = default!;

    public event EventHandler<MentorHelpTextMessage>? OnMentorHelpTextMessageReceived;
    private (TimeSpan Timestamp, bool Typing) _lastTypingUpdateSent;

    // #Misfits Add — ticket events for the UI
    public event Action<HelpTicketInfo>? OnTicketUpdated;
    public event Action<List<HelpTicketInfo>>? OnTicketListReceived;

    // #Misfits Add — track known tickets to only toast on new or significant state changes
    private readonly Dictionary<int, HelpTicketStatus> _knownTickets = new();

    // #Misfits Add — authoritative ticket cache, keyed by PlayerId. Populated by server
    // pushes and request responses. New UI subscribers (MentorHelpControl, etc.)
    // can read CachedTickets immediately instead of waiting for an async response.
    private readonly Dictionary<NetUserId, HelpTicketInfo> _cachedTickets = new();

    /// <summary>
    /// Returns the current cached ticket data. Safe to read at any time.
    /// </summary>
    public IReadOnlyDictionary<NetUserId, HelpTicketInfo> CachedTickets => _cachedTickets;

    public override void Initialize()
    {
        base.Initialize();

        // #Misfits Add — subscribe to ticket messages from server
        SubscribeNetworkEvent<HelpTicketUpdatedMessage>(OnTicketUpdatedMsg);
        SubscribeNetworkEvent<HelpTicketListMessage>(OnTicketListMsg);
    }

    protected override void OnMentorHelpTextMessage(MentorHelpTextMessage message, EntitySessionEventArgs eventArgs)
    {
        OnMentorHelpTextMessageReceived?.Invoke(this, message);
    }

    // #Misfits Add — relay ticket updates to the UI and show toast notifications
    private void OnTicketUpdatedMsg(HelpTicketUpdatedMessage msg)
    {
        if (msg.Ticket.Type == HelpTicketType.MentorHelp)
        {
            // #Misfits Add — update local cache before notifying UI
            _cachedTickets[msg.Ticket.PlayerId] = msg.Ticket;
            // #Misfits Fix — toast system disabled (same crash as BwoinkSystem — see comment there).
            // ShowTicketToast(msg.Ticket);
            OnTicketUpdated?.Invoke(msg.Ticket);
        }
    }

    private void OnTicketListMsg(HelpTicketListMessage msg)
    {
        // #Misfits Fix — ignore lists from the admin bwoink system; each list message is tagged
        // with the type that sent it so systems don't wipe each other's ticket caches.
        if (msg.ListType != HelpTicketType.MentorHelp)
            return;

        var mhelpTickets = msg.Tickets.Where(t => t.Type == HelpTicketType.MentorHelp).ToList();
        // #Misfits Fix — replace known ticket cache from authoritative server list.
        // This prevents old round ticket IDs from persisting client-side.
        _knownTickets.Clear();
        _cachedTickets.Clear();
        foreach (var t in mhelpTickets)
        {
            _knownTickets[t.TicketId] = t.Status;
            _cachedTickets[t.PlayerId] = t;
        }

        // #Misfits Fix — always notify listeners, including empty lists,
        // so UI caches can clear stale entries between rounds.
        OnTicketListReceived?.Invoke(mhelpTickets);
    }

    // #Misfits Add — show a toast popup for notable ticket events
    // #Misfits Fix — DISABLED. Same crash as BwoinkSystem — TicketToastPopup mutates the
    // UI control tree during DoFrameUpdateRecursive, causing "Collection was modified" crash.
    // Kept for future reimplementation using a safer notification pattern.
    // private void ShowTicketToast(HelpTicketInfo ticket)
    // {
    //     var previouslyKnown = _knownTickets.TryGetValue(ticket.TicketId, out var prevStatus);
    //     _knownTickets[ticket.TicketId] = ticket.Status;
    //
    //     string? title = null;
    //     string? body = null;
    //
    //     if (!previouslyKnown && ticket.Status == HelpTicketStatus.Open)
    //     {
    //         title = Loc.GetString("ticket-system-toast-new-title");
    //         body = Loc.GetString("ticket-system-toast-new-body", ("id", ticket.TicketId), ("player", ticket.PlayerName));
    //     }
    //     else if (previouslyKnown && prevStatus != ticket.Status)
    //     {
    //         switch (ticket.Status)
    //         {
    //             case HelpTicketStatus.Claimed:
    //                 title = Loc.GetString("ticket-system-toast-claimed-title");
    //                 body = Loc.GetString("ticket-system-toast-claimed-body", ("id", ticket.TicketId), ("role", "Mentor"), ("admin", ticket.ClaimedByName ?? "?"));
    //                 break;
    //             case HelpTicketStatus.Resolved:
    //                 title = Loc.GetString("ticket-system-toast-resolved-title");
    //                 body = Loc.GetString("ticket-system-toast-resolved-body", ("id", ticket.TicketId), ("role", "Mentor"), ("admin", ticket.ResolvedByName ?? "?"));
    //                 break;
    //             case HelpTicketStatus.Open when prevStatus == HelpTicketStatus.Resolved:
    //                 title = Loc.GetString("ticket-system-toast-reopened-title");
    //                 body = Loc.GetString("ticket-system-toast-reopened-body", ("id", ticket.TicketId), ("player", ticket.PlayerName));
    //                 break;
    //         }
    //     }
    //
    //     if (title != null && body != null)
    //     {
    //         var toast = new TicketToastPopup();
    //         toast.Show(title, body);
    //     }
    // }

    // #Misfits Add — send ticket claim/resolve requests
    public void ClaimTicket(int ticketId)
    {
        RaiseNetworkEvent(new HelpTicketClaimMessage(ticketId, HelpTicketType.MentorHelp));
    }

    public void ResolveTicket(int ticketId)
    {
        RaiseNetworkEvent(new HelpTicketResolveMessage(ticketId, HelpTicketType.MentorHelp));
    }

    // #Misfits Add — unclaim and reopen ticket requests
    public void UnclaimTicket(int ticketId)
    {
        RaiseNetworkEvent(new HelpTicketUnclaimMessage(ticketId, HelpTicketType.MentorHelp));
    }

    public void ReopenTicket(int ticketId)
    {
        RaiseNetworkEvent(new HelpTicketReopenMessage(ticketId, HelpTicketType.MentorHelp));
    }

    public void RequestTicketList()
    {
        RaiseNetworkEvent(new HelpTicketRequestListMessage(HelpTicketType.MentorHelp));
    }

    public void Send(NetUserId channelId, string text, bool playSound)
    {
        _audio.PlayGlobal(MentorHelpUIController.MHelpSendSound, Filter.Local(), false);
        RaiseNetworkEvent(new MentorHelpTextMessage(channelId, channelId, text, playSound: playSound));
        SendInputTextUpdated(channelId, false);
    }

    public void SendInputTextUpdated(NetUserId channel, bool typing)
    {
        if (_lastTypingUpdateSent.Typing == typing &&
            _lastTypingUpdateSent.Timestamp + TimeSpan.FromSeconds(1) > _timing.RealTime)
            return;

        _lastTypingUpdateSent = (_timing.RealTime, typing);
        RaiseNetworkEvent(new MentorHelpClientTypingUpdated(channel, typing));
    }
}
