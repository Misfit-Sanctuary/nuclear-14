using Content.Shared._Misfits.VaultDoorConsole;

namespace Content.Server._Misfits.VaultDoorConsole;

public enum VaultDoorConsoleDudEffect : byte
{
    ResetAttempts,
    RemoveDud,
}

[RegisterComponent, Access(typeof(VaultDoorConsoleSystem))]
public sealed partial class VaultDoorConsoleComponent : Component
{
    [DataField]
    public string SignalPort = "Pressed";

    [DataField]
    public int WordLength = 8;

    [DataField]
    public int PoolSize = 10;

    [DataField]
    public int DudCount = 3;

    [DataField]
    public int NoiseRowCount = 6;

    [DataField]
    public int MaxAttempts = 4;

    [DataField]
    public TimeSpan LockoutDuration = TimeSpan.FromMinutes(5);

    [DataField]
    public TimeSpan SuccessLockDuration = TimeSpan.FromMinutes(20);

    [DataField]
    public string RaidFaction = "Vault";

    [ViewVariables]
    public List<string> WordPool = new();

    [ViewVariables]
    public string TargetWord = string.Empty;

    [ViewVariables]
    public HashSet<string> RemovedWords = new();

    [ViewVariables]
    public Dictionary<string, VaultDoorConsoleDudEffect> Duds = new();

    [ViewVariables]
    public HashSet<string> ConsumedDuds = new();

    [ViewVariables]
    public List<List<VaultDoorConsoleSegment>> ColumnA = new();

    [ViewVariables]
    public List<List<VaultDoorConsoleSegment>> ColumnB = new();

    [ViewVariables]
    public int AttemptsRemaining;

    [ViewVariables]
    public List<string> Log = new();

    [ViewVariables]
    public bool Solved;

    [ViewVariables]
    public TimeSpan? SolvedUntil;

    [ViewVariables]
    public HashSet<EntityUid> BoltedDoors = new();

    [ViewVariables]
    public TimeSpan? LockedOutUntil;
}
