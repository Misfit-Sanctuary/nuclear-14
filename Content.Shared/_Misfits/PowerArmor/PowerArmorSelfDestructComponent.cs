using Content.Shared.Actions;
using Content.Shared.Inventory;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Misfits.PowerArmor;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class PowerArmorSelfDestructComponent : Component
{
    [DataField]
    public EntProtoId SelfDestructAction = "ActionPowerArmorSelfDestruct";

    [DataField, AutoNetworkedField]
    public EntityUid? SelfDestructActionEntity;

    [DataField]
    public SlotFlags RequiredSlot = SlotFlags.OUTERCLOTHING;
}

public sealed partial class PowerArmorSelfDestructEvent : InstantActionEvent { }
