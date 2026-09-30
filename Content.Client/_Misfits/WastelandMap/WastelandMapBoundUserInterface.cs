// #Misfits Change - Wasteland Map Viewer BUI
using Content.Client.Eye;
using Content.Client._Misfits.Overwatch;
using Content.Client._Misfits.Warps; // #Misfits Add - bunker hatch camera window
using Content.Shared._Misfits.Warps;
using Content.Shared._Misfits.Overwatch;
using Content.Shared._Misfits.WastelandMap;
using JetBrains.Annotations;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;
using Robust.Shared.Graphics;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.Client._Misfits.WastelandMap;

[UsedImplicitly]
public sealed class WastelandMapBoundUserInterface : BoundUserInterface
{
    private readonly EyeLerpingSystem _eyeLerpingSystem;
    private WastelandMapWindow? _window;
    private EntityUid? _currentOverwatchTarget;
    private BunkerHatchFeedWindowHost? _hatchWindow; // #Misfits Add

    public WastelandMapBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
        _eyeLerpingSystem = EntMan.System<EyeLerpingSystem>();
    }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<WastelandMapWindow>();
        _window.OnAddAnnotation += annotation => SendMessage(new WastelandMapAddAnnotationMessage(annotation));
        _window.OnRemoveAnnotation += index => SendMessage(new WastelandMapRemoveAnnotationMessage(index));
        _window.OnClearAnnotations += () => SendMessage(new WastelandMapClearAnnotationsMessage());
        _window.OnOverwatchAction += (type, targetNumber) =>
            SendMessage(new OverwatchConsoleMessage(type, targetNumber));
        _window.OnCommunicationsAction += (target, channelKind, revoke) =>
            SendMessage(new WastelandMapCommunicationsMessage(target, channelKind, revoke));
        // #Misfits Add - clicking a bunker hatch opens its camera window
        _hatchWindow = new BunkerHatchFeedWindowHost(
            hatch => SendMessage(new WastelandMapHatchViewMessage(hatch)),
            (hatch, lockIt) => SendMessage(new WastelandMapHatchLockMessage(hatch, lockIt)));
        _window.OnBunkerHatchClicked += _hatchWindow.Open;
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not WastelandMapBoundUserInterfaceState mapState)
            return;

        var bounds = new Box2(mapState.BoundsLeft, mapState.BoundsBottom, mapState.BoundsRight, mapState.BoundsTop);
        var texturePath = new ResPath(mapState.MapTexturePath);
        _window?.SetMap(mapState.MapTitle, texturePath, bounds, mapState.TrackedBlips, mapState.SharedAnnotations, mapState.CompactHud);
        var watch = EntMan.System<OverwatchConsoleSystem>().GetLocalWatch();
        _window?.UpdateOverwatch(mapState.Overwatch, watch, ResolveOverwatchEye(watch));
        _window?.UpdateCommunications(mapState.Communications);
        _window?.SetBunkerHatches(mapState.BunkerHatches); // #Misfits Add
        _hatchWindow?.Refresh(mapState.BunkerHatches); // #Misfits Add
    }

    private IEye? ResolveOverwatchEye(OverwatchWatchingComponent? watch)
    {
        var target = watch?.Watching;
        if (target == null)
        {
            ClearOverwatchTarget();
            return null;
        }

        if (_currentOverwatchTarget == null)
        {
            _eyeLerpingSystem.AddEye(target.Value);
            _currentOverwatchTarget = target;
        }
        else if (_currentOverwatchTarget != target)
        {
            _eyeLerpingSystem.RemoveEye(_currentOverwatchTarget.Value);
            _eyeLerpingSystem.AddEye(target.Value);
            _currentOverwatchTarget = target;
        }

        return EntMan.TryGetComponent<EyeComponent>(target, out var eye) ? eye.Eye : null;
    }

    private void ClearOverwatchTarget()
    {
        if (_currentOverwatchTarget == null)
            return;

        _eyeLerpingSystem.RemoveEye(_currentOverwatchTarget.Value);
        _currentOverwatchTarget = null;
    }

    protected override void Dispose(bool disposing)
    {
        ClearOverwatchTarget();
        _hatchWindow?.Close(); // #Misfits Add
        base.Dispose(disposing);
    }
}

