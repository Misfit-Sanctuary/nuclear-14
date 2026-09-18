using Content.Shared.Containers.ItemSlots;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Inventory;
using Content.Shared.Popups;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared._Misfits.Weapons;

///     #Misfits Add - Manages the belt-fed .50 HMG ammo backpack.
///     The pack is loaded by another player clicking the WEARER with an ammo box or a loaded
///     belt, and the wearer can never load their own pack.
///     Runs shared so client prediction blocks the interaction immediately.
public sealed class N14HMGBackpackSystem : EntitySystem
{
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    [Dependency] private readonly ItemSlotsSystem _itemSlots = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedGunSystem _gun = default!;
    [Dependency] private readonly INetManager _net = default!;

    // Dropped HMGs waiting to snap back into their pack's gun cradle. Deferred until the next tick to avoid the dropped gun being re-inserted into the pack before the drop finishes processing.
    private readonly List<(EntityUid Gun, EntityUid Pack, EntityUid? User)> _snapBack = new();

    public override void Initialize()
    {
        base.Initialize();

        // Block the wearer from loading their own pack by interacting with the pack item directly.
        SubscribeLocalEvent<N14HMGBackpackComponent, InteractUsingEvent>(OnPackItemInteract, before: [typeof(SharedGunSystem)]);

        // Allow others to load the worn pack by using ammo on the player wearing it.
        SubscribeLocalEvent<InteractUsingEvent>(OnInteractWearer, before: [typeof(SharedGunSystem)]);

        // Snap the HMG back into the pack's cradle when it is dropped or thrown.
        SubscribeLocalEvent<N14HMGComponent, DroppedEvent>(OnHmgDropped);

        // Linked belts dump their entire contents into the pack in a single action.
        SubscribeLocalEvent<N14LinkedBeltComponent, N14BulkAmmoFillDoAfterEvent>(OnBulkAmmoFillDoAfter);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_net.IsServer || _snapBack.Count == 0)
            return;

