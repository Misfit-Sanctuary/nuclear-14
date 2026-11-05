using Content.Shared.Chemistry.Dispenser;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Whitelist;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype;

namespace Content.Shared.Chemistry.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ReagentDispenserComponent : Component
{
    [DataField("pack", customTypeSerializer: typeof(PrototypeIdSerializer<ReagentDispenserInventoryPrototype>))]
    public string? PackPrototypeId;

    [DataField("numStorageSlots")]
    public int NumSlots = 25;

    [DataField]
    public EntityWhitelist? StorageWhitelist;

    [DataField]
    public ItemSlot BeakerSlot = new();

    public static string BaseStorageSlotId = "ReagentDispenser-storageSlot";

    [DataField]
    public List<string> StorageSlotIds = new();

    [DataField]
    public List<ItemSlot> StorageSlots = new();

    [DataField("clickSound")]
    public SoundSpecifier ClickSound = new SoundPathSpecifier("/Audio/Machines/machine_switch.ogg");

    [DataField, AutoNetworkedField]
    public ReagentDispenserDispenseAmount DispenseAmount = ReagentDispenserDispenseAmount.U10;
}
