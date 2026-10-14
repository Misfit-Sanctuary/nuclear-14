namespace Content.Shared.Construction.Steps;

[DataDefinition]
public sealed partial class EntitySimpleInsertConstructionGraphStep : ArbitraryInsertConstructionGraphStep
{
    [DataField("entity", required: true)]
    public string ProtoId = string.Empty;
    public override bool EntityValid(EntityUid uid, IEntityManager entityManager, IComponentFactory compFactory)
    {
        // TODO: check if containers know if entity is queued for del. Maybe also optimize construct system enumator to not loop through literally everything over and over for each step
        return !entityManager.IsQueuedForDeletion(uid) && entityManager.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID == ProtoId; // everything passed here should have metaComp(things like players may not have entityProto)
    }
}
