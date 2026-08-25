using Content.Shared._Misfits.Requisitions;
using Content.Shared._Misfits.Requisitions.Components;
using Content.Shared.Climbing.Components;
using Content.Shared.StepTrigger.Systems;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using static Content.Shared._Misfits.Requisitions.Components.RequisitionsRailingMode;

namespace Content.Shared._Misfits.Requisitions;

public abstract class SharedRequisitionsSystem : EntitySystem
{
    [Dependency] private readonly FixtureSystem _fixtures = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RequisitionsElevatorComponent, StepTriggerAttemptEvent>(OnElevatorStepTriggerAttempt);

        SubscribeLocalEvent<RequisitionsRailingComponent, MapInitEvent>(OnRailingMapInit);
    }

    private void OnElevatorStepTriggerAttempt(Entity<RequisitionsElevatorComponent> elevator, ref StepTriggerAttemptEvent args)
    {
        if (elevator.Comp.Mode == RequisitionsElevatorMode.Raised)
            args.Cancelled = true;
    }

    private void OnRailingMapInit(Entity<RequisitionsRailingComponent> railing, ref MapInitEvent args)
    {
        UpdateRailing(railing);
    }

    private void UpdateRailing(Entity<RequisitionsRailingComponent> railing)
    {
        if (!TryComp(railing, out FixturesComponent? fixtures) ||
            _fixtures.GetFixtureOrNull(railing, railing.Comp.Fixture, fixtures) is not { } fixture)
        {
            return;
        }

        var hard = railing.Comp.Mode is Raising or Raised;
        _physics.SetHard(railing, fixture, hard);

        if (hard)
            EnsureComp<ClimbableComponent>(railing);
        else
            RemCompDeferred<ClimbableComponent>(railing);
    }

    protected void SetRailingMode(Entity<RequisitionsRailingComponent> railing, RequisitionsRailingMode mode)
    {
        if (railing.Comp.Mode == mode)
            return;

        railing.Comp.Mode = mode;
        Dirty(railing);

        UpdateRailing(railing);
    }

    public void ChangeBudget(int amount)
    {
        var accountQuery = EntityQueryEnumerator<RequisitionsAccountComponent>();
        while (accountQuery.MoveNext(out var uid, out var comp))
        {
            comp.Balance += amount;
            Dirty(uid, comp);
        }

        SendUIStateAll();
    }

    public void ChangeBudget(string group, int amount)
    {
        var accountQuery = EntityQueryEnumerator<RequisitionsAccountComponent>();
        while (accountQuery.MoveNext(out var uid, out var comp))
        {
            if (comp.Group != group)
                continue;

            comp.Balance += amount;
            Dirty(uid, comp);
        }

        SendUIStateAll();
    }

    protected void SendUIStateAll()
    {
        var query = EntityQueryEnumerator<RequisitionsComputerComponent>();
        while (query.MoveNext(out var uid, out var computer))
        {
            SendUIState((uid, computer));
        }
    }

    protected void SendUIState(Entity<RequisitionsComputerComponent> computer)
    {
        var elevator = GetElevator(computer);
        var account = CompOrNull<RequisitionsAccountComponent>(computer.Comp.Account);
        var mode = elevator?.Comp.NextMode ?? elevator?.Comp.Mode;
        var busy = elevator?.Comp.Busy ?? false;
        var balance = account?.Balance ?? 0;
        var orderCount = elevator?.Comp.Orders.Count ?? 0;
        var capacity = elevator != null ? GetElevatorCapacity(elevator.Value) : 0;
        var full = elevator != null && orderCount >= capacity;
        var pendingOrders = elevator != null
            ? GetPendingOrders(elevator.Value.Comp.Orders)
            : new List<RequisitionsPendingOrder>();

        var saleValue = 0;
        var saleCount = 0;
        var saleItems = new List<RequisitionsSaleItem>();
        TimeSpan? busyStart = null;
        TimeSpan? busyEnd = null;
        if (elevator != null)
        {
            saleValue = AppraisePlatform(elevator.Value, out saleCount, out saleItems);

            if (elevator.Value.Comp.ToggledAt is { } toggledAt)
            {
                busyStart = toggledAt;
                busyEnd = toggledAt + elevator.Value.Comp.ToggleDelay;
            }
        }

        var comp = computer.Comp;
        comp.PlatformLowered = mode;
        comp.Busy = busy;
        comp.BusyStart = busyStart;
        comp.BusyEnd = busyEnd;
        comp.Linked = elevator != null && account != null;
        comp.Balance = balance;
        comp.Full = full;
        comp.OrderCount = orderCount;
        comp.Capacity = capacity;
        comp.PlatformSaleValue = saleValue;
        comp.PlatformSaleCount = saleCount;
        comp.PlatformItems = saleItems;
        comp.Storage = account != null ? new Dictionary<string, int>(account.Storage) : new Dictionary<string, int>();
        comp.Purchased = account != null ? new Dictionary<string, int>(account.Purchased) : new Dictionary<string, int>();
        comp.History = account != null ? new List<RequisitionsHistoryEntry>(account.History) : new List<RequisitionsHistoryEntry>();
        comp.CompletedBounties = account != null ? new List<string>(account.CompletedBounties) : new List<string>();

        // A pooled console shows the account's rotating board rather than a fixed list.
        // Writing it into Bounties means the existing client rendering needs no changes.
        if (comp.BountyPool != null)
        {
            comp.Bounties = account != null
                ? new List<RequisitionsBounty>(account.ActiveBounties)
                : new List<RequisitionsBounty>();
        }

        comp.BountyProgress = account != null ? new Dictionary<string, int>(account.BountyProgress) : new Dictionary<string, int>();
        comp.RandomRequests = account != null ? new List<RequisitionsRandomSlot>(account.RandomRequests) : new List<RequisitionsRandomSlot>();
        comp.PendingOrders = pendingOrders;

        var uid = computer.Owner;
        DirtyField(uid, comp, nameof(RequisitionsComputerComponent.PlatformLowered));
        DirtyField(uid, comp, nameof(RequisitionsComputerComponent.Busy));
        DirtyField(uid, comp, nameof(RequisitionsComputerComponent.BusyStart));
        DirtyField(uid, comp, nameof(RequisitionsComputerComponent.BusyEnd));
        DirtyField(uid, comp, nameof(RequisitionsComputerComponent.Linked));
        DirtyField(uid, comp, nameof(RequisitionsComputerComponent.Balance));
        DirtyField(uid, comp, nameof(RequisitionsComputerComponent.Full));
        DirtyField(uid, comp, nameof(RequisitionsComputerComponent.OrderCount));
        DirtyField(uid, comp, nameof(RequisitionsComputerComponent.Capacity));
        DirtyField(uid, comp, nameof(RequisitionsComputerComponent.PlatformSaleValue));
        DirtyField(uid, comp, nameof(RequisitionsComputerComponent.PlatformSaleCount));
        DirtyField(uid, comp, nameof(RequisitionsComputerComponent.PlatformItems));
        DirtyField(uid, comp, nameof(RequisitionsComputerComponent.Storage));
        DirtyField(uid, comp, nameof(RequisitionsComputerComponent.Purchased));
        DirtyField(uid, comp, nameof(RequisitionsComputerComponent.History));
        DirtyField(uid, comp, nameof(RequisitionsComputerComponent.CompletedBounties));

        if (comp.BountyPool != null)
            DirtyField(uid, comp, nameof(RequisitionsComputerComponent.Bounties));

        DirtyField(uid, comp, nameof(RequisitionsComputerComponent.BountyProgress));
        DirtyField(uid, comp, nameof(RequisitionsComputerComponent.RandomRequests));
        DirtyField(uid, comp, nameof(RequisitionsComputerComponent.PendingOrders));
    }

    protected virtual int AppraisePlatform(Entity<RequisitionsElevatorComponent> elevator, out int count, out List<RequisitionsSaleItem> items)
    {
        count = 0;
        items = new List<RequisitionsSaleItem>();
        return 0;
    }

    private static List<RequisitionsPendingOrder> GetPendingOrders(List<RequisitionsEntry> orders)
    {
        var pendingOrders = new List<RequisitionsPendingOrder>();
        foreach (var order in orders)
        {
            var found = false;
            foreach (var pending in pendingOrders)
            {
                if (!SamePendingOrder(pending.Entry, order))
                    continue;

                pending.Amount++;
                found = true;
                break;
            }

            if (!found)
                pendingOrders.Add(new RequisitionsPendingOrder(order, 1));
        }

        return pendingOrders;
    }

    private static bool SamePendingOrder(RequisitionsEntry a, RequisitionsEntry b)
    {
        if (a.Crate != b.Crate ||
            a.Cost != b.Cost ||
            a.Name != b.Name ||
            a.Description != b.Description ||
            !Equals(a.Icon, b.Icon) ||
            a.Entities.Count != b.Entities.Count)
        {
            return false;
        }

        for (var i = 0; i < a.Entities.Count; i++)
        {
            if (a.Entities[i] != b.Entities[i])
                return false;
        }

        return true;
    }

    protected bool IsFull(Entity<RequisitionsElevatorComponent> elevator)
    {
        return elevator.Comp.Orders.Count >= GetElevatorCapacity(elevator);
    }

    protected int GetElevatorCapacity(Entity<RequisitionsElevatorComponent> elevator)
    {
        var side = (int) MathF.Floor(elevator.Comp.Radius * 2 + 1);
        return side * side;
    }

    protected Entity<RequisitionsElevatorComponent>? GetElevator(Entity<RequisitionsComputerComponent> computer)
    {
        var group = computer.Comp.Group;
        var elevators = new List<Entity<RequisitionsElevatorComponent, TransformComponent>>();
        var query = EntityQueryEnumerator<RequisitionsElevatorComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var elevator, out var xform))
        {
            if (elevator.Group != group)
                continue;

            elevators.Add((uid, elevator, xform));
        }

        if (elevators.Count == 0)
            return null;

        if (elevators.Count == 1)
            return (elevators[0].Owner, elevators[0].Comp1);

        var computerCoords = _transform.GetMapCoordinates(computer);
        Entity<RequisitionsElevatorComponent>? closest = null;
        var closestDistance = float.MaxValue;
        foreach (var (uid, elevator, xform) in elevators)
        {
            var elevatorCoords = _transform.GetMapCoordinates(uid, xform);
            if (computerCoords.MapId != elevatorCoords.MapId)
                continue;

            var distance = (elevatorCoords.Position - computerCoords.Position).LengthSquared();
            if (closestDistance > distance)
            {
                closestDistance = distance;
                closest = (uid, elevator);
            }
        }

        if (closest == null)
            return (elevators[0].Owner, elevators[0].Comp1);

        return closest;
    }
}
