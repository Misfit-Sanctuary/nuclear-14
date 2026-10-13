using Content.Shared.Chemistry.Reagent;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Server._Misfits.MaterialExtractor;

[RegisterComponent]
public sealed partial class MaterialExtractorComponent : Component
{
    // Balance fields. Keep gameplay tuning on the entity prototype, not in the system.
    [DataField] public Dictionary<string, int> OutputWeights = new()
    {
        ["N14IronOre1"] = 20,
        ["N14CopperOre1"] = 18,
        ["N14LeadOre1"] = 14,
        ["SulfurOre1"] = 12,
        ["N14Sand1"] = 12,
        ["Salt1"] = 10,
        ["N14ZincOre1"] = 6,
        ["N14BauxiteOre1"] = 5,
        ["FertilizerOre1"] = 3,
    };
    [DataField] public int OperatorRadius = 4;
    [DataField] public int PulseIntervalSeconds = 2;
    [DataField] public int FirstWaveMinSeconds = 30;
    [DataField] public int FirstWaveMaxSeconds = 30;
    [DataField] public int WaveMinSeconds = 30;
    [DataField] public int WaveMaxSeconds = 30;
    [DataField] public int WaveWarningSeconds;
    [DataField] public int WaveMinMobCount = 1;
    [DataField] public int WaveMaxMobCount = 3;
    [DataField] public float WaveSpawnMinDistance = 16f;
    [DataField] public float WaveSpawnMaxDistance = 28f;
    [DataField] public int WaveSpawnAttempts = 40;
    [DataField] public float WaveSpawnPvsBuffer = 3f;
    [DataField] public Dictionary<string, int> WaveMobWeights = new()
    {
        ["N14MobMaterialExtractorMoleratWave"] = 24,
        ["N14MobMaterialExtractorGeckoWave"] = 18,
        ["N14MobMaterialExtractorMirelurkWave"] = 12,
        ["N14MobMaterialExtractorRadhogWave"] = 10,
        ["N14MobMaterialExtractorFireGeckoWave"] = 8,
        ["N14MobMaterialExtractorNightstalkerCubWave"] = 8,
        ["N14MobMaterialExtractorGiantAntWave"] = 7,
        ["N14MobMaterialExtractorNightstalkerWave"] = 4,
        ["N14MobMaterialExtractorRadMirelurkWave"] = 3,
        ["N14MobMaterialExtractorGiantFireAntWave"] = 3,
        ["N14MobMaterialExtractorRadscorpionWave"] = 3,
    };
    [DataField] public float PoorDepositChance = 0.25f;
    [DataField] public float RichDepositChance = 0.15f;
    [DataField] public float PoorYieldMultiplier = 0.7f;
    [DataField] public float RichYieldMultiplier = 1.4f;
    [DataField] public float LowFuelWarningFraction = 0.25f;
    [DataField] public string FuelSolution = "tank";
    [DataField] public ProtoId<ReagentPrototype> FuelReagent = "WeldingFuel";

    public TimeSpan NextPulse;
    public TimeSpan NextWave;
    public TimeSpan DamagePauseUntil;
    public bool BeaconOn;
    public bool WarningSent;
    public bool LowFuelWarningIssued;
    public bool WasRunning;
    public readonly HashSet<EntityUid> ActiveAttackers = [];
    public float YieldMultiplier = 1f;
    public string DepositQuality = "FAIR";
}
