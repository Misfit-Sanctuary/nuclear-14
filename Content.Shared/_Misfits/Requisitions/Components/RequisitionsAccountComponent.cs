using Content.Shared._Misfits.Requisitions;
using Content.Shared._Misfits.Requisitions;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._Misfits.Requisitions.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentPause]
[Access(typeof(SharedRequisitionsSystem))]
public sealed partial class RequisitionsAccountComponent : Component
{
    [DataField]
    public string Group = "Default";

    [DataField]
    public bool Started;

    [DataField]
    public int Balance;

    [DataField]
    public int StartingBalance;

    [DataField]
    public int Gain;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextGain;

    [DataField]
    public TimeSpan GainEvery = TimeSpan.FromSeconds(30);

    [DataField]
    public Dictionary<string, int> Purchased = new();

    [DataField]
    public List<RequisitionsHistoryEntry> History = new();

    [DataField]
    public List<string> CompletedBounties = new();

    /// <summary>
    /// Bounties currently posted on this account's board. Only used by consoles with a
    /// <see cref="Components.RequisitionsComputerComponent.BountyPool"/>.
    /// </summary>
    [DataField]
    public List<RequisitionsBounty> ActiveBounties = new();

    [DataField]
    public Dictionary<string, int> BountyProgress = new();

    [DataField]
    public Dictionary<string, int> Storage = new();

    [DataField]
    public int StorageLimit = 2000;

    [DataField]
    public List<RequisitionsRandomSlot> RandomRequests = new();
}
