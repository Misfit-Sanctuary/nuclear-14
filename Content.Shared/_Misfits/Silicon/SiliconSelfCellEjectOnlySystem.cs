using Content.Shared.Containers.ItemSlots;
using Content.Shared.Popups;

namespace Content.Shared._Misfits.Silicon;

/// <summary>
/// [Changed by MisfitsCrew/Operator] Protects a silicon's installed power cell from external ejection without
/// blocking unrelated interactions such as welding repairs.
/// </summary>
public sealed class SiliconSelfCellEjectOnlySystem : EntitySystem
{
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SiliconSelfCellEjectOnlyComponent, ItemSlotEjectAttemptEvent>(OnSlotEjectAttempt);
    }

    private void OnSlotEjectAttempt(EntityUid uid, SiliconSelfCellEjectOnlyComponent component, ref ItemSlotEjectAttemptEvent args)
    {
        // [Changed by MisfitsCrew/Operator] Restrict only the configured cell slot and preserve self-service access.
        if (args.Slot.ID != component.SlotId || args.User == uid)
            return;

        args.Cancelled = true;

        if (args.User is { } user)
            _popup.PopupClient("Only this chassis can remove its own power cell.", uid, user);
    }
}
