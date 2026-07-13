using Content.Shared.Inventory;
using Content.Shared.Popups;
using Content.Shared.Tag;
using Content.Shared.UserInterface;

namespace Content.Shared._Misfits.VaultDoorConsole;

public sealed class SharedVaultDoorConsoleSystem : EntitySystem
{
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly TagSystem _tag = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<VaultDoorConsoleGateComponent, ActivatableUIOpenAttemptEvent>(OnOpenAttempt);
    }

    private void OnOpenAttempt(Entity<VaultDoorConsoleGateComponent> ent, ref ActivatableUIOpenAttemptEvent args)
    {
        if (args.Cancelled)
            return;

        if (!HasPipBoy(args.User))
        {
            _popup.PopupClient("You need a Pip-Boy to interface with this terminal.", ent, args.User);
            args.Cancel();
            return;
        }

        if (!ent.Comp.BypassRaidRequirement && !ent.Comp.RaidActive)
        {
            _popup.PopupClient("SECURITY LOCKOUT: no active operation against this vault is on record.", ent, args.User);
            args.Cancel();
        }
    }

    private bool HasPipBoy(EntityUid user)
    {
        return _inventory.TryGetSlotEntity(user, "id", out var idUid) &&
               _tag.HasTag(idUid.Value, "MisfitsPipBoy");
    }
}
