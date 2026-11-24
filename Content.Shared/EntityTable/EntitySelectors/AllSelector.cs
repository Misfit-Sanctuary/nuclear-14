using Robust.Shared.Prototypes;
using Robust.Shared.Random;


namespace Content.Shared.EntityTable.EntitySelectors;

/// <summary>
/// Gets spawns from all of the child selectors
/// </summary>
public sealed partial class AllSelector : EntityTableSelector
{
    [DataField(required: true)]
    public List<EntityTableSelector> Children;

    protected override IEnumerable<EntProtoId> GetSpawnsImplementation(IRobustRandom rand,
        IEntityManager entMan,
        IPrototypeManager proto)
    {
        foreach (var child in Children)
        {
            foreach (var spawn in child.GetSpawns(rand, entMan, proto))
            {
                yield return spawn;
            }
        }
    }
}
