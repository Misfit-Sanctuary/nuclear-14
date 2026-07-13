using Content.Shared._Misfits.VaultDoorConsole;

namespace Content.Client._Misfits.VaultDoorConsole;

public sealed class VaultDoorConsoleBoundUserInterface : BoundUserInterface
{
    private VaultDoorConsoleWindow? _window;

    public VaultDoorConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = new VaultDoorConsoleWindow(this);
        _window.OnClose += Close;
        _window.OpenCentered();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is VaultDoorConsoleBoundUserInterfaceState castState)
            _window?.UpdateState(castState);
    }

    public void SendToken(string token)
    {
        SendMessage(new VaultDoorConsoleGuessMessage(token));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
            return;

        _window?.Dispose();
    }
}
