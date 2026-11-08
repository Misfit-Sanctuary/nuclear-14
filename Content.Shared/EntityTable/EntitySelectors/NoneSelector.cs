using Robust.Shared.Prototypes;
using Robust.Shared.Random;


namespace Content.Shared.EntityTable.EntitySelectors;

/// <summary>
/// Selects nothing.
/// </summary>
public sealed partial class NoneSelector : EntityTableSelector
{
    protected override IEnumerable<EntProtoId> GetSpawnsImplementation(IRobustRandom rand,
        IEntityManager entMan,
        IPrototypeManager proto)
    {
        yield break;
    }
}
