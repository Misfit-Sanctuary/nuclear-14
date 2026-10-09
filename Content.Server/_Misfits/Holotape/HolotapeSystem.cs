using Content.Server.UserInterface;
using Content.Shared._Misfits.Holotape;
using Content.Shared._Misfits.Overwatch;
using Content.Shared.Dataset;
using Content.Shared.DeviceLinking;
using Content.Shared.GameTicking;
using Content.Shared.Interaction;
using Content.Shared.UserInterface;
using Content.Server.DeviceLinking.Systems;
using Robust.Server.GameObjects;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
// #Misfits Add - Server system for holotape reading and terminal random content.
// Handles: holotape insertion into readers, terminal click-to-read, MapInit content setup.
// #Misfits Add - Integrates with TerminalNotebookSystem to include notes in terminal state.

namespace Content.Server._Misfits.Holotape;

/// <summary>
/// Manages holotape playback and terminal content display.
/// Holotapes require a reader (terminal/Pip-Boy) to view their content.
/// Terminals display randomized pre-war/post-war entries when clicked directly.
/// </summary>
public sealed class HolotapeSystem : EntitySystem
{
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    // #Misfits Add - Notebook system dependency for notes integration on terminal UI open
    [Dependency] private readonly TerminalNotebookSystem _notebook = default!;
    [Dependency] private readonly TerminalNotesDataStore _notesData = default!;
    // #Misfits Add - DeviceLinkSystem for firing signals from the terminal LINKS tab
    [Dependency] private readonly DeviceLinkSystem _deviceLink = default!;

    // Tracks which terminal entry keys have been assigned this round.
    // Ensures no two terminals display the same content.
    private readonly HashSet<string> _usedEntries = new();

    public override void Initialize()
    {
        base.Initialize();

        // Clear used entry tracking on round restart
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);

        // Localize holotape/terminal content on map spawn
        SubscribeLocalEvent<HolotapeDataComponent, MapInitEvent>(OnHolotapeDataMapInit);

        // Pick random content for terminals on map spawn
        SubscribeLocalEvent<TerminalRandomContentComponent, MapInitEvent>(OnTerminalRandomMapInit);

        // When a holotape is used on a reader entity, open the viewer
        SubscribeLocalEvent<HolotapeReaderComponent, InteractUsingEvent>(OnReaderInteractUsing);

        // When a terminal with its own content is clicked, show that content
        SubscribeLocalEvent<HolotapeDataComponent, AfterActivatableUIOpenEvent>(OnTerminalUIOpen);