        for (var i = _snapBack.Count - 1; i >= 0; i--)
        {
            var (gun, pack, user) = _snapBack[i];
            _snapBack.RemoveAt(i);

            if (Deleted(gun) || Deleted(pack) || !HasComp<N14HMGBackpackComponent>(pack))
                continue;

            // No longer lying on the floor (re-picked up or stowed somewhere): leave it be.
            if (_containers.IsEntityInContainer(gun))
                continue;

            if (!_itemSlots.TryGetSlot(pack, "gun_holder", out var slot) || slot.HasItem)
                continue;

            _itemSlots.TryInsert(pack, "gun_holder", gun, user);
        }
    }

    private void OnPackItemInteract(EntityUid uid, N14HMGBackpackComponent comp, InteractUsingEvent args)
    {
        // Only interactions that could feed ammo into the pack (ammo boxes, belts) are gated.
        if (!HasComp<BallisticAmmoProviderComponent>(args.Used))
            return;

        // The wearer can never load their own pack. Anyone else may load it while it is worn.
        if (!IsWornOnBack(uid, args.User))
            return;

        _popup.PopupClient(Loc.GetString(comp.CannotSelfLoadPopup), uid, args.User);
        args.Handled = true;
    }

    private void OnInteractWearer(InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        // Used must be a ballistic ammo giver (ammo box or loaded belt).
        if (!TryComp<BallisticAmmoProviderComponent>(args.Used, out var giverComp))
            return;

        // The target must be a person wearing an HMG ammo backpack on their back.
        if (!TryGetWornPack(args.Target, out var pack) ||
            !TryComp<BallisticAmmoProviderComponent>(pack, out var recieverComp))
            return;

        // The wearer can never load their own pack.
        if (args.Target == args.User)
        {
            _popup.PopupClient(Loc.GetString("hmg-backpack-cannot-self-load"), pack, args.User);
            args.Handled = true;
            return;
        }

        // Mirror the vanilla ballistic transfer guards so mismatched ammo gets a proper popup instead of being shoved into the feed belt.
        if (recieverComp.AmmoCount == recieverComp.Capacity)
        {
            _popup.PopupPredicted(
                Loc.GetString("gun-ballistic-transfer-target-full", ("entity", MetaData(pack).EntityName)),
                pack, args.User);
            args.Handled = true;
            return;
        }

        if (giverComp.AmmoCount == 0)
        {
            _popup.PopupPredicted(
                Loc.GetString("gun-ballistic-transfer-empty", ("entity", MetaData(args.Used).EntityName)),
                args.Used, args.User);
            args.Handled = true;
            return;
        }

        if (recieverComp.Whitelist?.Tags is null || giverComp.Whitelist?.Tags is null ||
            !recieverComp.Whitelist.Tags.Any(giverComp.Whitelist.Tags.Contains))
        {
            _popup.PopupPredicted(
                Loc.GetString("gun-ballistic-transfer-invalid",
                    ("ammoEntity", MetaData(args.Used).EntityName),
                    ("targetEntity", MetaData(pack).EntityName)),
                args.Used, args.User);
            args.Handled = true;
            return;
        }

        // Linked belts are shoved into the pack's feed in a single action.
        if (HasComp<N14LinkedBeltComponent>(args.Used))
        {
            args.Handled = _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, args.User,
                    giverComp.FillDelay, new N14BulkAmmoFillDoAfterEvent(), used: args.Used, target: pack, eventTarget: args.Used)
            {
                BreakOnMove = false,
                BreakOnDamage = false,
                NeedHand = true,
                BlockDuplicate = true,
                RequireCanInteract = true,
                BreakOnDropItem = true,
            });
            return;
        }

        // Ammo boxes keep the standard ballistic repeat-fill.
        args.Handled = _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, args.User,
                giverComp.FillDelay, new AmmoFillDoAfterEvent(), used: args.Used, target: pack, eventTarget: args.Used)
        {
            BreakOnMove = false,
            BreakOnDamage = false,
            NeedHand = true,
            BlockDuplicate = true,
            RequireCanInteract = true,
            BreakOnDropItem = true,
        });
    }

    private void OnBulkAmmoFillDoAfter(EntityUid uid, N14LinkedBeltComponent comp, N14BulkAmmoFillDoAfterEvent args)
    {
        if (args.Cancelled)
            return;

        if (!TryComp<BallisticAmmoProviderComponent>(uid, out var giverComp) ||
            args.Target is not { } target ||
            !TryComp<BallisticAmmoProviderComponent>(target, out var recieverComp))
            return;

        // Transfer as much of the belt as the pack can take, all at once.
        var toTake = Math.Min(giverComp.AmmoCount, recieverComp.Capacity - recieverComp.AmmoCount);
        if (toTake <= 0)
            return;

        var ammo = _gun.DoTakeAmmo(toTake, uid, args.User);
        if (ammo.Count == 0)
            return;

        _gun.DoAmmoInsert(ammo, recieverComp, target, args.User);
    }

    private void OnHmgDropped(EntityUid uid, N14HMGComponent comp, DroppedEvent args)
    {
        // Snap back into the cradle of a pack worn by whoever dropped the gun.
        if (!TryGetWornPack(args.User, out var pack))
            return;

        // Cradle already occupied: the drop stands.
        if (!_itemSlots.TryGetSlot(pack, "gun_holder", out var slot) || slot.HasItem)
            return;

        // Deferred to the next tick - see the _snapBack note above.
        _snapBack.Add((uid, pack, args.User));
    }

    private bool TryGetWornPack(EntityUid target, out EntityUid pack)
    {
        pack = EntityUid.Invalid;

        if (!_inventory.TryGetContainerSlotEnumerator(target, out var enumerator, SlotFlags.BACK))
            return false;

        while (enumerator.NextItem(out var item))
        {
            if (!HasComp<N14HMGBackpackComponent>(item))
                continue;

            pack = item;
            return true;
        }

        return false;
    }

    private bool IsWornOnBack(EntityUid backpack, EntityUid user)
    {
        if (!_containers.TryGetContainingContainer((backpack, null, null), out var container))
            return false;

        return container.Owner == user && (container.ID == "back" || container.ID == "back2");
    }
}

///     #Misfits Add - Marker for the belt-fed ammo backpack of the .50 HMG.
[RegisterComponent]
public sealed partial class N14HMGBackpackComponent : Component
{
    ///     Loc string shown when the wearer tries to load their own pack.
    [DataField]
    public LocId CannotSelfLoadPopup = "hmg-backpack-cannot-self-load";
}

///     #Misfits Add - Marker for the .50 HMG gun itself.
[RegisterComponent]
public sealed partial class N14HMGComponent : Component
{
}

///     #Misfits Add - Marker for the high-capacity linked belts that feed the .50 HMG.
///     When a belt is used on a wearer carrying the HMG backpack, it transfers its entire
///     contents in a single action via <see cref="N14BulkAmmoFillDoAfterEvent"/>,
///     rather than the vanilla 5-round-per-tick repeat fill used by ammo boxes.
[RegisterComponent]
public sealed partial class N14LinkedBeltComponent : Component
{
}

///     #Misfits Add - One-shot do-after for dumping a linked belt's entire contents into the HMG ammo backpack.
[Serializable, NetSerializable]
public sealed partial class N14BulkAmmoFillDoAfterEvent : SimpleDoAfterEvent
{
}
