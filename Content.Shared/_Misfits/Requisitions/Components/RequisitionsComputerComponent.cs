using Content.Shared._Misfits.Currency.Components;
using Content.Shared._Misfits.Requisitions;
using Content.Shared._Misfits.Requisitions.Prototypes;
using Robust.Shared.GameStates;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Shared._Misfits.Requisitions.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true, fieldDeltas: true)]
[Access(typeof(SharedRequisitionsSystem))]
public sealed partial class RequisitionsComputerComponent : Component
{
    [DataField]
    public string Group = "Default";

    [DataField]
    public EntProtoId AccountProto = "N14ASRSAccount";

    [DataField]
    public CurrencyType? AcceptedCurrency;

    [DataField]
    public EntityUid? Account;

    [DataField("soundIncomingSurplus")]
    public SoundSpecifier IncomingSurplus = new SoundPathSpecifier("/Audio/Effects/Cargo/ping.ogg");

    [DataField]
    public EntityUid? Platform;

    [DataField(required: true), AutoNetworkedField, AlwaysPushInheritance]
    public List<RequisitionsCategory> Categories = new();

    [DataField, AutoNetworkedField, AlwaysPushInheritance]
    public List<RequisitionsSellEntry> SellEntries = new();

    [DataField, AutoNetworkedField, AlwaysPushInheritance]
    public List<RequisitionsBounty> Bounties = new();

    /// <summary>
    /// When set, the bounty board is filled with random <see cref="RequisitionsBountyPrototype"/>s
    /// from this pool and refilled as they are completed. When null the fixed
    /// <see cref="Bounties"/> list is used as-is.
    /// </summary>
    [DataField]
    public string? BountyPool;

    /// <summary>
    /// How many pooled bounties are posted at once. Unused when <see cref="BountyPool"/> is null.
    /// </summary>
    [DataField]
    public int MaxActiveBounties = 4;

    [AutoNetworkedField]
    public List<string> CompletedBounties = new();

    [AutoNetworkedField]
    public Dictionary<string, int> BountyProgress = new();

    [DataField]
    public ProtoId<RequisitionsRequestPoolPrototype>? RequestPool;

    [DataField]
    public ProtoId<RequisitionsRewardPoolPrototype>? RewardPool;

    [DataField]
    public int RandomRequestSlots = 5;

    [DataField]
    public TimeSpan RandomRequestRefillDelay = TimeSpan.FromMinutes(5);

    [DataField]
    public TimeSpan RandomRequestRerollDelay = TimeSpan.FromMinutes(30);

    [DataField]
    public int RandomRequestMinTargets = 1;

    [DataField]
    public int RandomRequestMaxTargets = 1;

    [DataField]
    public int HardRequestSlots;

    [DataField]
    public int DirectBudgetRequestSlots;

    [DataField]
    public float HardRequestScoreMultiplier = 3f;

    [AutoNetworkedField]
    public List<RequisitionsRandomSlot> RandomRequests = new();

    [AutoNetworkedField]
    public RequisitionsElevatorMode? PlatformLowered;

    [AutoNetworkedField]
    public bool Busy;

    [AutoNetworkedField]
    public TimeSpan? BusyStart;

    [AutoNetworkedField]
    public TimeSpan? BusyEnd;

    [AutoNetworkedField]
    public bool Linked;

    [AutoNetworkedField]
    public int Balance;

    [AutoNetworkedField]
    public bool Full;

    [AutoNetworkedField]
    public int OrderCount;

    [AutoNetworkedField]
    public int Capacity;

    [AutoNetworkedField]
    public int PlatformSaleValue;

    [AutoNetworkedField]
    public int PlatformSaleCount;

    [AutoNetworkedField]
    public List<RequisitionsSaleItem> PlatformItems = new();

    [AutoNetworkedField]
    public Dictionary<string, int> Storage = new();

    [AutoNetworkedField]
    public Dictionary<string, int> Purchased = new();

    [AutoNetworkedField]
    public List<RequisitionsPendingOrder> PendingOrders = new();

    [AutoNetworkedField]
    public List<RequisitionsHistoryEntry> History = new();

    [DataField]
    public bool IsLastInteracted = false;
}
