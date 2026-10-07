using System.Linq;
using Content.Server.Decals;
using Content.Shared.Chemistry.Components;
using Content.Shared.Coordinates.Helpers;
using Content.Shared.Decals;
using Content.Shared.Fluids;
using Content.Shared.FootPrint;
using Robust.Shared.Map;

namespace Content.Server.Fluids.EntitySystems;

public sealed partial class AbsorbentSystem
{
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly DecalSystem _decals = default!;

    /// <summary>
    ///     Removes cleanable grid decals in the same area that a mop cleans
    ///     footprint entities. This includes blood splatters and other
    ///     cleanable floor markings.
    /// </summary>
    private int TryCleanNearbyDecals(EntityCoordinates targetCoords, AbsorbentComponent component)
    {
        var gridUid = targetCoords.GetGridUid(EntityManager);
        if (gridUid == null)
            return 0;

        var decals = _decals.GetDecalsInRange(
            gridUid.Value,
            targetCoords.Position,
            component.FootprintCleaningRange,
            decal => decal.Cleanable);

        var cleaned = 0;
        foreach (var (id, _) in decals.Take(component.MaxCleanedFootprints))
        {
            if (_decals.RemoveDecal(gridUid.Value, id))
                cleaned++;
        }

        return cleaned;
    }

    /// <summary>
    ///     Tries to clean a number of footprints in a range determined by the component. Returns the number of cleaned footprints.
    /// </summary>
    private int TryCleanNearbyFootprints(EntityUid user, EntityUid target, Entity<AbsorbentComponent> used,  Entity<SolutionComponent> absorbentSoln)
    {
        var footprintQuery = GetEntityQuery<FootPrintComponent>();
        var targetCoords = Transform(target).Coordinates;
        var entities = _lookup.GetEntitiesInRange<FootPrintComponent>(targetCoords, used.Comp.FootprintCleaningRange, LookupFlags.Uncontained);

        // Take up to [MaxCleanedFootprints] footprints closest to the target
        var cleaned = entities.AsEnumerable()
            .Select(uid => (uid, dst: Transform(uid).Coordinates.TryDistance(EntityManager, _transform, targetCoords, out var dst) ? dst : 0f))
            .Where(ent => ent.dst > 0f)
            .OrderBy(ent => ent.dst)
            .Select(ent => (ent.uid, comp: footprintQuery.GetComponent(ent.uid)));

        // And try to interact with each one of them, ignoring useDelay
        var processed = 0;
        foreach (var (uid, footprintComp) in cleaned)
        {
            if (TryPuddleInteract(user, used.Owner, uid, used.Comp, useDelay: null, absorbentSoln))
                processed++;

            if (processed >= used.Comp.MaxCleanedFootprints)
                break;
        }

        return processed;
    }
}
