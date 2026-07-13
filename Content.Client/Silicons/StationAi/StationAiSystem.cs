using Content.Client.UserInterface.Systems.Actions;
using Content.Shared._Misfits.Silicon;
using Content.Shared.Silicons.StationAi;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Shared.Player;

namespace Content.Client.Silicons.StationAi;

public sealed partial class StationAiSystem : SharedStationAiSystem
{
    [Dependency] private readonly IOverlayManager _overlayMgr = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IUserInterfaceManager _ui = default!;

    private StationAiOverlay? _overlay;
    // [Changed by MisfitsCrew/Operator] Keep command feedback separate so it can follow shunted local-player control.
    private StationAiNpcCommandOverlay? _commandOverlay;

    public override void Initialize()
    {
        base.Initialize();
        InitializeAirlock();
        InitializePowerToggle();

        SubscribeLocalEvent<StationAiOverlayComponent, LocalPlayerAttachedEvent>(OnAiAttached);
        SubscribeLocalEvent<StationAiOverlayComponent, LocalPlayerDetachedEvent>(OnAiDetached);
        SubscribeLocalEvent<StationAiOverlayComponent, ComponentInit>(OnAiOverlayInit);
        SubscribeLocalEvent<StationAiOverlayComponent, ComponentRemove>(OnAiOverlayRemove);
        // [Changed by MisfitsCrew/Operator] Bind command feedback to the commander component so it follows shunted control.
        SubscribeLocalEvent<StationAiNpcCommanderComponent, LocalPlayerAttachedEvent>(OnCommanderAttached);
        SubscribeLocalEvent<StationAiNpcCommanderComponent, LocalPlayerDetachedEvent>(OnCommanderDetached);
        SubscribeLocalEvent<StationAiNpcCommanderComponent, ComponentInit>(OnCommanderInit);
        SubscribeLocalEvent<StationAiNpcCommanderComponent, ComponentRemove>(OnCommanderRemove);
        SubscribeNetworkEvent<StationAiNpcMoveTargetingFinishedEvent>(OnMoveTargetingFinished);
    }

    private void OnMoveTargetingFinished(StationAiNpcMoveTargetingFinishedEvent ev)
    {
        _ui.GetUIController<ActionUIController>()
            .StopTargetingIfEvent<StationAiMoveSelectedNpcsActionEvent>();
    }

    private void OnAiOverlayInit(Entity<StationAiOverlayComponent> ent, ref ComponentInit args)
    {
        var attachedEnt = _player.LocalEntity;

        if (attachedEnt != ent.Owner)
            return;

        AddOverlay();
    }

    private void OnAiOverlayRemove(Entity<StationAiOverlayComponent> ent, ref ComponentRemove args)
    {
        var attachedEnt = _player.LocalEntity;

        if (attachedEnt != ent.Owner)
            return;

        RemoveOverlay();
    }

    private void AddOverlay()
    {
        if (_overlay != null)
            return;

        _overlay = new StationAiOverlay();
        _overlayMgr.AddOverlay(_overlay);
    }

    private void RemoveOverlay()
    {
        if (_overlay == null)
            return;

        _overlayMgr.RemoveOverlay(_overlay);
        _overlay = null;
    }

    private void OnAiAttached(Entity<StationAiOverlayComponent> ent, ref LocalPlayerAttachedEvent args)
    {
        AddOverlay();
    }

    private void OnAiDetached(Entity<StationAiOverlayComponent> ent, ref LocalPlayerDetachedEvent args)
    {
        RemoveOverlay();
    }

    // [Changed by MisfitsCrew/Operator] Section: command-overlay lifecycle follows local control between core and shunted body.
    private void OnCommanderAttached(Entity<StationAiNpcCommanderComponent> ent, ref LocalPlayerAttachedEvent args)
    {
        AddCommandOverlay();
    }

    private void OnCommanderDetached(Entity<StationAiNpcCommanderComponent> ent, ref LocalPlayerDetachedEvent args)
    {
        RemoveCommandOverlay();
    }

    private void OnCommanderInit(Entity<StationAiNpcCommanderComponent> ent, ref ComponentInit args)
    {
        if (_player.LocalEntity == ent.Owner)
            AddCommandOverlay();
    }

    private void OnCommanderRemove(Entity<StationAiNpcCommanderComponent> ent, ref ComponentRemove args)
    {
        if (_player.LocalEntity == ent.Owner)
            RemoveCommandOverlay();
    }

    private void AddCommandOverlay()
    {
        if (_commandOverlay != null)
            return;

        _commandOverlay = new StationAiNpcCommandOverlay();
        _overlayMgr.AddOverlay(_commandOverlay);
    }

    private void RemoveCommandOverlay()
    {
        if (_commandOverlay == null)
            return;

        _overlayMgr.RemoveOverlay(_commandOverlay);
        _commandOverlay = null;
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlayMgr.RemoveOverlay<StationAiOverlay>();
        // [Changed by MisfitsCrew/Operator] Ensure command feedback cannot survive client system shutdown.
        _overlayMgr.RemoveOverlay<StationAiNpcCommandOverlay>();
    }
}
