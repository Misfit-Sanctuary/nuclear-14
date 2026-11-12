using Content.Shared.SprayPainter;
using Content.Shared.SprayPainter.Components;
using Content.Shared.Decals;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;

namespace Content.Client.SprayPainter.UI;

public sealed class SprayPainterBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private SprayPainterWindow? _window;

    public SprayPainterBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<SprayPainterWindow>();

        _window.OnSpritePicked = OnSpritePicked;
        _window.OnColorPicked = OnColorPicked;
        _window.OnCustomColorChanged = OnCustomColorChanged;
        _window.OnDecalPicked = OnDecalPicked;
        _window.OnDecalColorChanged = OnDecalColorChanged;
        _window.OnDecalAngleChanged = OnDecalAngleChanged;
        _window.OnDecalSnapChanged = OnDecalSnapChanged;
        _window.OnDecalColorPickerToggled = OnDecalColorPickerToggled;
        _window.OnDecalModeChanged = OnDecalModeChanged;

        Update();
    }

    public override void Update()
    {
        if (_window == null || !EntMan.TryGetComponent(Owner, out SprayPainterComponent? comp))
            return;

        var system = EntMan.System<SprayPainterSystem>();
        _window.Populate(system.Entries, comp.Index, comp.PickedColor, comp.ColorPalette);
        _window.SetCustomPipeColor(comp.PickedCustomColor);
        _window.PopulateDecals(system.Decals);
        _window.SetSelectedDecal(comp.SelectedDecal);
        _window.SetDecalColor(comp.SelectedDecalColor);
        _window.SetDecalAngle(comp.SelectedDecalAngle);
        _window.SetDecalSnap(comp.SnapDecals);
        _window.SetDecalColorPicker(comp.ColorPickerEnabled);
        _window.SetDecalMode(comp.DecalMode);
    }

    private void OnSpritePicked(ItemList.ItemListSelectedEventArgs args)
    {
        SendMessage(new SprayPainterSpritePickedMessage(args.ItemIndex));
    }

    private void OnColorPicked(ItemList.ItemListSelectedEventArgs args)
    {
        var key = _window?.IndexToColorKey(args.ItemIndex);
        SendMessage(new SprayPainterColorPickedMessage(key));
    }

    private void OnCustomColorChanged(Color color) =>
        SendMessage(new SprayPainterSetCustomColorMessage(color));

    private void OnDecalPicked(ProtoId<DecalPrototype> decal) =>
        SendMessage(new SprayPainterSetDecalMessage(decal));

    private void OnDecalColorChanged(Color? color) =>
        SendMessage(new SprayPainterSetDecalColorMessage(color));

    private void OnDecalAngleChanged(int angle) =>
        SendMessage(new SprayPainterSetDecalAngleMessage(angle));

    private void OnDecalSnapChanged(bool snap) =>
        SendMessage(new SprayPainterSetDecalSnapMessage(snap));

    private void OnDecalColorPickerToggled(bool toggle) =>
        SendMessage(new SprayPainterSetDecalColorPickerMessage(toggle));

    private void OnDecalModeChanged(DecalPaintMode mode) =>
        SendMessage(new SprayPainterSetDecalModeMessage(mode));
}
