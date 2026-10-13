using Robust.Shared.Serialization;

namespace Content.Shared._RMC.Vendors;

[Serializable, NetSerializable]
public enum CMAutomatedVendorUiKey
{
    Key
}

[Serializable, NetSerializable]
public sealed class CMAutomatedVendorState : BoundUserInterfaceState
{
    public List<CMVendorSectionState> Sections { get; }
    public List<CMVendorStoredItemState> StoredItems { get; }
    public int Points { get; }
    public int ReplenishmentPoints { get; }
    public bool CanReplenish { get; }
    public string ReplenishmentPrompt { get; }
    public TimeSpan? NextReplenishment { get; }
    public TimeSpan ReplenishmentInterval { get; }
    public bool CanStoreEquipment { get; }
    public string DepartmentName { get; }
    public string VendorTitle { get; }
    public List<string> AllocationCategories { get; }
    public List<string> SharedEquipmentCategories { get; }

    public CMAutomatedVendorState(
        List<CMVendorSectionState> sections,
        List<CMVendorStoredItemState> storedItems,
        int points,
        int replenishmentPoints,
        bool canReplenish,
        string replenishmentPrompt,
        TimeSpan? nextReplenishment,
        TimeSpan replenishmentInterval,
        bool canStoreEquipment,
        string departmentName,
        string vendorTitle,
        List<string> allocationCategories,
        List<string> sharedEquipmentCategories)
    {
        Sections = sections;
        StoredItems = storedItems;
        Points = points;
        ReplenishmentPoints = replenishmentPoints;
        CanReplenish = canReplenish;
        ReplenishmentPrompt = replenishmentPrompt;
        NextReplenishment = nextReplenishment;
        ReplenishmentInterval = replenishmentInterval;
        CanStoreEquipment = canStoreEquipment;
        DepartmentName = departmentName;
        VendorTitle = vendorTitle;
        AllocationCategories = allocationCategories;
        SharedEquipmentCategories = sharedEquipmentCategories;
    }
}

[Serializable, NetSerializable]
public sealed record CMVendorSectionState(string Name, int? Choices, int Purchases, List<CMVendorEntryState> Entries);

[Serializable, NetSerializable]
public sealed record CMVendorEntryState(
    string Name,
    EntProtoId Id,
    int? Amount,
    int? MaxAmount,
    int? Points,
    int Tier,
    bool HasAuthority,
    string? RequiredAuthority,
    bool HasRequiredJob,
    string? RequiredJob,
    string Category);

[Serializable, NetSerializable]
public sealed record CMVendorStoredItemState(NetEntity Entity, string Name, EntProtoId Id, string Category);

[Serializable, NetSerializable]
public sealed class CMAutomatedVendorVendMessage : BoundUserInterfaceMessage
{
    public int Section { get; }
    public int Entry { get; }

    public CMAutomatedVendorVendMessage(int section, int entry)
    {
        Section = section;
        Entry = entry;
    }
}

[Serializable, NetSerializable]
public sealed class CMAutomatedVendorReplenishMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class CMAutomatedVendorStoreHeldMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class CMAutomatedVendorWithdrawStoredMessage : BoundUserInterfaceMessage
{
    public NetEntity Item { get; }

    public CMAutomatedVendorWithdrawStoredMessage(NetEntity item)
    {
        Item = item;
    }
}
