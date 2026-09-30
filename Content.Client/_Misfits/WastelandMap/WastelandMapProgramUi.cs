// #Misfits Change - Pip-Boy tactical map program UI
using Content.Client.UserInterface.Fragments;
using Content.Client._Misfits.Warps; // #Misfits Add - bunker hatch camera window
using Content.Shared.CartridgeLoader;
using Content.Shared._Misfits.WastelandMap;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.Client._Misfits.WastelandMap;

public sealed partial class WastelandMapProgramUi : UIFragment
{
    private WastelandMapProgramUiFragment? _fragment;
    private BunkerHatchFeedWindowHost? _hatchWindow; // #Misfits Add

    public override Control GetUIFragmentRoot()
    {
        return _fragment!;
    }

    public override void Setup(BoundUserInterface userInterface, EntityUid? fragmentOwner)
    {
        _fragment = new WastelandMapProgramUiFragment();
        _fragment.OnAddAnnotation += annotation => userInterface.SendMessage(new CartridgeUiMessage(new WastelandMapCartridgeAddAnnotationMessageEvent(annotation)));
        _fragment.OnRemoveAnnotation += index => userInterface.SendMessage(new CartridgeUiMessage(new WastelandMapCartridgeRemoveAnnotationMessageEvent(index)));
        _fragment.OnClearAnnotations += () => userInterface.SendMessage(new CartridgeUiMessage(new WastelandMapCartridgeClearAnnotationsMessageEvent()));
        // #Misfits Add - with an Enclave ID in, clicking a bunker hatch opens a view-only camera window.
        _hatchWindow = new BunkerHatchFeedWindowHost(
            hatch => userInterface.SendMessage(new CartridgeUiMessage(new WastelandMapCartridgeHatchViewMessageEvent(hatch))),
            sendLock: null);
        _fragment.OnBunkerHatchClicked += _hatchWindow.Open;
    }

    public override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is not WastelandMapBoundUserInterfaceState mapState)
            return;

        var bounds = new Box2(mapState.BoundsLeft, mapState.BoundsBottom, mapState.BoundsRight, mapState.BoundsTop);
        var texturePath = new ResPath(mapState.MapTexturePath);
        _fragment?.SetMap(mapState.MapTitle, texturePath, bounds, mapState.TrackedBlips, mapState.SharedAnnotations);
        _fragment?.SetBunkerHatches(mapState.BunkerHatches); // #Misfits Add
        _hatchWindow?.Refresh(mapState.BunkerHatches); // #Misfits Add
    }
}