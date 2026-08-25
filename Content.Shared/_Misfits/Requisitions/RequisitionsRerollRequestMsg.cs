using Robust.Shared.Serialization;

namespace Content.Shared._Misfits.Requisitions;

[Serializable, NetSerializable]
public sealed class RequisitionsRerollRequestMsg(int slot) : BoundUserInterfaceMessage
{
    public int Slot = slot;
}
