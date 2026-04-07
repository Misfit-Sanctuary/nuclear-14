// #Misfits Add - Client-side BUI for the graffiti spray can.
// Opens the picker window and forwards decal selection back to the server.

using Content.Client._Misfits.GraffitiSprayCan.UI;
using Content.Shared._Misfits.GraffitiSprayCan;
using Content.Shared.Decals;
using Robust.Client.UserInterface;
using Robust.Shared.Prototypes;

namespace Content.Client._Misfits.GraffitiSprayCan;

public sealed class GraffitiSprayCanBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private GraffitiSprayCanWindow? _window;

    public GraffitiSprayCanBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey) { }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<GraffitiSprayCanWindow>();

        // Populate from prototypes (available client-side) and current selected state
        RefreshWindow();

        // Forward selection back to the server
        _window.OnDecalPicked = id => SendMessage(new GraffitiDecalSelectedMessage(id));
    }

    private void RefreshWindow()
    {
        if (_window == null)
            return;

        var protoMan = IoCManager.Resolve<IPrototypeManager>();
        var decals = protoMan.EnumeratePrototypes<DecalPrototype>();

        string? selectedId = null;
        int charges = 0;
        if (EntMan.TryGetComponent(Owner, out GraffitiSprayCanComponent? comp))
        {
            selectedId = comp.SelectedDecalId;
            charges = comp.Charges;
        }

        _window.Populate(decals, selectedId, charges);
    }
}
