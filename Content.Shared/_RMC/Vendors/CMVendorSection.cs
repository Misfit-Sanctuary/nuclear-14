using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Content.Shared.Roles;

namespace Content.Shared._RMC.Vendors;

[DataDefinition, Serializable, NetSerializable]
public sealed partial class CMVendorSection
{
    [DataField(required: true)]
    public string Name = string.Empty;

    /// <summary>
    /// Optional job-specific allocation gate. Empty means every faction member with the required authority tier
    /// can use the section. FullAllocationJobs on the vendor bypass this list.
    /// </summary>
    [DataField]
    public List<ProtoId<JobPrototype>> Jobs = new();

    [DataField]
    public (string Id, int Amount)? Choices;

    [DataField]
    public string? TakeAll;

    [DataField]
    public string? TakeOne;

    [DataField(required: true)]
    public List<CMVendorEntry> Entries = new();
}

[DataDefinition, Serializable, NetSerializable]
public sealed partial record CMVendorEntry
{
    [DataField(required: true)]
    public EntProtoId Id;

    [DataField]
    public string? Name;

    [DataField]
    public int? Amount;

    /// <summary>
    /// The finite stock ceiling used by faction replenishment. When omitted, the configured initial amount becomes
    /// the ceiling on first use.
    /// </summary>
    [DataField]
    public int? MaxAmount;

    [DataField]
    public int? Points;

    /// <summary>
    /// Shared faction replenishment points required to restore one unit of this entry. Falls back to the tier cost.
    /// </summary>
    [DataField]
    public int? ReplenishmentCost;

    /// <summary>
    /// Allocation sub-tab, such as Helmet, Armor, Ballistic, or Plasma.
    /// </summary>
    [DataField]
    public string? Category;

    /// <summary>
    /// Blueprint-style faction authority tier. Valid values are 1 through 5.
    /// </summary>
    [DataField]
    public int Tier = 1;

    /// <summary>
    /// Optional exact-job restriction for specialist equipment. Unlike the authority tier, this does not hide the
    /// listing: other vendor users still see it as ineligible.
    /// </summary>
    [DataField]
    public List<ProtoId<JobPrototype>> Jobs = new();

    /// <summary>
    /// Player-facing job or qualification required by <see cref="Jobs"/>.
    /// </summary>
    [DataField]
    public string? RequiredJob;

    [DataField]
    public List<EntProtoId> LinkedEntries = new();
}
