using Content.Shared.Random.Helpers;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;


namespace Content.Shared.EntityTable.EntitySelectors;

/// <summary>
/// Gets the spawns from one of the child selectors, based on the weight of the children
/// </summary>
public sealed partial class GroupSelector : EntityTableSelector
{
    [DataField(required: true)]
    public List<EntityTableSelector> Children = new();

    protected override IEnumerable<EntProtoId> GetSpawnsImplementation(IRobustRandom rand,
        IEntityManager entMan,
        IPrototypeManager proto)
    {
        var children = new Dictionary<EntityTableSelector, float>(Children.Count);
        foreach (var child in Children)
            children.Add(child, child.Weight);

        var pick = rand.Pick(children);
        return pick.GetSpawns(rand, entMan, proto);
    }
}
