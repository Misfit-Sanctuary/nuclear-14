// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Containers.ItemSlots;
using Content.Shared.Examine;
using Robust.Shared.Prototypes;
using Content.Shared._Misfits.Genetics.Mutations;

namespace Content.Shared._Misfits.Genetics.Console;

public sealed partial class GeneticsDiskSystem : EntitySystem
{
    [Dependency] private ItemSlotsSystem _slots = default!;

    [Dependency] private EntityQuery<GeneticsDiskComponent> _query = default!;
    [Dependency] private EntityQuery<GeneticsDiskSlotComponent> _slotQuery = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GeneticsDiskComponent, ExaminedEvent>(OnExamined);
    }

    // Misfits Add - a disk's contents were only readable inside a genetics console,
    // so a courier holding several had no way to tell them apart. Examining one now
    // names the stored mutation and its rarity.
    private void OnExamined(Entity<GeneticsDiskComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        if (ent.Comp.Enzymes is { } enzymes)
        {
            args.PushMarkup(Loc.GetString("genetics-disk-examine-enzymes", ("name", enzymes.Name)));
            return;
        }

        if (ent.Comp.Mutation is not { } mutationId ||
            !_prototypes.TryIndex<EntityPrototype>(mutationId, out var mutationProto) ||
            !mutationProto.TryGetComponent<MutationComponent>(out var mutation))
        {
            args.PushMarkup(Loc.GetString("genetics-disk-examine-blank"));
            return;
        }

        // Copy to a local first - MutationComponent.Rarity is read-only to other types,
        // and calling ToString() directly on the member counts as an Execute access.
        var rarity = mutation.Rarity;

        args.PushMarkup(Loc.GetString("genetics-disk-examine-mutation",
            ("mutation", mutationProto.Name),
            ("rarity", rarity.ToString())));
    }

    /// <summary>
    /// Changes the mutation stored on a disk.
    /// </summary>
    public void SetMutation(Entity<GeneticsDiskComponent> ent, EntProtoId<MutationComponent>? id)
    {
        if (ent.Comp.Mutation == id)
            return;

        ent.Comp.Mutation = id;
        DirtyField(ent, ent.Comp, nameof(GeneticsDiskComponent.Mutation));

        if (id != null) // mutually exclusive
            SetEnzymes(ent, null);
    }

    /// <summary>
    /// Changes the unique enzymes stored on a disk.
    /// </summary>
    public void SetEnzymes(Entity<GeneticsDiskComponent> ent, UniqueEnzymes? enzymes)
    {
        if (ent.Comp.Enzymes?.Name == enzymes?.Name)
            return;

        ent.Comp.Enzymes = enzymes;
        DirtyField(ent, ent.Comp, nameof(GeneticsDiskComponent.Enzymes));

        if (enzymes != null) // mutually exclusive
            SetMutation(ent, null);
    }

    /// <summary>
    /// Gets the disk in a computer's disk slot, or null if it has no disk.
    /// </summary>
    public Entity<GeneticsDiskComponent>? GetDisk(Entity<GeneticsDiskSlotComponent?> ent)
    {
        if (_slotQuery.Resolve(ent, ref ent.Comp) &&
            _slots.GetItemOrNull(ent.Owner, ent.Comp.DiskSlot) is {} item &&
            _query.TryComp(item, out var disk))
            return (item, disk);

        return null;
    }
}
