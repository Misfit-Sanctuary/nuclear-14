using Robust.Shared.GameStates;

namespace Content.Shared._Misfits.VaultDoorConsole;


[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class VaultDoorConsoleGateComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool RaidActive;

    [DataField, AutoNetworkedField]
    public bool BypassRaidRequirement;
}
