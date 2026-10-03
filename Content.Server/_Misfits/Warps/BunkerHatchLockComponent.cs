// #Misfits Add - Lock state of a surface bunker hatch.
namespace Content.Server._Misfits.Warps;

/// <summary>
/// Lets a bunker hatch be locked. A locked hatch keeps outsiders out; Enclave members still pass,
/// and the hatch locks behind them. Enclave NCOs lock and unlock it (at the hatch or from the tac
/// map), anyone can pry it open with a crowbar, and an open hatch locks itself again after a while.
/// </summary>
[RegisterComponent]
public sealed partial class BunkerHatchLockComponent : Component
{
    /// <summary>
    /// Hatches start every round locked.
    /// </summary>
    [DataField]
    public bool Locked = true;

    /// <summary>
    /// How long prying it open with a crowbar takes.
    /// </summary>
    [DataField]
    public TimeSpan PryTime = TimeSpan.FromSeconds(85);

    /// <summary>
    /// An open hatch locks itself again this long after it was opened.
    /// </summary>
    [DataField]
    public TimeSpan AutoLockDelay = TimeSpan.FromMinutes(10);

    /// <summary>
    /// After someone locks the hatch by hand, nobody can lock it by hand again for this long.
    /// Automatic locks (the timer, or an Enclave member passing through) ignore this.
    /// </summary>
    [DataField]
    public TimeSpan LockCooldown = TimeSpan.FromSeconds(30);

    /// <summary>
    /// When an open hatch will lock itself. Null while locked.
    /// </summary>
    [ViewVariables]
    public TimeSpan? AutoLockAt;

    /// <summary>
    /// The earliest time a hand lock is allowed again.
    /// </summary>
    [ViewVariables]
    public TimeSpan NextLockAllowed;
}
