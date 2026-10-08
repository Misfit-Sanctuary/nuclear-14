namespace Content.Shared.Construction.Steps;

[DataDefinition]
public sealed partial class EntitySimpleInsertConstructionGraphStep : ArbitraryInsertConstructionGraphStep
{
    [DataField("entity")]
    public string ProtoId = string.Empty;
    public override bool EntityValid(EntityUid uid, IEntityManager entityManager, IComponentFactory compFactory)
    {
        return entityManager.GetComponent<MetaDataComponent>(uid).EntityPrototype!.ID ==  ProtoId;
    }
}
