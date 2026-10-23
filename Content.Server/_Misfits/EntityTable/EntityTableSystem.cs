using Content.Server.NPC.HTN;
using Content.Shared.EntityTable.EntitySelectors;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;


namespace Content.Server._Misfits.EntityTable;

public sealed partial class EntityTableSystem : Content.Shared.EntityTable.EntityTableSystem
{
    /// <summary>
    /// DO NOT SET THIS TO A SEED.
    /// Internally the seeded implementation is alot more expensive and less random
    /// when expeditions get refactored i will probably write my own RobustRandom implementation
    /// </summary>
    public override IRobustRandom InitSystemRNG()
    {
        return new RobustRandom();
    }
}
