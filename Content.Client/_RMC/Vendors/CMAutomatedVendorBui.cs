using Content.Shared._RMC.Vendors;
using Robust.Client.UserInterface;

namespace Content.Client._RMC.Vendors;

public sealed class CMAutomatedVendorBui : BoundUserInterface
{
    private CMAutomatedVendorWindow? _window;

    public CMAutomatedVendorBui(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<CMAutomatedVendorWindow>();
        _window.OnVend += (section, entry) => SendMessage(new CMAutomatedVendorVendMessage(section, entry));
        _window.OnReplenishHeld += () => SendMessage(new CMAutomatedVendorReplenishMessage());
        _window.OnStoreHeld += () => SendMessage(new CMAutomatedVendorStoreHeldMessage());
        _window.OnWithdrawStored += item => SendMessage(new CMAutomatedVendorWithdrawStoredMessage(item));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        _window?.UpdateState((CMAutomatedVendorState) state);
    }
}
