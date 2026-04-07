using Content.Shared._Misfits.Weapons;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Containers;
// #Misfits Fix - Use concrete HitscanBatteryAmmoProviderComponent; abstract BatteryAmmoProviderComponent
// is not resolvable via TryComp in Robust ECS and caused NullReferenceException during entity spawn preview.

// #Misfits Add - Handles fire cost multiplier for guns with GunDamageBonusComponent.
// When a cell is inserted into a gun that has a FireCostMultiplier, the cell's
// FireCost is scaled up so fewer shots are available. Restored on ejection.
// Bonus damage is applied separately in GunSystem.cs (server-side hitscan hit path).

namespace Content.Shared._Misfits.Weapons;

public sealed class GunDamageBonusSystem : EntitySystem
{
    private const string MagazineSlot = "gun_magazine";

    public override void Initialize()
    {
        base.Initialize();

        // Listen for cell insertion/ejection on guns that have the bonus component
        SubscribeLocalEvent<GunDamageBonusComponent, EntInsertedIntoContainerMessage>(OnMagInserted);
        SubscribeLocalEvent<GunDamageBonusComponent, EntRemovedFromContainerMessage>(OnMagRemoved);
        SubscribeLocalEvent<GunDamageBonusComponent, ComponentStartup>(OnStartup);
    }

    /// <summary>
    /// On map init / startup, apply multiplier to any already-inserted cell.
    /// </summary>
    private void OnStartup(EntityUid uid, GunDamageBonusComponent comp, ComponentStartup args)
    {
        if (comp.FireCostMultiplier == 1.0f)
            return;

        // Check if there's already a cell in the magazine slot
        if (!TryComp<ContainerManagerComponent>(uid, out var containers))
            return;

        if (!containers.Containers.TryGetValue(MagazineSlot, out var container))
            return;

        foreach (var ent in container.ContainedEntities)
        {
            ApplyFireCostMultiplier(uid, ent, comp);
            break; // Only one cell per slot
        }
    }

    private void OnMagInserted(EntityUid uid, GunDamageBonusComponent comp, EntInsertedIntoContainerMessage args)
    {
        if (args.Container.ID != MagazineSlot)
            return;

        if (comp.FireCostMultiplier == 1.0f)
            return;

        ApplyFireCostMultiplier(uid, args.Entity, comp);
    }

    private void OnMagRemoved(EntityUid uid, GunDamageBonusComponent comp, EntRemovedFromContainerMessage args)
    {
        if (args.Container.ID != MagazineSlot)
            return;

        RestoreFireCost(args.Entity, comp);
    }

    /// <summary>
    /// Multiply the cell's FireCost and store the original value for restoration.
    /// </summary>
    private void ApplyFireCostMultiplier(EntityUid gunUid, EntityUid cellUid, GunDamageBonusComponent comp)
    {
        if (!TryComp<HitscanBatteryAmmoProviderComponent>(cellUid, out var battery))
            return;

        // Store original cost before modifying
        comp.OriginalFireCost = battery.FireCost;
        battery.FireCost *= comp.FireCostMultiplier;
        Dirty(cellUid, battery);
    }

    /// <summary>
    /// Restore the cell's original FireCost when ejected from the gun.
    /// </summary>
    private void RestoreFireCost(EntityUid cellUid, GunDamageBonusComponent comp)
    {
        if (comp.OriginalFireCost == null)
            return;

        if (!TryComp<HitscanBatteryAmmoProviderComponent>(cellUid, out var battery))
            return;

        battery.FireCost = comp.OriginalFireCost.Value;
        comp.OriginalFireCost = null;
        Dirty(cellUid, battery);
    }
}
