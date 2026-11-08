// #Misfits Refactor - Client-side player war system.
// Receives player-war state syncs and routes panel requests/results.

using System.Linq;
using Content.Client._Misfits.FactionWar.UI;
using Content.Shared._Misfits.FactionWar;
using Content.Shared.Examine;
using Robust.Client.Console;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.ResourceManagement;
using Robust.Shared.Console;
using Robust.Shared.Timing;

namespace Content.Client._Misfits.FactionWar;

/// <summary>
/// Manages the <see cref="AllyTagOverlay"/> and the <see cref="FactionWarWindow"/> GUI.
/// The /war client command opens the war panel; /warjoin opens the same panel on its Wars tab,
/// where pending wars can be joined. // #Misfits Tweak - /warjoin merged into the /war panel.
/// All game-logic validation stays server-side.
/// </summary>
public sealed class FactionWarClientSystem : EntitySystem
{
    [Dependency] private readonly IOverlayManager     _overlayManager = default!;
    [Dependency] private readonly IPlayerManager      _playerManager  = default!;
    [Dependency] private readonly IEyeManager         _eyeManager     = default!;
    [Dependency] private readonly IResourceCache      _resourceCache  = default!;
    [Dependency] private readonly EntityLookupSystem  _entityLookup   = default!;
    [Dependency] private readonly ExamineSystemShared _examine        = default!;
    [Dependency] private readonly SharedTransformSystem _transform    = default!;
    [Dependency] private readonly IClientConsoleHost  _conHost        = default!;
    [Dependency] private readonly IGameTiming         _timing         = default!;

    public IReadOnlyList<PlayerWarEntry> ActiveWars => _activeWars;
    public byte? LocalWarJoinSide { get; private set; }
    public string? LocalWarKey { get; private set; }
    public IReadOnlyDictionary<NetEntity, FactionWarParticipantInfo> WarParticipants => _warParticipants;

    private List<PlayerWarEntry> _activeWars = new();
    private Dictionary<NetEntity, FactionWarParticipantInfo> _warParticipants = new();
    private AllyTagOverlay?    _overlay;
    private FactionWarWindow?  _window;
    private ForceWarWindow?    _forceWarWindow;
    private WarSideReviewWindow? _warSideReviewWindow;
    private WarInviteWindow? _warInviteWindow;
    private CeasefireProposalEvent? _pendingCeasefireProposal;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<FactionWarStateUpdatedEvent>(OnWarStateUpdated);
        SubscribeNetworkEvent<PlayerWarPanelDataEvent>(OnPanelData);
        SubscribeNetworkEvent<FactionWarCommandResultEvent>(OnCommandResult);
        SubscribeNetworkEvent<PlayerWarJoinPanelDataEvent>(OnJoinPanelData);
        SubscribeNetworkEvent<FactionWarJoinResultEvent>(OnJoinResult);
        SubscribeNetworkEvent<FactionWarParticipantsUpdatedEvent>(OnParticipantsUpdated);
        SubscribeNetworkEvent<FactionWarForceResultEvent>(OnForceWarResult);
        SubscribeNetworkEvent<CeasefireProposalEvent>(OnCeasefireProposal);
        SubscribeNetworkEvent<WarSideReviewPromptEvent>(OnSideReviewPrompt);
        SubscribeNetworkEvent<WarSideReviewResultEvent>(OnSideReviewResult);
        SubscribeNetworkEvent<WarInvitePromptEvent>(OnWarInvitePrompt);
        SubscribeNetworkEvent<WarInviteResultEvent>(OnWarInviteResult);
        SubscribeNetworkEvent<FactionWarForceObserveResultEvent>(OnForceObserveResult); // #Misfits Add

        _conHost.RegisterCommand(
            "war",
            Loc.GetString("faction-war-cmd-desc"),
            "war",
            OpenWarPanel);

