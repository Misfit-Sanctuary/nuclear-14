using Content.Shared.EntityTable.EntitySelectors;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Shared.EntityTable;

public abstract partial class EntityTableSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly INetManager _net = default!;
    protected static IRobustRandom SystemRNGInstance = default!;
    public override void Initialize()
    {
        SystemRNGInstance = InitSystemRNG();
    }

    public IEnumerable<EntProtoId> GetSpawns(EntityTableSelector? table)
    {
        if (table == null || _net.IsClient) // we probably want to move all rng logic to server
            return new List<EntProtoId>();

        return table.GetSpawns(SystemRNGInstance, EntityManager, _prototypeManager);
    }

    public IEnumerable<EntProtoId> GetSpawns(EntityTableSelector? table, IRobustRandom randomInstance)
    {
        if (table == null || _net.IsClient) // we probably want to move all rng logic to server
            return new List<EntProtoId>();

        return table.GetSpawns(randomInstance, EntityManager, _prototypeManager);
    }

    public virtual IRobustRandom InitSystemRNG()
    {
        return _random;
    }
}
