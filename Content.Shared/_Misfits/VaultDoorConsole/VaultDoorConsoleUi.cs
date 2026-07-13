using Robust.Shared.Serialization;

namespace Content.Shared._Misfits.VaultDoorConsole;

[Serializable, NetSerializable]
public enum VaultDoorConsoleUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public enum VaultDoorConsoleTokenKind : byte
{
    None,

    Word,

    Dud,
}

[Serializable, NetSerializable, DataRecord]
public partial struct VaultDoorConsoleSegment
{
    public string Text;
    public VaultDoorConsoleTokenKind Kind;

    public string Token;

    public bool Used;

    public VaultDoorConsoleSegment(string text, VaultDoorConsoleTokenKind kind, string token, bool used)
    {
        Text = text;
        Kind = kind;
        Token = token;
        Used = used;
    }
}

[Serializable, NetSerializable]
public sealed class VaultDoorConsoleBoundUserInterfaceState : BoundUserInterfaceState
{
    public readonly List<List<VaultDoorConsoleSegment>> ColumnA;
    public readonly List<List<VaultDoorConsoleSegment>> ColumnB;

    public readonly int AttemptsRemaining;
    public readonly int MaxAttempts;

    public readonly List<string> Log;

    public readonly bool Solved;

    public readonly TimeSpan? SolvedRemaining;

    public readonly bool LockedOut;
    public readonly TimeSpan? LockedOutRemaining;

    public VaultDoorConsoleBoundUserInterfaceState(
        List<List<VaultDoorConsoleSegment>> columnA,
        List<List<VaultDoorConsoleSegment>> columnB,
        int attemptsRemaining,
        int maxAttempts,
        List<string> log,
        bool solved,
        TimeSpan? solvedRemaining,
        bool lockedOut,
        TimeSpan? lockedOutRemaining)
    {
        ColumnA = columnA;
        ColumnB = columnB;
        AttemptsRemaining = attemptsRemaining;
        MaxAttempts = maxAttempts;
        Log = log;
        Solved = solved;
        SolvedRemaining = solvedRemaining;
        LockedOut = lockedOut;
        LockedOutRemaining = lockedOutRemaining;
    }
}

[Serializable, NetSerializable]
public sealed class VaultDoorConsoleGuessMessage : BoundUserInterfaceMessage
{
    public readonly string Token;

    public VaultDoorConsoleGuessMessage(string token)
    {
        Token = token;
    }
}
