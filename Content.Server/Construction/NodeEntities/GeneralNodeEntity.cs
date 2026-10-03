using Content.Shared.Construction;
using JetBrains.Annotations;

namespace Content.Server.Construction.NodeEntities;

/// <summary>
///     made for Misfits. Prevents code duplication when writing construct graphs
///     <see cref="GeneralConstructsComponent"/> defines the dictionary to look up in ent's yaml
///     GetId for just filling in entityUid, PerformAction spawns Amount derived from dictionary lookup via protoKey
///     see shields_broken_construction.yml for an example
/// </summary>
[UsedImplicitly]
[DataDefinition]
public sealed partial class GeneralNodeEntity : IGraphAction, IGraphNodeEntity
{
    [DataField(required: true)]
    public string ProtoKey = string.Empty;

    [DataField]
    public int Amount = 1;
    public string? GetId(EntityUid? uid, EntityUid? userUid, GraphNodeEntityArgs args)
    {
        if (args.EntityManager.Deleted(uid) ||
            !args.EntityManager.TryGetComponent<GeneralConstructsComponent>(uid, out var comp))
            return null;

        return comp.GeneralEnts[ProtoKey].Id;
    }

    public void PerformAction(EntityUid uid, EntityUid? userUid, IEntityManager entityManager)
    {
        if (entityManager.Deleted(uid) ||
       !entityManager.TryGetComponent<GeneralConstructsComponent>(uid, out var comp))
            return;

        var coordinates = entityManager.GetComponent<TransformComponent>(uid).Coordinates;
        for (int i = 0; i < Amount; i++)
        {
            entityManager.SpawnEntity(comp.GeneralEnts[ProtoKey], coordinates);
        }
    }
}
