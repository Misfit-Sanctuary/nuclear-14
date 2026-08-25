using Robust.Shared.Prototypes;

namespace Content.Shared._Misfits.Requisitions;

/// <summary>
/// A collection job the requisitions terminal can post. Consoles with a
/// <c>bountyPool</c> draw a random handful of these into their bounty board and
/// replace each one as it is completed, instead of using a fixed <c>bounties</c> list.
/// </summary>
[Prototype("requisitionsBounty")]
public sealed partial class RequisitionsBountyPrototype : IPrototype
{
    /// <inheritdoc/>
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// Which console pools may draw this bounty. Empty means any pool.
    /// </summary>
    [DataField]
    public List<string> Pools = new();

    /// <summary>
    /// The entity that has to be delivered on the platform.
    /// </summary>
    [DataField(required: true)]
    public EntProtoId Item;

    /// <summary>
    /// How many of <see cref="Item"/> are needed.
    /// </summary>
    [DataField]
    public int Amount = 1;

    /// <summary>
    /// Budget paid out on completion.
    /// </summary>
    [DataField(required: true)]
    public int Reward;

    /// <summary>
    /// Optional crate banked into terminal storage on completion.
    /// </summary>
    [DataField]
    public EntProtoId? RewardCrate;

    /// <summary>
    /// Player facing name. Falls back to the item's own name when unset.
    /// </summary>
    [DataField]
    public LocId? Name;

    public RequisitionsBounty ToBounty()
    {
        return new RequisitionsBounty
        {
            Id = ID,
            Name = Name,
            Item = Item,
            Amount = Amount,
            Reward = Reward,
            RewardCrate = RewardCrate,
        };
    }
}
