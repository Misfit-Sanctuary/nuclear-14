// #Misfits Add - Client-side /raid request system. Registers the /raid console command,
// owns both the requester window and the admin embedded control, and translates server
// decision broadcasts into a screen popup so affected players can't miss the news.
// The actual chat-line for the decision is delivered server-side via DispatchServerMessage,
// so this system only needs to handle the popup + UI sync.

using Content.Client._Misfits.RaidRequest.UI;
using Content.Client.Popups;
using Content.Shared._Misfits.RaidRequest;
using Content.Shared.Popups;
using Robust.Client.Console;
using Robust.Shared.Console;

namespace Content.Client._Misfits.RaidRequest;

public sealed class RaidRequestClientSystem : EntitySystem
{
    [Dependency] private readonly IClientConsoleHost _conHost = default!;
    [Dependency] private readonly PopupSystem        _popup   = default!;

    private RaidRequestWindow? _window;

    // #Misfits Add - Pick the starting tab once fresh data arrives after /raid is opened.
    private bool _pickTabOnData;

    // #Misfits Add - Peer-approval popup. At most one is open at a time; if the server fires
    // a second prompt before the first is decided, we replace the contents in place.
    private RaidRequestPeerDecisionWindow? _peerWindow;

    // #Misfits Add - Mandatory raid-over notices are shown one at a time so simultaneous
    // conclusions cannot stack windows on top of each other.
    private RaidConcludedWindow? _concludedWindow;
    private readonly Queue<RaidRequestEntry> _conclusionQueue = new();

    /// <summary>Latest admin snapshot. Mirrored into <see cref="RaidRequestAdminControl"/> when present.</summary>
    public IReadOnlyList<RaidRequestEntry> AdminRequests => _adminRequests;
    private List<RaidRequestEntry> _adminRequests = new();

    /// <summary>Bound by <see cref="RaidRequestAdminControl"/> while the tab is open.</summary>
    public RaidRequestAdminControl? AdminControl;

    // #Misfits Add - Server-broadcast dict of every entity participating in an approved
    // faction-tier raid (NetEntity → canonical faction side ID). Read by AllyTagOverlay
    // alongside the war participants dict to draw [ALLY]/[ENEMY] tags during raids.
    // Empty when no faction-tier raid is approved.
    public IReadOnlyDictionary<NetEntity, string> RaidParticipants => _raidParticipants;
    private Dictionary<NetEntity, string> _raidParticipants = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<RaidRequestPanelDataMsg>(OnPanelData);
        SubscribeNetworkEvent<RaidRequestSubmitResultMsg>(OnSubmitResult);
        SubscribeNetworkEvent<RaidRequestJoinResultMsg>(OnJoinResult); // #Misfits Add
        SubscribeNetworkEvent<RaidRequestAdminListMsg>(OnAdminList);
        SubscribeNetworkEvent<RaidRequestAdminUpdateMsg>(OnAdminUpdate);
        SubscribeNetworkEvent<RaidRequestDecisionResultMsg>(OnDecisionResult);
        // #Misfits Add - End-raid result reuses the existing decision-result UI lane.
        SubscribeNetworkEvent<RaidRequestEndResultMsg>(OnEndResult);
        SubscribeNetworkEvent<RaidRequestDecisionAnnouncementMsg>(OnDecisionAnnouncement);
        SubscribeNetworkEvent<RaidRequestConcludedAnnouncementMsg>(OnConcludedAnnouncement);
        // #Misfits Add - Overlay participants stream (drives AllyTagOverlay).
        SubscribeNetworkEvent<RaidRequestParticipantsUpdatedMsg>(OnParticipantsUpdated);
        // #Misfits Add - Peer-approval popup for target faction or group leader.
        SubscribeNetworkEvent<RaidRequestPeerPromptMsg>(OnPeerPrompt);
        SubscribeNetworkEvent<RaidRequestPeerDecisionResultMsg>(OnPeerDecisionResult);

        _conHost.RegisterCommand(
            "raid",
            "Open the Raid Request panel.",
            "raid",
            OpenRaidPanel);

