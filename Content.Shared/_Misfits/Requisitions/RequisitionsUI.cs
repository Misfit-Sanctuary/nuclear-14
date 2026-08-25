using Robust.Shared.Serialization;

namespace Content.Shared._Misfits.Requisitions;

[Serializable, NetSerializable]
public enum N14RequisitionsUIKey
{
    Key
}

[Serializable, NetSerializable]
public sealed class RequisitionsBuyMsg(int category, int order) : BoundUserInterfaceMessage
{
    public int Category = category;
    public int Order = order;
}

[Serializable, NetSerializable]
public sealed class RequisitionsCartItem(int category, int order, int amount)
{
    public int Category = category;
    public int Order = order;
    public int Amount = amount;
}

[Serializable, NetSerializable]
public sealed class RequisitionsBuyCartMsg(List<RequisitionsCartItem> items) : BoundUserInterfaceMessage
{
    public List<RequisitionsCartItem> Items = items;
}

[Serializable, NetSerializable]
public sealed class RequisitionsPendingOrder(RequisitionsEntry entry, int amount)
{
    public RequisitionsEntry Entry = entry;
    public int Amount = amount;
}

[Serializable, NetSerializable]
public sealed class RequisitionsHistoryEntry(string buyer, string crate, int amount, int cost, bool sold = false)
{
    public string Buyer = buyer;
    public string Crate = crate;
    public int Amount = amount;
    public int Cost = cost;
    public bool Sold = sold;
}

[Serializable, NetSerializable]
public sealed class RequisitionsPlatformMsg(bool raise) : BoundUserInterfaceMessage
{
    public bool Raise = raise;
}

[Serializable, NetSerializable]
public sealed class RequisitionsRefreshMsg : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class RequisitionsPrintHistoryMsg : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class RequisitionsWithdrawStorageMsg : BoundUserInterfaceMessage
{
    public string? Proto;
    public int Amount;
}

[Serializable, NetSerializable]
public sealed class RequisitionsSaleItem(string proto, int count, int value)
{
    public string Proto = proto;
    public int Count = count;
    public int Value = value;
    public List<string> Outputs = new();
}
