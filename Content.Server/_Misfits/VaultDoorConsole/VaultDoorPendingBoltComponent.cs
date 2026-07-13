namespace Content.Server._Misfits.VaultDoorConsole;

[RegisterComponent, Access(typeof(VaultDoorConsoleSystem))]
public sealed partial class VaultDoorPendingBoltComponent : Component
{
    public EntityUid Console;
}