        _conHost.RegisterCommand(
            "warjoin",
            Loc.GetString("faction-war-join-cmd-desc"),
            "warjoin",
            OpenWarJoinPanel);

        _conHost.RegisterCommand(
            "forcewar",
            "Open the admin Force War panel.",
            "forcewar",
            OpenForceWarPanel);

        _conHost.RegisterCommand(
            "surrender",
            "Surrender in an active war. You will be forced down, incapacitated, and marked as [SURRENDERED].",
            "surrender",
            OpenSurrender);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _window?.Close();
        _window = null;
        _forceWarWindow?.Close();
        _forceWarWindow = null;
        _warSideReviewWindow?.Close();
        _warSideReviewWindow = null;
        _warInviteWindow?.Close();
        _warInviteWindow = null;
        RemoveOverlay();
    }

    // ── Network event handlers ─────────────────────────────────────────────

    private void OnWarStateUpdated(FactionWarStateUpdatedEvent msg)
    {
        _activeWars = msg.ActiveWars;
        UpdateLocalWarContext();
        UpdateOverlayVisibility();

        // Refresh the war panel if open so the war list and join buttons follow state changes.
        if (_window != null)
            RequestPanelData();

        // Keep force-war ceasefire dropdown current.
        _forceWarWindow?.UpdateActiveWars(_activeWars);
    }

    private void OnPanelData(PlayerWarPanelDataEvent msg)
    {
        _activeWars = msg.ActiveWars;

        UpdateLocalWarContext();

        UpdateOverlayVisibility();

        _forceWarWindow?.UpdateOnlinePlayers(msg.OnlinePlayers);
        _forceWarWindow?.UpdateActiveWars(_activeWars);

        if (_window == null)
            return;

        var localEntity = _playerManager.LocalSession?.AttachedEntity is { } attached
            ? GetNetEntity(attached)
            : (NetEntity?) null;
        _window.UpdateState(msg, _playerManager.LocalSession?.UserId, localEntity, _pendingCeasefireProposal);
        // StatusMessage (round-start lock / cooldown) is shown on the Declare tab by UpdateState.
    }

    private void OnCommandResult(FactionWarCommandResultEvent msg)
    {
        _window?.ShowResult(msg.Success, msg.Message);
    }

    private void OnJoinPanelData(PlayerWarJoinPanelDataEvent msg)
    {
        if (_window == null)
            return;

        _window.UpdateJoinState(msg);

        UpdateLocalWarContext();

        UpdateOverlayVisibility();
    }

    private void OnJoinResult(FactionWarJoinResultEvent msg)
    {
        _window?.ShowResult(msg.Success, msg.Message);

        // If join succeeded, refresh panel data to update the UI state.
        if (msg.Success && _window != null)
            RequestPanelData();
    }

    private void OnParticipantsUpdated(FactionWarParticipantsUpdatedEvent msg)
    {
        _warParticipants = msg.Participants;
        UpdateLocalWarContext();
        UpdateOverlayVisibility();
    }

    private void OnCeasefireProposal(CeasefireProposalEvent msg)
    {
        _pendingCeasefireProposal = msg;
        EnsureWarWindow();
        _window!.OpenCentered();
        _window!.ShowTab(FactionWarWindow.WarsTab);
        RequestPanelData();
    }

    private void OnForceWarResult(FactionWarForceResultEvent msg)
    {
        // Route to the correct result label based on which action triggered this.
        if (msg.IsCeasefire)
            _forceWarWindow?.ShowCeasefireResult(msg.Success, msg.Message);
        else
            _forceWarWindow?.ShowResult(msg.Success, msg.Message);
    }

    private void OnSideReviewPrompt(WarSideReviewPromptEvent msg)
    {
        EnsureWarSideReviewWindow();
        _warSideReviewWindow!.UpdatePrompt(msg);
        _warSideReviewWindow.OpenCentered();
    }

    private void OnSideReviewResult(WarSideReviewResultEvent msg)
    {
        _warSideReviewWindow?.ShowResult(msg.Success, msg.Message);
    }

    private void OnWarInvitePrompt(WarInvitePromptEvent msg)
    {
        EnsureWarInviteWindow();
        _warInviteWindow!.UpdatePrompt(msg);
        _warInviteWindow.OpenCentered();
    }

    private void OnWarInviteResult(WarInviteResultEvent msg)
    {
        _warInviteWindow?.ShowResult(msg.Success, msg.Message);
    }

    // #Misfits Add - Handle observe result from server.
    private void OnForceObserveResult(FactionWarForceObserveResultEvent msg)
    {
        _forceWarWindow?.ShowObserveResult(msg.Success, msg.Message);
    }

    // ── /war client command ────────────────────────────────────────────────

    private void OpenWarPanel(IConsoleShell shell, string argStr, string[] args)
    {
        EnsureWarWindow();
        _window!.OpenCentered();

        // Ask server for fresh panel data (faction detection must happen server-side).
        RequestPanelData();
    }

    // ── /warjoin client command ────────────────────────────────────────────

    private void OpenWarJoinPanel(IConsoleShell shell, string argStr, string[] args)
    {
        // #Misfits Tweak - joining now lives on the /war panel's Wars tab.
        EnsureWarWindow();
        _window!.OpenCentered();
        _window!.ShowTab(FactionWarWindow.WarsTab);
        RequestPanelData();
    }

    /// <summary>The /war panel shows both the war list and join options, so ask for both.</summary>
    private void RequestPanelData()
    {
        RaiseNetworkEvent(new FactionWarOpenPanelRequestEvent());
        RaiseNetworkEvent(new FactionWarJoinPanelRequestEvent());
    }

    // ── /forcewar client command (admin) ────────────────────────────────────

    private void OpenForceWarPanel(IConsoleShell shell, string argStr, string[] args)
    {
        EnsureForceWarWindow();
        _forceWarWindow!.OpenCentered();

        // Populate the admin panel with the latest online players and active wars.
        RaiseNetworkEvent(new FactionWarOpenPanelRequestEvent());
    }

    // ── /surrender client command ──────────────────────────────────────────

    private void OpenSurrender(IConsoleShell shell, string argStr, string[] args)
    {
        RaiseNetworkEvent(new PlayerWarSurrenderRequestEvent());
    }

    // ── Window lifecycle ───────────────────────────────────────────────────

    private void EnsureWarWindow()
    {
        if (_window != null)
            return;

        _window = new FactionWarWindow();
        _window.OnClose += () => _window = null;

        _window.OnDeclareWar += (targetKind, targetId, reason, sideName1) =>
        {
            RaiseNetworkEvent(new PlayerWarDeclareRequestEvent
            {
                TargetKind = targetKind,
                TargetId = targetId,
                Reason = reason,
                SideName1 = sideName1,
            });
        };

        _window.OnCeasefire += otherPlayer =>
        {
            RaiseNetworkEvent(new PlayerWarCeasefireRequestEvent
            {
                OtherPlayer = otherPlayer,
            });
        };

        _window.OnAcceptCeasefireProposal += otherPlayer =>
        {
            // #Misfits Fix - answered, so stop showing the ceasefire banner.
            _pendingCeasefireProposal = null;
            RaiseNetworkEvent(new CeasefireAcceptedEvent
            {
                OtherPlayer = otherPlayer,
            });
        };

        _window.OnRejectCeasefireProposal += otherPlayer =>
        {
            // #Misfits Fix - answered, so stop showing the ceasefire banner.
            _pendingCeasefireProposal = null;
            RaiseNetworkEvent(new CeasefireRejectedEvent
            {
                OtherPlayer = otherPlayer,
            });
        };

        _window.OnRefresh += RequestPanelData;

        _window.OnJoinWar += (warKey, chosenSide) =>
        {
            RaiseNetworkEvent(new PlayerWarJoinRequestEvent
            {
                WarKey = warKey,
                ChosenSide = chosenSide,
            });
        };
    }

    private void EnsureForceWarWindow()
    {
        if (_forceWarWindow != null)
            return;

        _forceWarWindow = new ForceWarWindow();
        _forceWarWindow.OnClose += () => _forceWarWindow = null;

        _forceWarWindow.OnForceWar += (player1, side1, player2, side2, reason) =>
        {
            RaiseNetworkEvent(new PlayerWarForceRequestEvent
            {
                Player1 = player1,
                SideName1 = side1,
                Player2 = player2,
                SideName2 = side2,
                Reason = reason,
            });
        };

        _forceWarWindow.OnForceCeasefire += (player1, player2) =>
        {
            RaiseNetworkEvent(new PlayerWarForceCeasefireRequestEvent
            {
                Player1 = player1,
                Player2 = player2,
            });
        };

        // #Misfits Add - Admin observe war. Observer is always the admin using the panel.
        _forceWarWindow.OnForceObserve += participant =>
        {
            RaiseNetworkEvent(new PlayerWarForceObserveRequestEvent
            {
                Participant = participant,
            });
        };

        // Populate the ceasefire dropdown with current wars.
        _forceWarWindow.UpdateActiveWars(_activeWars);
    }

    private void EnsureWarSideReviewWindow()
    {
        if (_warSideReviewWindow != null)
            return;

        _warSideReviewWindow = new WarSideReviewWindow();
        _warSideReviewWindow.OnClose += () => _warSideReviewWindow = null;

        _warSideReviewWindow.OnSubmit += (warKey, removedParticipants) =>
        {
            RaiseNetworkEvent(new WarSideReviewSubmitEvent
            {
                WarKey = warKey,
                RemovedParticipants = removedParticipants.ToList(),
            });
        };
    }

    private void EnsureWarInviteWindow()
    {
        if (_warInviteWindow != null)
            return;

        _warInviteWindow = new WarInviteWindow();
        _warInviteWindow.OnClose += () => _warInviteWindow = null;

        _warInviteWindow.OnRespond += (warKey, accept) =>
        {
            RaiseNetworkEvent(new WarInviteResponseEvent
            {
                WarKey = warKey,
                Accept = accept,
            });
        };
    }

    // ── Overlay lifecycle ──────────────────────────────────────────────────

    private void UpdateOverlayVisibility()
    {
        if (_activeWars.Count == 0 || _warParticipants.Count == 0)
        {
            RemoveOverlay();
            return;
        }

        EnsureOverlay();
    }

    private void EnsureOverlay()
    {
        if (_overlay != null)
            return;

        _overlay = new AllyTagOverlay(
            this,
            EntityManager,
            _playerManager,
            _eyeManager,
            _timing,
            _resourceCache,
            _entityLookup,
            _examine,
            _transform);

        _overlayManager.AddOverlay(_overlay);
    }

    private void RemoveOverlay()
    {
        if (_overlay == null)
            return;

        _overlayManager.RemoveOverlay<AllyTagOverlay>();
        _overlay = null;
    }

    /// <summary>
    /// Trigger a refresh of the overlay lifecycle. Public so other client systems
    /// can prompt the war system to re-evaluate whether the overlay should be present.
    /// </summary>
    public void RefreshOverlay()
    {
        UpdateOverlayVisibility();
    }

    private void UpdateLocalWarContext()
    {
        LocalWarKey = null;
        LocalWarJoinSide = null;

        if (_playerManager.LocalSession?.AttachedEntity is not { } localEntity)
            return;

        var localNetEntity = GetNetEntity(localEntity);

        foreach (var war in _activeWars)
        {
            if (!war.Participants.TryGetValue(localNetEntity, out var side))
                continue;

            LocalWarKey = war.WarKey;
            LocalWarJoinSide = side;
            return;
        }
    }
}
