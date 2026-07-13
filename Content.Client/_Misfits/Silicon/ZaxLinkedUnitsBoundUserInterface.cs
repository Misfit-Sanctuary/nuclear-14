using Content.Shared._Misfits.Silicon;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._Misfits.Silicon;

[UsedImplicitly]
public sealed class ZaxLinkedUnitsBoundUserInterface : BoundUserInterface
{
    private ZaxLinkedUnitsWindow? _window;

    public ZaxLinkedUnitsBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<ZaxLinkedUnitsWindow>();
        _window.OnRefresh += () => SendMessage(new ZaxLinkedUnitsRefreshMessage());
        _window.OnWarp += target => SendMessage(new ZaxLinkedUnitsWarpMessage(target));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not ZaxLinkedUnitsBoundUserInterfaceState linkedUnitsState)
            return;

        _window?.SetUnits(linkedUnitsState.Units);
    }
}
