// #Misfits Add - Client-side faction war system.
// Receives war state syncs from the server and manages the AllyTagOverlay lifecycle.
// Registers the /war and /warjoin client console commands that open their respective GUIs.
// All faction detection is done server-side (NpcFactionMemberComponent.Factions is not
// synced to clients); the server sends pre-computed panel data via network events.
// Individual war participants (via /warjoin) are tracked and exposed for the overlay.

using System.Linq;
using Content.Client._Misfits.FactionWar.UI;
using Content.Shared._Misfits.FactionWar;
using Content.Shared.Examine;
using Robust.Client.Console;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.Console;
using Robust.Shared.Timing;

namespace Content.Client._Misfits.FactionWar;

/// <summary>
/// Manages the <see cref="AllyTagOverlay"/> and the <see cref="FactionWarWindow"/>/<see cref="WarJoinWindow"/> GUIs.
/// The /war client command opens the faction war panel; /warjoin opens the enlistment panel.
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
    [Dependency] private readonly SharedTransformSystem _transform   = default!;
    [Dependency] private readonly IClientConsoleHost  _conHost        = default!;
    [Dependency] private readonly IGameTiming         _timing         = default!;

    /// <summary>Current active wars. Read by <see cref="AllyTagOverlay"/> each frame.</summary>
    public IReadOnlyList<FactionWarEntry> ActiveWars => _activeWars;

    /// <summary>
    /// Local player's war-capable faction ID as determined by the server.
    /// Used by the overlay to avoid client-side IsMember which doesn't work
    /// (NpcFactionMemberComponent.Factions is not synced to clients).
    /// </summary>
    public string? LocalFactionId { get; private set; }

    /// <summary>
    /// If the local player enlisted via /warjoin, this is the faction side they joined.
    /// Used by the overlay when LocalFactionId is null (non-faction player).
    /// </summary>
    public string? LocalWarJoinSide { get; private set; }

    /// <summary>
    /// Individual war participants: NetEntity → faction side they are fighting for.
    /// Broadcast by the server. Used by the overlay to tag warjoin'd players.
    /// </summary>
    public IReadOnlyDictionary<NetEntity, string> WarParticipants => _warParticipants;

    private List<FactionWarEntry> _activeWars = new();
    private Dictionary<NetEntity, string> _warParticipants = new();
    private AllyTagOverlay?    _overlay;
    private FactionWarWindow?  _window;
    private WarJoinWindow?     _warJoinWindow;
    private ForceWarWindow?    _forceWarWindow;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<FactionWarStateUpdatedEvent>(OnWarStateUpdated);
        SubscribeNetworkEvent<FactionWarPanelDataEvent>(OnPanelData);
        SubscribeNetworkEvent<FactionWarCommandResultEvent>(OnCommandResult);
        SubscribeNetworkEvent<FactionWarJoinPanelDataEvent>(OnJoinPanelData);
        SubscribeNetworkEvent<FactionWarJoinResultEvent>(OnJoinResult);
        SubscribeNetworkEvent<FactionWarParticipantsUpdatedEvent>(OnParticipantsUpdated);
        SubscribeNetworkEvent<FactionWarForceResultEvent>(OnForceWarResult);

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
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _window?.Close();
        _window = null;
        _warJoinWindow?.Close();
        _warJoinWindow = null;
        _forceWarWindow?.Close();
        _forceWarWindow = null;
        RemoveOverlay();
    }

    // ── Network event handlers ─────────────────────────────────────────────

    private void OnWarStateUpdated(FactionWarStateUpdatedEvent msg)
    {
        _activeWars = msg.ActiveWars;
        UpdateOverlayVisibility();

        // Refresh war panel if open so Active Wars list repopulates after respawn/state change.
        if (_window != null)
            RaiseNetworkEvent(new FactionWarOpenPanelRequestEvent());

        // Refresh warjoin panel if open (pending wars may have changed phase).
        if (_warJoinWindow != null)
            RaiseNetworkEvent(new FactionWarJoinPanelRequestEvent());

        // Keep force-war ceasefire dropdown current.
        _forceWarWindow?.UpdateActiveWars(_activeWars);
    }

    private void OnPanelData(FactionWarPanelDataEvent msg)
    {
        // Cache faction ID for overlay use.
        LocalFactionId = msg.MyFactionId;
        _activeWars    = msg.ActiveWars;

        UpdateOverlayVisibility();

        if (_window == null)
            return;

        var eligibleTargets = msg.EligibleTargets
            .Select(t => (t.DisplayName, t.Id))
            .ToList();

        var ceasefireTargets = msg.CeasefireTargets
            .Select(t => (t.DisplayName, t.Id))
            .ToList();

        _window.UpdateState(
            msg.MyFactionId,
            msg.MyFactionDisplay,
            msg.ActiveWars,
            eligibleTargets,
            ceasefireTargets);

        if (msg.StatusMessage != null)
            _window.ShowResult(false, msg.StatusMessage);
    }

    private void OnCommandResult(FactionWarCommandResultEvent msg)
    {
        _window?.ShowResult(msg.Success, msg.Message);
    }

    private void OnJoinPanelData(FactionWarJoinPanelDataEvent msg)
    {
        if (_warJoinWindow == null)
            return;

        _warJoinWindow.UpdateState(
            msg.PendingWars,
            msg.AlreadyInFaction,
            msg.AlreadyJoinedSide,
            msg.StatusMessage,
            msg.IsTopRanking,
            msg.MyWarFactionId);

        // If the player just successfully joined, cache their side for the overlay.
        if (msg.AlreadyJoinedSide != null)
        {
            LocalWarJoinSide = msg.AlreadyJoinedSide;
            UpdateOverlayVisibility();
        }
    }

    private void OnJoinResult(FactionWarJoinResultEvent msg)
    {
        _warJoinWindow?.ShowResult(msg.Success, msg.Message);

        // If join succeeded, refresh panel data to update the UI state.
        if (msg.Success)
            RaiseNetworkEvent(new FactionWarJoinPanelRequestEvent());
    }

    private void OnParticipantsUpdated(FactionWarParticipantsUpdatedEvent msg)
    {
        _warParticipants = msg.Participants;
        UpdateOverlayVisibility();
    }

    private void OnForceWarResult(FactionWarForceResultEvent msg)
    {
        // Route to the correct result label based on which action triggered this.
        if (msg.IsCeasefire)
            _forceWarWindow?.ShowCeasefireResult(msg.Success, msg.Message);
        else
            _forceWarWindow?.ShowResult(msg.Success, msg.Message);
    }

    // ── /war client command ────────────────────────────────────────────────

    private void OpenWarPanel(IConsoleShell shell, string argStr, string[] args)
    {
        EnsureWarWindow();
        _window!.OpenCentered();

        // Ask server for fresh panel data (faction detection must happen server-side).
        RaiseNetworkEvent(new FactionWarOpenPanelRequestEvent());
    }

    // ── /warjoin client command ────────────────────────────────────────────

    private void OpenWarJoinPanel(IConsoleShell shell, string argStr, string[] args)
    {
        EnsureWarJoinWindow();
        _warJoinWindow!.OpenCentered();

        RaiseNetworkEvent(new FactionWarJoinPanelRequestEvent());
    }

    // ── /forcewar client command (admin) ────────────────────────────────────

    private void OpenForceWarPanel(IConsoleShell shell, string argStr, string[] args)
    {
        EnsureForceWarWindow();
        _forceWarWindow!.OpenCentered();
    }

    // ── Window lifecycle ───────────────────────────────────────────────────

    private void EnsureWarWindow()
    {
        if (_window != null)
            return;

        _window = new FactionWarWindow();
        _window.OnClose += () => _window = null;

        _window.OnDeclareWar += (targetId, casusBelli) =>
        {
            RaiseNetworkEvent(new FactionWarDeclareRequestEvent
            {
                TargetFaction = targetId,
                CasusBelli    = casusBelli,
            });
        };

        _window.OnCeasefire += targetId =>
        {
            RaiseNetworkEvent(new FactionWarCeasefireRequestEvent
            {
                TargetFaction = targetId,
            });
        };
    }

    private void EnsureWarJoinWindow()
    {
        if (_warJoinWindow != null)
            return;

        _warJoinWindow = new WarJoinWindow();
        _warJoinWindow.OnClose += () => _warJoinWindow = null;

        _warJoinWindow.OnJoinWar += (aggressor, target, chosenSide, factionWide) =>
        {
            RaiseNetworkEvent(new FactionWarJoinRequestEvent
            {
                AggressorFaction = aggressor,
                TargetFaction    = target,
                ChosenSide       = chosenSide,
                FactionWide      = factionWide,
            });
        };
    }

    private void EnsureForceWarWindow()
    {
        if (_forceWarWindow != null)
            return;

        _forceWarWindow = new ForceWarWindow();
        _forceWarWindow.OnClose += () => _forceWarWindow = null;

        _forceWarWindow.OnForceWar += (aggressor, target, casus) =>
        {
            RaiseNetworkEvent(new FactionWarForceRequestEvent
            {
                AggressorFaction = aggressor,
                TargetFaction    = target,
                CasusBelli       = casus,
            });
        };

        _forceWarWindow.OnForceCeasefire += (aggressor, target) =>
        {
            RaiseNetworkEvent(new FactionWarForceCeasefireRequestEvent
            {
                AggressorFaction = aggressor,
                TargetFaction    = target,
            });
        };

        // Populate the ceasefire dropdown with current wars.
        _forceWarWindow.UpdateActiveWars(_activeWars);
    }

    // ── Overlay lifecycle ──────────────────────────────────────────────────

    private void UpdateOverlayVisibility()
    {
        if (_activeWars.Count == 0)
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
}
