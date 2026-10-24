using Robust.Shared.Prototypes;

namespace Content.Server._Misfits.SpecialStats;

/// <summary>
/// Marks a storage entity as eligible for the Luck S.P.E.C.I.A.L. bonus.
/// </summary>
[RegisterComponent]
public sealed partial class LuckJunkBonusComponent : Component
{
    [DataField]
    public List<LuckyLootEntry> LuckyItems = new()
    {
        new("N14CurrencyCap", LuckyLootRarity.Common),
        new("N14CurrencyCap10", LuckyLootRarity.Common),
        new("N14Stimpak", LuckyLootRarity.Common),
        new("N14MagazinePistol22lr", LuckyLootRarity.Common),
        new("N14MagazinePistol9mm", LuckyLootRarity.Common),
        new("N14MagazinePistol10mm", LuckyLootRarity.Common),
        new("MagazineBox22", LuckyLootRarity.Common),
        new("MagazineBox9mm", LuckyLootRarity.Common),
        new("MagazineBox10mm", LuckyLootRarity.Common),
        new("MagazineBox20gauge", LuckyLootRarity.Common),
        new("N14MagazineShotgun20", LuckyLootRarity.Common),
        new("SpeedLoader9", LuckyLootRarity.Common),
        new("SpeedLoader10", LuckyLootRarity.Common),
        new("MisfitsDaturaHide", LuckyLootRarity.Common),
        new("MisfitsSnakebiteTourniquet", LuckyLootRarity.Common),
        new("MisfitsHealingSalve", LuckyLootRarity.Common),
        new("MisfitsHerbalAnodyne", LuckyLootRarity.Common),
        new("MisfitsHerbalAntimicrobial", LuckyLootRarity.Common),
        new("MisfitsDaturaHide3", LuckyLootRarity.Common),
        new("MisfitsSnakebiteTourniquet3", LuckyLootRarity.Common),
        new("MisfitsHealingSalve3", LuckyLootRarity.Common),
        new("MisfitsHerbalAnodyne3", LuckyLootRarity.Common),
        new("MisfitsHerbalAntimicrobial3", LuckyLootRarity.Common),
        new("MisfitsMakeshiftRepairPatch", LuckyLootRarity.Common),
        new("MisfitsPillAntibiotics", LuckyLootRarity.Common),
        new("MisfitsPillPenicillin", LuckyLootRarity.Common),
        new("MisfitsPillPrussianBlue", LuckyLootRarity.Common),
        new("MisfitsPillPilgrimsRemedy", LuckyLootRarity.Common),
        new("MisfitsPillHerbalStimulant", LuckyLootRarity.Common),
        new("N14FoodBlamcomaccheese", LuckyLootRarity.Common),
        new("N14FoodCram", LuckyLootRarity.Common),
        new("N14FoodInstamash", LuckyLootRarity.Common),
        new("N14FoodSugarbombs", LuckyLootRarity.Common),
        new("N14FoodBubblegum", LuckyLootRarity.Common),
        new("N14DrinkNukaCola", LuckyLootRarity.Common),
        new("N14JunkDuctTape", LuckyLootRarity.Common),
        new("N14JunkWonderglue", LuckyLootRarity.Common),

        new("N14CurrencyCap50", LuckyLootRarity.Uncommon),
        new("N14RadXPill", LuckyLootRarity.Uncommon),
        new("N14Jet", LuckyLootRarity.Uncommon),
        new("N14Turbo", LuckyLootRarity.Uncommon),
        new("N14RadAwayBloodbag", LuckyLootRarity.Uncommon),
        new("N14JunkToyNukaTruck", LuckyLootRarity.Uncommon),
        new("N14HolotapeMisfitsRandom", LuckyLootRarity.Uncommon),
        new("N14MagazineTesla", LuckyLootRarity.Uncommon),
        new("N14MagazineTumblers", LuckyLootRarity.Uncommon),
        new("N14MagazineGuns", LuckyLootRarity.Uncommon),
        new("N14MagazineWasteland", LuckyLootRarity.Uncommon),
        new("N14DrinkNukaColaQuartz", LuckyLootRarity.Uncommon),
        new("N14DrinkNukaColaVictory", LuckyLootRarity.Uncommon),
        new("N14MagazineAmerican180Drum", LuckyLootRarity.Uncommon),
        new("N14MagazineSMG9mm", LuckyLootRarity.Uncommon),
        new("N14MagazineSMG10mm", LuckyLootRarity.Uncommon),
        new("Magazine45SubMachineGun", LuckyLootRarity.Uncommon),
        new("N14MagazinePistol12mm", LuckyLootRarity.Uncommon),
        new("N14MagazineSMG12mm", LuckyLootRarity.Uncommon),
        new("N14MagazineShotgun12", LuckyLootRarity.Uncommon),
        new("MagazineBox12gauge", LuckyLootRarity.Uncommon),
        new("MagazineBox12", LuckyLootRarity.Uncommon),
        new("MagazineBox44", LuckyLootRarity.Uncommon),
        new("MagazineBox45", LuckyLootRarity.Uncommon),
        new("N14MagazinePistol45", LuckyLootRarity.Uncommon),
        new("N14MagazinePistol44", LuckyLootRarity.Uncommon),
        new("MagazineBox556", LuckyLootRarity.Uncommon),
        new("Magazine556Rifle", LuckyLootRarity.Uncommon),
        new("LongMagazine556Rifle", LuckyLootRarity.Uncommon),
        new("MagazineBox762", LuckyLootRarity.Uncommon),
        new("Magazine762Rifle", LuckyLootRarity.Uncommon),
        new("Magazine762AmmoShort", LuckyLootRarity.Uncommon),
        new("SpeedLoader44", LuckyLootRarity.Uncommon),
        new("MisfitsDaturaHide5", LuckyLootRarity.Uncommon),
        new("MisfitsSnakebiteTourniquet5", LuckyLootRarity.Uncommon),
        new("MisfitsHealingSalve5", LuckyLootRarity.Uncommon),
        new("MisfitsHerbalAnodyne5", LuckyLootRarity.Uncommon),
        new("MisfitsHerbalAntimicrobial5", LuckyLootRarity.Uncommon),
        new("MisfitsFieryPurgative", LuckyLootRarity.Uncommon),
        new("MisfitsBloodShield", LuckyLootRarity.Uncommon),
        new("MisfitsPilgrimsRemedy", LuckyLootRarity.Uncommon),
        new("MisfitsGravesalt", LuckyLootRarity.Uncommon),
        new("MisfitsHerbalStimulant", LuckyLootRarity.Uncommon),
        new("MisfitsReinforcedPlatingPatch", LuckyLootRarity.Uncommon),
        new("MisfitsInsulationPatch", LuckyLootRarity.Uncommon),
        new("MisfitsMakeshiftRepairPatch5", LuckyLootRarity.Uncommon),
        new("MisfitsPillRadShield", LuckyLootRarity.Uncommon),
        new("MisfitsPillTraumadol", LuckyLootRarity.Uncommon),
        new("N14Flashlight", LuckyLootRarity.Uncommon),
        new("N14Crowbar", LuckyLootRarity.Uncommon),
        new("N14Screwdriver", LuckyLootRarity.Uncommon),
        new("N14Multitool", LuckyLootRarity.Uncommon),
        new("N14DrinkNukaColaCherry", LuckyLootRarity.Uncommon),
        new("N14DrinkNukaColaWild", LuckyLootRarity.Uncommon),
        new("N14InstrumentHarmonica", LuckyLootRarity.Uncommon),

        new("N14DrinkNukaColaQuantum", LuckyLootRarity.Rare),
        new("N14HandheldHealthAnalyzer", LuckyLootRarity.Rare),
        new("N14MagazineSMG9mmDrum", LuckyLootRarity.Rare),
        new("LMGMagazine556Rifle", LuckyLootRarity.Rare),
        new("Magazine762AmmoBelt", LuckyLootRarity.Rare),
        new("MagazineBox308", LuckyLootRarity.Rare),
        new("Magazine308Rifle", LuckyLootRarity.Rare),
        new("Magazine308RifleLong", LuckyLootRarity.Rare),
        new("ClipMagazine308Rifle", LuckyLootRarity.Rare),
        new("Magazine308RifleSniper", LuckyLootRarity.Rare),
        new("MagazineBox45-70", LuckyLootRarity.Rare),
        new("SpeedLoader45-70", LuckyLootRarity.Rare),
        new("SpeedLoader45-70Tube", LuckyLootRarity.Rare),
        new("MagazineBox50", LuckyLootRarity.Rare),
        new("N14Magazine50AMR", LuckyLootRarity.Rare),
        new("N14MagazineMinigun5mm", LuckyLootRarity.Rare),
        new("40mmGrenadeFrag", LuckyLootRarity.Rare),
        new("40mmGrenadeFire", LuckyLootRarity.Rare),
        new("N14PowerCellSmall", LuckyLootRarity.Rare),
        new("N14MicrofusionCell", LuckyLootRarity.Rare),
        new("N14ElectronChargePack", LuckyLootRarity.Rare),
        new("MisfitsDaturaHide10", LuckyLootRarity.Rare),
        new("MisfitsSnakebiteTourniquet10", LuckyLootRarity.Rare),
        new("MisfitsHealingSalve10", LuckyLootRarity.Rare),
        new("MisfitsHerbalAnodyne10", LuckyLootRarity.Rare),
        new("MisfitsHerbalAntimicrobial10", LuckyLootRarity.Rare),
        new("MisfitsGlowingBloodPack", LuckyLootRarity.Rare),
        new("MisfitsAdvancedRepairPatch", LuckyLootRarity.Rare),
        new("MisfitsMakeshiftRepairPatch8", LuckyLootRarity.Rare),
        new("MisfitsReinforcedPlatingPatch5", LuckyLootRarity.Rare),
        new("MisfitsInsulationPatch5", LuckyLootRarity.Rare),
        new("MisfitsPillX111Compound", LuckyLootRarity.Rare),
        new("MisfitsPillRefreshingBeverage", LuckyLootRarity.Rare),
        new("MisfitsPillFury", LuckyLootRarity.Rare),
        new("MisfitsPillOverdrive", LuckyLootRarity.Rare),
        new("MisfitsPillXCell", LuckyLootRarity.Rare),
        new("MisfitsPillLazarusCompound", LuckyLootRarity.Rare),
        new("MisfitsPillAnesthezine", LuckyLootRarity.Rare),
        new("MisfitsPillNeuroblock", LuckyLootRarity.Rare),
        new("MisfitsMedkitGeneralFilled", LuckyLootRarity.Rare),
        new("MisfitsMedkitTraumaFilled", LuckyLootRarity.Rare),
        new("MisfitsMedkitBurnFilled", LuckyLootRarity.Rare),
        new("MisfitsMedkitToxinFilled", LuckyLootRarity.Rare),
        new("MisfitsMedkitRadiationFilled", LuckyLootRarity.Rare),
        new("MisfitsMedkitPainManagementFilled", LuckyLootRarity.Rare),
        new("MisfitsMedkitPsychiatricFilled", LuckyLootRarity.Rare),
        new("MisfitsMedkitNarcolepsyFilled", LuckyLootRarity.Rare),
        new("MisfitsMedkitInfectionFilled", LuckyLootRarity.Rare),
        new("MisfitsMedkitFieldFilled", LuckyLootRarity.Rare),
        new("MisfitsPillCanisterPrussianBlue", LuckyLootRarity.Rare),
        new("GeigerCounter", LuckyLootRarity.Rare),
        new("N14InstrumentGuitar", LuckyLootRarity.Rare),
        new("VinylDiskBigIron", LuckyLootRarity.Rare),
        new("VinylDiskInkspotsIDont", LuckyLootRarity.Rare),

        new("N14SuperStimpak", LuckyLootRarity.VeryRare),
        new("N14PlasmaCartridge", LuckyLootRarity.VeryRare),
        new("N14FusionCore", LuckyLootRarity.VeryRare),
        new("N14PlasmaCartridgeMultiplas", LuckyLootRarity.VeryRare),
        new("N14PlasmaShell", LuckyLootRarity.VeryRare),
        new("MisfitsReinforcedPlatingPatch8", LuckyLootRarity.VeryRare),
        new("MisfitsInsulationPatch8", LuckyLootRarity.VeryRare),
        new("MisfitsAdvancedRepairPatch5", LuckyLootRarity.VeryRare),
        new("MisfitsAdvancedRepairPatch8", LuckyLootRarity.VeryRare),
        new("MisfitsMedkitCombatFilled", LuckyLootRarity.VeryRare),
        new("MisfitsMedkitAdvancedEmergencyFilled", LuckyLootRarity.VeryRare),

        new("N14WastelanderPipboy", LuckyLootRarity.Legendary),
    };

    /// <summary>
    /// Additional roll-success probability per Luck point above the default of 5.
    /// </summary>
    [DataField]
    public float ChancePerLuckPoint = 0.06666667f;
}

public enum LuckyLootRarity : byte
{
    Common,
    Uncommon,
    Rare,
    VeryRare,
    Legendary,
}

[DataDefinition]
public sealed partial class LuckyLootEntry
{
    public LuckyLootEntry()
    {
    }

    public LuckyLootEntry(EntProtoId id, LuckyLootRarity rarity)
    {
        Id = id;
        Rarity = rarity;
    }

    [DataField(required: true)]
    public EntProtoId Id;

    [DataField]
    public LuckyLootRarity Rarity = LuckyLootRarity.Common;
}
