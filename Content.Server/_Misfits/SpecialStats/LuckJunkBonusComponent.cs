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

        new("N14SuperStimpak", LuckyLootRarity.VeryRare),
        new("N14PlasmaCartridge", LuckyLootRarity.VeryRare),
        new("N14FusionCore", LuckyLootRarity.VeryRare),
        new("N14PlasmaCartridgeMultiplas", LuckyLootRarity.VeryRare),
        new("N14PlasmaShell", LuckyLootRarity.VeryRare),

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
