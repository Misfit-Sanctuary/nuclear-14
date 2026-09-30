// #Misfits Add - BUI for the bunker tunnel door menu.
using Content.Shared._Misfits.Warps;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._Misfits.Warps;

[UsedImplicitly]
public sealed class BunkerTunnelDoorBoundUserInterface : BoundUserInterface
{
    private BunkerTunnelDoorWindow? _window;

    public BunkerTunnelDoorBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<BunkerTunnelDoorWindow>();
        _window.OnPreview += hatch => SendMessage(new BunkerTunnelDoorPreviewMessage(hatch));
        _window.OnGo += hatch => SendMessage(new BunkerTunnelDoorGoMessage(hatch));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is BunkerTunnelDoorUiState doorState)
            _window?.UpdateState(doorState);
    }
}