        // #Misfits Add - Handle link port invocation from the terminal LINKS tab
        SubscribeLocalEvent<HolotapeReaderComponent, InvokeTerminalLinkPortMessage>(OnInvokeLinkPort);
    }

    /// <summary>
    /// Resets used entry tracking so the next round gets a fresh pool.
    /// </summary>
    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        _usedEntries.Clear();
    }

    /// <summary>
    /// Resolves FTL locale keys to actual strings on map initialization.
    /// </summary>
    private void OnHolotapeDataMapInit(EntityUid uid, HolotapeDataComponent comp, MapInitEvent args)
    {
        if (comp.Localized)
            return;

        // Resolve title FTL key
        if (!string.IsNullOrEmpty(comp.Title))
            comp.Title = Loc.GetString(comp.Title);

        // Resolve content FTL key
        if (!string.IsNullOrEmpty(comp.Content))
            comp.Content = Loc.GetString(comp.Content);

        comp.Localized = true;
    }

    /// <summary>
    /// Picks a random entry from the configured dataset pool and writes it
    /// into the entity's HolotapeDataComponent before localization runs.
    /// Each dataset value is an FTL key prefix; "-title" and "-content" are appended.
    /// </summary>
    private void OnTerminalRandomMapInit(EntityUid uid, TerminalRandomContentComponent comp, MapInitEvent args)
    {
        if (!TryComp<HolotapeDataComponent>(uid, out var data))
            return;

        if (comp.ContentPool.Count == 0)
            return;

        // Pick a random dataset ID from the pool
        var datasetId = _random.Pick(comp.ContentPool);

        if (!_prototype.TryIndex<DatasetPrototype>(datasetId, out var dataset))
            return;

        if (dataset.Values.Count == 0)
            return;

        // Filter out entries already assigned to other terminals this round
        var available = new List<string>();
        foreach (var val in dataset.Values)
        {
            if (!_usedEntries.Contains(val))
                available.Add(val);
        }

        // If all entries are used (more terminals than entries), reset and allow repeats
        if (available.Count == 0)
            available.AddRange(dataset.Values);

        // Pick a unique random FTL key prefix from the available pool
        var entryKey = _random.Pick(available);
        _usedEntries.Add(entryKey);

        // Set title and content using the key prefix + suffixes
        data.Title = entryKey + "-title";
        data.Content = entryKey + "-content";

        // #Misfits Fix - Localize immediately here because OnHolotapeDataMapInit fires
        // BEFORE this handler (subscription order) and has already set Localized = true.
        data.Title = Loc.GetString(data.Title);
        data.Content = Loc.GetString(data.Content);
        data.Localized = true;
    }

    /// <summary>
    /// When a holotape is used on a reader (terminal or Pip-Boy),
    /// open the holotape's own terminal viewer UI and display its content.
    /// Uses the holotape entity's UserInterface, not the reader's.
    /// </summary>
    private void OnReaderInteractUsing(EntityUid uid, HolotapeReaderComponent comp, InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        // Check if the used item is a holotape with content
        if (!TryComp<HolotapeDataComponent>(args.Used, out var holotapeData))
            return;

        // Open the holotape's own UI (the holotape entity has UserInterface + HolotapeUiKey)
        if (!_ui.HasUi(args.Used, HolotapeUiKey.Key))
            return;

        _ui.OpenUi(args.Used, HolotapeUiKey.Key, args.User);
        // #Misfits Add - Pass isHolotapeItem: true so client shows HOLOTAPE header instead of TERMINAL
        _ui.SetUiState(args.Used, HolotapeUiKey.Key,
            new HolotapeBoundUserInterfaceState(holotapeData.Title, holotapeData.Content, isHolotapeItem: true));

        args.Handled = true;
    }

    /// <summary>
    /// When a terminal with its own HolotapeDataComponent is clicked and
    /// the ActivatableUI opens, send the terminal's own content to the viewer.
    /// If the terminal has a TerminalNotebookComponent, include persisted notes.
    /// If the terminal has a DeviceLinkSourceComponent, include available port IDs.
    /// If the terminal has a TerminalDatabaseComponent, include faction database state.
    /// </summary>
    private void OnTerminalUIOpen(EntityUid uid, HolotapeDataComponent comp, AfterActivatableUIOpenEvent args)
    {
        if (!_ui.HasUi(uid, HolotapeUiKey.Key))
            return;

        // #Misfits Add - Single state builder shared with RefreshTerminalState.
        var state = BuildTerminalState(uid, args.User, openDatabaseDocumentId: null);
        _ui.SetUiState(uid, HolotapeUiKey.Key, state);
        
        var target = uid; 
        var user = args.User;

        // Delay 1 tick before refreshing UI state to ensure contents load on first open.
        // remove when actual underlying problem is found
        Timer.Spawn(TimeSpan.Zero, () =>
            {
                if (Deleted(target))
                    return;

                if (!_ui.HasUi(target, HolotapeUiKey.Key))
                    return;

                var freshState = BuildTerminalState(target, user, openDatabaseDocumentId: null);
                _ui.SetUiState(target, HolotapeUiKey.Key, freshState);
            });
    }

    // #Misfits Add - Public re-entry point used by TerminalDatabaseSystem after every
    // database mutation. Keeps the state object's other tabs (data/notes/links) accurate
    // by going through the same builder used on initial UI open.
    /// <summary>
    /// Rebuilds and pushes the full HolotapeBoundUserInterfaceState for this terminal.
    /// Pass openDatabaseDocumentId to render a specific document in the DATABASE tab viewer.
    /// </summary>
    public void RefreshTerminalState(EntityUid uid, EntityUid actor, Guid? openDatabaseDocumentId = null)
    {
        if (!_ui.HasUi(uid, HolotapeUiKey.Key))
            return;
        var state = BuildTerminalState(uid, actor, openDatabaseDocumentId);
        _ui.SetUiState(uid, HolotapeUiKey.Key, state);
    }

    /// <summary>
    /// Assembles the full state object for a terminal: random/static title+content
    /// (DATA tab), notebook notes (NOTES tab), device link ports (LINKS tab),
    /// and faction database state (DATABASE tab).
    /// </summary>
    private HolotapeBoundUserInterfaceState BuildTerminalState(EntityUid uid, EntityUid actor, Guid? openDatabaseDocumentId)
    {
        // ── DATA tab content ───────────────────────────────────────────────
        var title = string.Empty;
        var content = string.Empty;
        if (TryComp<HolotapeDataComponent>(uid, out var data))
        {
            title = data.Title;
            content = data.Content;
        }

        // ── LINKS tab: gather device link source port data ─────────────────
        var hasLinkSource = false;
        List<string>? linkPorts = null;
        if (TryComp<DeviceLinkSourceComponent>(uid, out var linkSource))
        {
            var ports = linkSource.Ports;
            if (ports != null)
            {
                hasLinkSource = true;
                linkPorts = new List<string>();
                foreach (var port in ports)
                    linkPorts.Add(port.Id);
            }
        }

        // ── NOTES tab: include notebook notes + viewer ID ──────────────────
        List<TerminalNoteEntry>? notes = null;
        NetUserId? viewerId = null;
        if (TryComp<TerminalNotebookComponent>(uid, out var notebook)
            && !string.IsNullOrEmpty(notebook.TerminalId))
        {
            notes = _notesData.GetNotes(notebook.TerminalId);
            if (TryComp<ActorComponent>(actor, out var actorComp))
                viewerId = actorComp.PlayerSession.UserId;
        }

        // ── DATABASE tab: viewer-driven faction database resolution ────────
        // #Misfits Change - DATABASE is exposed only by explicit terminal gateways.
        // The viewer's ID still determines which faction database (if any) they see.
        TerminalDatabaseState? databaseState = null;
        if (HasComp<TerminalDatabaseAccessComponent>(uid))
        {
            var dbSystem = EntityManager.System<TerminalDatabaseSystem>();
            databaseState = dbSystem.BuildState(uid, actor, openDatabaseDocumentId);
        }
        var overwatchState = EntityManager.System<Content.Server._Misfits.Overwatch.OverwatchConsoleSystem>()
            .BuildUiState(uid);

        return new HolotapeBoundUserInterfaceState(
            title, content,
            notes, viewerId,
            isHolotapeItem: false,
            hasLinkSource: hasLinkSource,
            linkPorts: linkPorts,
            database: databaseState,
            overwatch: overwatchState);
    }

    // #Misfits Add - Validate port exists on the terminal's DeviceLinkSourceComponent and invoke it.
    // This allows terminals to fire device link signals (e.g. Open/Close door) from the UI.
    private void OnInvokeLinkPort(EntityUid uid, HolotapeReaderComponent comp, InvokeTerminalLinkPortMessage args)
    {
        if (!TryComp<DeviceLinkSourceComponent>(uid, out var linkSource))
            return;

        // Read Ports into a local to avoid RA0002 Execute access on the component field.
        var ports = linkSource.Ports;
        if (ports == null || !ports.Contains(args.PortId))
            return;

        _deviceLink.InvokePort(uid, args.PortId);
    }
}
