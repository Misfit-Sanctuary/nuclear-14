using Content.Server.Botany;
using Content.Server.Botany.Components;
using Content.Shared.EntityEffects;
using Content.Shared.Random;
using Content.Shared.Random.Helpers;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype;

namespace Content.Server.EntityEffects.Effects;

public sealed partial class PlantMutateChemicals : EntityEffect
{
    [DataField("randomPickBotanyReagent", customTypeSerializer: typeof(PrototypeIdSerializer<WeightedRandomFillSolutionPrototype>))]
    public string RandomPickBotanyReagent = "RandomPickBotanyReagent";

    public override void Effect(EntityEffectBaseArgs args)
    {
        var proto = IoCManager.Resolve<IPrototypeManager>();
        var random = IoCManager.Resolve<IRobustRandom>();
        var plantHolder = args.EntityManager.GetComponent<PlantHolderComponent>(args.TargetEntity);
        if (plantHolder.Seed == null)
            return;

        var chemicals = plantHolder.Seed.Chemicals;
        var pick = proto.Index<WeightedRandomFillSolutionPrototype>(RandomPickBotanyReagent).Pick(random);
        var amount = random.NextFloat(0.1f, (float) pick.quantity);
        var chemicalId = pick.reagent;
        var seedChemQuantity = new SeedChemQuantity();

        if (chemicals.ContainsKey(chemicalId))
        {
            seedChemQuantity.Min = chemicals[chemicalId].Min;
            seedChemQuantity.Max = chemicals[chemicalId].Max + amount;
        }
        else
        {
            seedChemQuantity.Min = Math.Clamp((float) pick.quantity / 5f, 0.0001f, 1f);
            seedChemQuantity.Max = seedChemQuantity.Min + amount;
            seedChemQuantity.Inherent = false;
        }

        seedChemQuantity.PotencyDivisor = 100f / seedChemQuantity.Max;
        chemicals[chemicalId] = seedChemQuantity;
    }

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
    {
        return null;
    }
}
