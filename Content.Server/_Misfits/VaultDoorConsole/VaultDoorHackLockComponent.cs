namespace Content.Server._Misfits.VaultDoorConsole;

[RegisterComponent, Access(typeof(VaultDoorConsoleSystem))]
public sealed partial class VaultDoorHackLockComponent : Component
{
    public TimeSpan LockedUntil;
}