        // #Cythisiax Added - /raidjoin shortcut, mirrors /warjoin: opens /raid on the Raids tab
        _conHost.RegisterCommand(
            "raidjoin",
            "Open the Raid Request panel on the Raids tab to join an approved raid.",
            "raidjoin",
            OpenRaidJoinPanel);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _window?.Close();
        _window = null;
        _peerWindow?.Close();
        _peerWindow = null;
        _concludedWindow?.ForceClose();
        _concludedWindow = null;
        _conclusionQueue.Clear();
    }

    // ── /raid console command ─────────────────────────────────────────────

    private void OpenRaidPanel(IConsoleShell shell, string argStr, string[] args)
    {
        EnsureWindow();
        _window!.OpenCentered();
        _pickTabOnData = true;
        // Ask the server for fresh panel data — target resolution lives server-side.
        RaiseNetworkEvent(new RaidRequestOpenPanelMsg());
    }

    // #Cythisiax Added - /raidjoin: same window, straight to the Raids tab where joining lives
    private void OpenRaidJoinPanel(IConsoleShell shell, string argStr, string[] args)
    {
        EnsureWindow();
        _window!.OpenCentered();
        _pickTabOnData = false;
        _window.ShowTab(RaidRequestWindow.RaidsTab);
        RaiseNetworkEvent(new RaidRequestOpenPanelMsg());
    }

    private void EnsureWindow()
    {
        if (_window != null)
            return;

        _window = new RaidRequestWindow();
        _window.OnClose += () => _window = null;
        _window.OnRefresh += () => RaiseNetworkEvent(new RaidRequestOpenPanelMsg());
        // #Misfits Add - Volunteer for a side of an approved raid.
        _window.OnJoin += (requestId, side) =>
            RaiseNetworkEvent(new RaidRequestJoinMsg { RequestId = requestId, Side = side });

        _window.OnSubmit += (target, location, reason) =>
        {
            RaiseNetworkEvent(new RaidRequestSubmitMsg
            {
                TargetKind = target.Kind,
                TargetId   = target.Id,
                LocationNotes = location,
                Reason        = reason,
            });
        };
    }

    // ── Network handlers: requester ───────────────────────────────────────

    private void OnPanelData(RaidRequestPanelDataMsg msg)
    {
        if (_window == null)
            return;

        _window.UpdateState(msg);

        // Open on "Raids" when there is something to join or the player can't request one;
        // otherwise go straight to the request form.
        if (_pickTabOnData)
        {
            _pickTabOnData = false;
            _window.ShowTab(msg.OpenRaids.Count > 0 || !msg.CanSubmit
                ? RaidRequestWindow.RaidsTab
                : RaidRequestWindow.DeclareTab);
        }
    }

    private void OnSubmitResult(RaidRequestSubmitResultMsg msg)
    {
        _window?.ShowResult(msg.Success, msg.Message);
        if (msg.Success)
        {
            // #Misfits Tweak - clear the form and jump to "My requests" so the new entry is visible.
            _window?.ResetForm();
            _window?.ShowTab(RaidRequestWindow.MyRequestsTab);
            RaiseNetworkEvent(new RaidRequestOpenPanelMsg());
        }
    }

    // #Misfits Add - Join result: show it and refresh so the card shows the new side.
    private void OnJoinResult(RaidRequestJoinResultMsg msg)
    {
        _window?.ShowResult(msg.Success, msg.Message);
        if (msg.Success)
            RaiseNetworkEvent(new RaidRequestOpenPanelMsg());
    }

    // ── Network handlers: admin ───────────────────────────────────────────

    private void OnAdminList(RaidRequestAdminListMsg msg)
    {
        _adminRequests = msg.Requests;
        AdminControl?.UpdateList(_adminRequests);
    }

    private void OnAdminUpdate(RaidRequestAdminUpdateMsg msg)
    {
        // Replace or insert this single entry, keep newest-first ordering.
        var existing = _adminRequests.FindIndex(e => e.Id == msg.Entry.Id);
        if (existing >= 0)
            _adminRequests[existing] = msg.Entry;
        else
            _adminRequests.Insert(0, msg.Entry);

        AdminControl?.UpdateList(_adminRequests);
    }

    private void OnDecisionResult(RaidRequestDecisionResultMsg msg)
    {
        AdminControl?.ShowDecisionResult(msg.RequestId, msg.Success, msg.Message);
    }

    // #Misfits Add - End-raid result; reuses the same status label as approve/deny.
    private void OnEndResult(RaidRequestEndResultMsg msg)
    {
        AdminControl?.ShowDecisionResult(msg.RequestId, msg.Success, msg.Message);
    }

    /// <summary>Subscribed by <see cref="RaidRequestAdminControl"/> to send approve/deny.</summary>
    public void SendDecision(int requestId, bool approve, string comment)
    {
        RaiseNetworkEvent(new RaidRequestDecisionMsg
        {
            RequestId = requestId,
            Approve   = approve,
            Comment   = comment,
        });
    }

    // #Misfits Add - Admin pressed "End Raid" on an Approved request.
    public void SendEndRaid(int requestId)
    {
        RaiseNetworkEvent(new RaidRequestEndMsg { RequestId = requestId });
    }

    // ── Peer-approval popup ───────────────────────────────────

    private void OnPeerPrompt(RaidRequestPeerPromptMsg msg)
    {
        if (_peerWindow == null || _peerWindow.Disposed)
        {
            _peerWindow = new RaidRequestPeerDecisionWindow();
            _peerWindow.OnDecided += (approve, comment) =>
            {
                if (_peerWindow == null)
                    return;
                RaiseNetworkEvent(new RaidRequestPeerDecisionMsg
                {
                    RequestId = _peerWindow.RequestId,
                    Approve   = approve,
                    Comment   = comment ?? string.Empty,
                });
            };
            _peerWindow.OnClose += () => _peerWindow = null;
        }

        _peerWindow.Populate(msg.Entry);
        _peerWindow.OpenCentered();
    }

    private void OnPeerDecisionResult(RaidRequestPeerDecisionResultMsg msg)
    {
        if (_peerWindow == null || _peerWindow.Disposed || _peerWindow.RequestId != msg.RequestId)
            return;

        if (msg.Success)
            _peerWindow.Close();
        else
            _peerWindow.ShowFailure(msg.Message);
    }

    /// <summary>Subscribed by <see cref="RaidRequestAdminControl"/> on first open.</summary>
    public void SendAdminSubscribe() => RaiseNetworkEvent(new RaidRequestAdminSubscribeMsg());

    // ── Decision popup ────────────────────────────────────────────────────

    private void OnDecisionAnnouncement(RaidRequestDecisionAnnouncementMsg msg)
    {
        var entry = msg.Entry;
        var verb  = entry.Status == RaidRequestStatus.Approved ? "APPROVED" : "DENIED";
        var who   = entry.IsIndividual
            ? entry.RequesterCharacterName
            : RaidRequestConfig.FactionDisplayName(entry.RequesterFaction);
        var target = string.IsNullOrWhiteSpace(entry.TargetDisplayName)
            ? (entry.TargetKind == RaidRequestTargetKind.Faction
                ? RaidRequestConfig.FactionDisplayName(entry.TargetId)
                : $"Group {entry.TargetId}")
            : entry.TargetDisplayName;

        // Different framing depending on whether you're the raider side or being raided.
        var header = msg.IsTargetSide
            ? $"INCOMING RAID REQUEST {verb}"
            : $"RAID REQUEST {verb}";

        var text = $"{header}\n{who} → {target}\nSee chat for admin remarks.";

        // Cursor popup is the most reliable way to grab the player's attention without disrupting flow.
        var popupType = entry.Status == RaidRequestStatus.Approved
            ? PopupType.LargeCaution
            : PopupType.Large;
        _popup.PopupCursor(text, popupType);
    }

    // ── Overlay participants ──────────────────────────────────────────────

    // #Misfits Add - Replace the cached raid-participants dict whenever the server
    // re-broadcasts. After swapping, prod the war system to (re)evaluate overlay
    // lifecycle since the war system owns the AllyTagOverlay.
    private void OnConcludedAnnouncement(RaidRequestConcludedAnnouncementMsg msg)
    {
        _conclusionQueue.Enqueue(msg.Entry);
        ShowNextConclusion();
    }

    private void ShowNextConclusion()
    {
        if (_concludedWindow != null || _conclusionQueue.Count == 0)
            return;

        _concludedWindow = new RaidConcludedWindow();
        _concludedWindow.Populate(_conclusionQueue.Dequeue());
        _concludedWindow.OnAcknowledged += () =>
        {
            _concludedWindow = null;
            ShowNextConclusion();
        };
        _concludedWindow.OpenCentered();
    }

    private void OnParticipantsUpdated(RaidRequestParticipantsUpdatedMsg msg)
    {
        _raidParticipants = msg.Participants;

        if (EntityManager.TrySystem<Content.Client._Misfits.FactionWar.FactionWarClientSystem>(out var war))
            war.RefreshOverlay();
    }
}
