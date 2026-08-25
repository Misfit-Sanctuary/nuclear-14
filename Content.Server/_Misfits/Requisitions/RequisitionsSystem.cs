using System.Linq;
using System.Numerics;
using Content.Server.Administration.Logs;
using System.Text;
using Content.Server.Cargo.Components;
using Content.Server.Cargo.Systems;
using Content.Server.Chat.Systems;
using Content.Server.Paper;
using Content.Server.Stack;
using Content.Server.Storage.Components;
using Content.Server.Storage.EntitySystems;
using Content.Shared._Misfits.Currency.Components;
using Content.Shared._Misfits.Requisitions;
using Content.Shared._Misfits.Requisitions.Prototypes;
using Content.Shared._Misfits.Requisitions;
using Content.Shared._Misfits.Requisitions.Components;
using Content.Shared.Chasm;
using Content.Shared.Chat;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Coordinates;
using Content.Shared.Database;
using Content.Shared.FixedPoint;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Components;
using Content.Shared.Physics;
using Content.Shared.Popups;
using Content.Shared.Stacks;
using Content.Shared.UserInterface;
using Robust.Server.Audio;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using static Content.Shared._Misfits.Requisitions.Components.RequisitionsElevatorMode;

namespace Content.Server._Misfits.Requisitions;

public sealed partial class RequisitionsSystem : SharedRequisitionsSystem
{
    private const float DirectBudgetMinMultiplier = 2.5f;
    private const float DirectBudgetMaxMultiplier = 3.5f;
    private const int DirectBudgetMinReward = 600;

    [Dependency] private readonly IAdminLogManager _adminLogs = default!;
    [Dependency] private readonly AudioSystem _audio = default!;
    [Dependency] private readonly ChasmSystem _chasm = default!;
    [Dependency] private readonly ChatSystem _chatSystem = default!;
    [Dependency] private readonly EntityStorageSystem _entityStorage = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly StackSystem _stack = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solutionContainers = default!;

    private static readonly EntProtoId PaperRequisitionInvoice = "N14PaperRequisitionInvoice";
    private static readonly EntProtoId StorageCrate = "CrateGenericSteel";
    private const int CrateResaleMinimum = 10;

    private EntityQuery<ChasmComponent> _chasmQuery;
    private EntityQuery<ChasmFallingComponent> _chasmFallingQuery;

    private readonly HashSet<Entity<MobStateComponent>> _toPit = new();

    public override void Initialize()
    {
        base.Initialize();

        _chasmQuery = GetEntityQuery<ChasmComponent>();
        _chasmFallingQuery = GetEntityQuery<ChasmFallingComponent>();

        SubscribeLocalEvent<RequisitionsComputerComponent, MapInitEvent>(OnComputerMapInit);
        SubscribeLocalEvent<RequisitionsComputerComponent, BeforeActivatableUIOpenEvent>(OnComputerBeforeActivatableUIOpen);
        SubscribeLocalEvent<RequisitionsComputerComponent, InteractUsingEvent>(OnComputerInteractUsing);

        Subs.BuiEvents<RequisitionsComputerComponent>(N14RequisitionsUIKey.Key, subs =>
        {
            subs.Event<RequisitionsBuyMsg>(OnBuy);
            subs.Event<RequisitionsBuyCartMsg>(OnBuyCart);
            subs.Event<RequisitionsPlatformMsg>(OnPlatform);
            subs.Event<RequisitionsRefreshMsg>(OnRefresh);
            subs.Event<RequisitionsPrintHistoryMsg>(OnPrintHistory);
            subs.Event<RequisitionsWithdrawStorageMsg>(OnWithdrawStorage);
            subs.Event<RequisitionsRerollRequestMsg>(OnRerollRequest);
        });
    }

    private void OnComputerMapInit(Entity<RequisitionsComputerComponent> ent, ref MapInitEvent args)
    {
        var account = GetAccount(ent.Comp.Group, ent.Comp.AccountProto);
        ent.Comp.Account = account;

        if (ent.Comp.BountyPool is { } pool)
            RefillBounties(account, pool, ent.Comp.MaxActiveBounties);

        Dirty(ent);
    }

    /// <summary>
    /// Tops the account's bounty board back up to <paramref name="max"/> by drawing random
    /// prototypes from <paramref name="pool"/>, skipping anything already posted or completed.
    /// If every candidate has been used it allows repeats rather than leaving the board short.
    /// </summary>
    private void RefillBounties(Entity<RequisitionsAccountComponent> account, string pool, int max)
    {
        var comp = account.Comp;
        if (comp.ActiveBounties.Count >= max)
            return;

        var candidates = new List<RequisitionsBountyPrototype>();
        var fallback = new List<RequisitionsBountyPrototype>();
        foreach (var proto in _prototypeManager.EnumeratePrototypes<RequisitionsBountyPrototype>())
        {
            if (proto.Pools.Count > 0 && !proto.Pools.Contains(pool))
                continue;

            fallback.Add(proto);

            if (comp.CompletedBounties.Contains(proto.ID))
                continue;

            if (comp.ActiveBounties.Any(b => b.Id == proto.ID))
                continue;

            candidates.Add(proto);
        }

        if (fallback.Count == 0)
            return;

        var changed = false;
        while (comp.ActiveBounties.Count < max)
        {
            RequisitionsBountyPrototype picked;
            if (candidates.Count > 0)
            {
                picked = _random.Pick(candidates);
                candidates.Remove(picked);
            }
            else
            {
                // Pool exhausted - allow a repeat so the board never sits empty.
                var repeatable = fallback.Where(p => comp.ActiveBounties.All(b => b.Id != p.ID)).ToList();
                if (repeatable.Count == 0)
                    break;

                picked = _random.Pick(repeatable);
            }

            comp.ActiveBounties.Add(picked.ToBounty());
            changed = true;
        }

        if (changed)
            Dirty(account);
    }

    private void OnComputerBeforeActivatableUIOpen(Entity<RequisitionsComputerComponent> computer, ref BeforeActivatableUIOpenEvent args)
    {
        SetUILastInteracted(computer);
        SendUIState(computer);
    }

    private void OnComputerInteractUsing(Entity<RequisitionsComputerComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || ent.Comp.AcceptedCurrency is not { } accepted)
            return;

        if (!TryComp(args.Used, out ConsumableCurrencyComponent? currency) || currency.CurrencyType != accepted)
            return;

        if (ent.Comp.Account is not { } accountUid || !TryComp(accountUid, out RequisitionsAccountComponent? account))
            return;

        var count = TryComp(args.Used, out StackComponent? stack) ? stack.Count : 1;
        var value = currency.ValuePerUnit * count;
        if (value <= 0)
            return;

        account.Balance += value;
        Dirty(accountUid, account);
        QueueDel(args.Used);

        _audio.PlayPvs(ent.Comp.IncomingSurplus, ent);
        _popup.PopupEntity(Loc.GetString("n14-requisitions-deposit", ("amount", value)), ent, args.User);
        SendUIStateAll();

        _adminLogs.Add(LogType.Action, $"{ToPrettyString(args.User):actor} deposited {value} into requisitions account {ent.Comp.Group}");
        args.Handled = true;
    }

    private void OnRefresh(Entity<RequisitionsComputerComponent> computer, ref RequisitionsRefreshMsg args)
    {
        SendUIState(computer);
    }

    private void OnWithdrawStorage(Entity<RequisitionsComputerComponent> computer, ref RequisitionsWithdrawStorageMsg args)
    {
        if (GetElevator(computer) is not { } elevator)
            return;

        if (computer.Comp.Account is not { } accountUid || !TryComp(accountUid, out RequisitionsAccountComponent? account))
            return;

        if (account.Storage.Count == 0)
            return;

        if (elevator.Comp.Orders.Count >= GetElevatorCapacity(elevator))
        {
            _popup.PopupEntity(Loc.GetString("n14-requisition-platform-full"), computer, args.Actor);
            return;
        }

        var contents = new Dictionary<string, int>();
        if (args.Proto is { } requested)
        {
            var have = account.Storage.GetValueOrDefault(requested);
            if (have <= 0)
                return;

            var take = args.Amount <= 0 ? have : Math.Min(args.Amount, have);
            contents[requested] = take;
        }
        else
        {
            foreach (var (proto, amount) in account.Storage)
                contents[proto] = amount;
        }

        if (contents.Count == 0)
            return;

        var orders = new List<RequisitionsEntry>();
        var loose = new Dictionary<string, int>();
        foreach (var (proto, amount) in contents)
        {
            if (IsCrateProto(proto))
            {
                for (var i = 0; i < amount; i++)
                    orders.Add(new RequisitionsEntry { Crate = proto, Cost = 0 });
            }
            else
            {
                loose[proto] = amount;
            }
        }

        if (loose.Count > 0)
        {
            var existingStorageOrder = elevator.Comp.Orders.Find(order => order.Crate.Id == StorageCrate.Id && order.Cost == 0);
            if (existingStorageOrder != null)
            {
                foreach (var (proto, amount) in loose)
                    existingStorageOrder.Contents[proto] = existingStorageOrder.Contents.GetValueOrDefault(proto) + amount;
            }
            else
            {
                orders.Add(new RequisitionsEntry { Crate = StorageCrate, Cost = 0, Contents = loose });
            }
        }

        if (elevator.Comp.Orders.Count + orders.Count > GetElevatorCapacity(elevator))
        {
            _popup.PopupEntity(Loc.GetString("n14-requisition-platform-full"), computer, args.Actor);
            return;
        }

        elevator.Comp.Orders.AddRange(orders);

        var withdrawn = 0;
        foreach (var (proto, amount) in contents)
        {
            withdrawn += amount;
            var remaining = account.Storage.GetValueOrDefault(proto) - amount;
            if (remaining > 0)
                account.Storage[proto] = remaining;
            else
                account.Storage.Remove(proto);
        }

        Dirty(accountUid, account);
        Dirty(elevator);
        _audio.PlayPvs(computer.Comp.IncomingSurplus, computer);
        SendUIStateAll();

        _adminLogs.Add(LogType.Action,
            $"{ToPrettyString(args.Actor):actor} withdrew {withdrawn} item(s) from requisitions storage {computer.Comp.Group}");
    }

    private void OnRerollRequest(Entity<RequisitionsComputerComponent> computer, ref RequisitionsRerollRequestMsg args)
    {
        if (computer.Comp.Account is not { } accountUid || !TryComp(accountUid, out RequisitionsAccountComponent? account))
            return;

        if (args.Slot < 0 || args.Slot >= account.RandomRequests.Count)
            return;

        var slot = account.RandomRequests[args.Slot];
        if (slot.Request is not { } request || _timing.CurTime < request.RerollAvailableAt)
            return;

        var config = GetRequestBoardConfig(computer.Comp.Group);
        if (config?.RequestPool is not { } poolId || !_prototypeManager.TryIndex(poolId, out var pool))
            return;

        if (config.RewardPool is not { } rewardPoolId || !_prototypeManager.TryIndex(rewardPoolId, out var rewardPool))
            return;

        RollRequestIntoSlot(slot, pool, rewardPool, config.RandomRequestRerollDelay, IsHardSlot(args.Slot, config), config.HardRequestScoreMultiplier,
            CollectActiveTargetIds(account.RandomRequests, slot), config.RandomRequestMinTargets, config.RandomRequestMaxTargets,
            IsDirectBudgetSlot(args.Slot, config));
        Dirty(accountUid, account);
        SendUIStateAll();

        _adminLogs.Add(LogType.Action,
            $"{ToPrettyString(args.Actor):actor} rerolled requisitions request slot {args.Slot} in group {computer.Comp.Group}");
    }

    public int DebugRerollAllRandomRequests()
    {
        var rerolled = 0;
        var time = _timing.CurTime;
        var accounts = EntityQueryEnumerator<RequisitionsAccountComponent>();
        while (accounts.MoveNext(out var uid, out var account))
        {
            var config = GetRequestBoardConfig(account.Group);
            if (config?.RequestPool is not { } poolId || !_prototypeManager.TryIndex(poolId, out var pool))
                continue;

            if (config.RewardPool is not { } rewardPoolId || !_prototypeManager.TryIndex(rewardPoolId, out var rewardPool))
                continue;

            EnsureRandomRequestSlots(account, config.RandomRequestSlots, time);

            for (var i = 0; i < account.RandomRequests.Count; i++)
            {
                var slot = account.RandomRequests[i];
                RollRequestIntoSlot(slot, pool, rewardPool, config.RandomRequestRerollDelay, IsHardSlot(i, config), config.HardRequestScoreMultiplier,
                    CollectActiveTargetIds(account.RandomRequests, slot), config.RandomRequestMinTargets, config.RandomRequestMaxTargets,
                    IsDirectBudgetSlot(i, config));
                rerolled++;
            }

            Dirty(uid, account);
        }

        if (rerolled > 0)
            SendUIStateAll();

        return rerolled;
    }

    private void OnPrintHistory(Entity<RequisitionsComputerComponent> computer, ref RequisitionsPrintHistoryMsg args)
    {
        if (computer.Comp.Account is not { } accountUid || !TryComp(accountUid, out RequisitionsAccountComponent? account))
            return;

        var builder = new StringBuilder();
        builder.Append(Loc.GetString("n14-requisition-transcript-header", ("group", computer.Comp.Group)));

        foreach (var entry in account.History)
        {
            var name = _prototypeManager.TryIndex<EntityPrototype>(entry.Crate, out var proto) ? proto.Name : entry.Crate;
            builder.Append('\n');
            builder.Append(Loc.GetString(
                entry.Sold ? "n14-requisition-transcript-sold" : "n14-requisition-transcript-bought",
                ("buyer", entry.Buyer),
                ("amount", entry.Amount),
                ("item", name),
                ("cost", entry.Cost)));
        }

        var paper = Spawn("Paper", Transform(computer).Coordinates);
        _metaSystem.SetEntityName(paper, Loc.GetString("n14-requisition-transcript-name"));
        if (TryComp(paper, out PaperComponent? paperComp))
            _paperSystem.SetContent(paper, builder.ToString(), paperComp);

        _audio.PlayPvs(computer.Comp.IncomingSurplus, computer);
    }

    private void OnBuy(Entity<RequisitionsComputerComponent> computer, ref RequisitionsBuyMsg args)
    {
        var items = new List<RequisitionsCartItem>
        {
            new(args.Category, args.Order, 1),
        };

        TryBuyCart(computer, args.Actor, items);
    }

    private void OnBuyCart(Entity<RequisitionsComputerComponent> computer, ref RequisitionsBuyCartMsg args)
    {
        TryBuyCart(computer, args.Actor, args.Items);
    }

    private void TryBuyCart(Entity<RequisitionsComputerComponent> computer, EntityUid actor, List<RequisitionsCartItem> items)
    {
        if (items.Count == 0)
            return;

        if (GetElevator(computer) is not { } elevator)
            return;

        var remainingCapacity = GetElevatorCapacity(elevator) - elevator.Comp.Orders.Count;
        if (remainingCapacity <= 0)
        {
            _popup.PopupEntity(Loc.GetString("n14-requisition-platform-full"), computer, actor);
            return;
        }

        if (computer.Comp.Account is not { } accountUid ||
            !TryComp(accountUid, out RequisitionsAccountComponent? account))
        {
            return;
        }

        var orders = new List<RequisitionsEntry>();
        var totalCost = 0;
        var totalAmount = 0;
        var pendingStock = new Dictionary<string, int>();
        var history = new List<RequisitionsHistoryEntry>();
        var buyerName = Name(actor);

        foreach (var item in items)
        {
            if (item.Amount <= 0)
                return;

            if (item.Category < 0 || item.Category >= computer.Comp.Categories.Count)
            {
                Log.Error($"Player {ToPrettyString(actor)} tried to buy out of bounds requisitions order: category {item.Category}");
                return;
            }

            var category = computer.Comp.Categories[item.Category];
            if (item.Order < 0 || item.Order >= category.Entries.Count)
            {
                Log.Error($"Player {ToPrettyString(actor)} tried to buy out of bounds requisitions order: category {item.Category}, order {item.Order}");
                return;
            }

            var order = category.Entries[item.Order];

            if (order.Stock >= 0)
            {
                var crateId = order.Crate.Id;
                var alreadyBought = account.Purchased.GetValueOrDefault(crateId) + pendingStock.GetValueOrDefault(crateId);
                if (alreadyBought + item.Amount > order.Stock)
                    return;

                pendingStock[crateId] = pendingStock.GetValueOrDefault(crateId) + item.Amount;
            }

            if (order.Cost > 0 && item.Amount > (int.MaxValue - totalCost) / order.Cost)
                return;

            if (item.Amount > int.MaxValue - totalAmount)
                return;

            totalCost += order.Cost * item.Amount;
            totalAmount += item.Amount;

            if (totalAmount > remainingCapacity)
            {
                _popup.PopupEntity(Loc.GetString("n14-requisition-platform-full"), computer, actor);
                return;
            }

            history.Add(new RequisitionsHistoryEntry(buyerName, order.Crate.Id, item.Amount, order.Cost * item.Amount));

            for (var i = 0; i < item.Amount; i++)
            {
                orders.Add(order);
            }
        }

        if (account.Balance < totalCost)
        {
            _popup.PopupEntity(Loc.GetString("n14-requisition-insufficient-funds"), computer, actor);
            return;
        }

        account.Balance -= totalCost;
        foreach (var (crateId, amount) in pendingStock)
        {
            account.Purchased[crateId] = account.Purchased.GetValueOrDefault(crateId) + amount;
        }

        history.Reverse();
        account.History.InsertRange(0, history);
        if (account.History.Count > 30)
            account.History.RemoveRange(30, account.History.Count - 30);

        elevator.Comp.Orders.AddRange(orders);
        Dirty(accountUid, account);
        Dirty(elevator);

        _audio.PlayPvs(computer.Comp.IncomingSurplus, computer);
        SendUIStateAll();
        _adminLogs.Add(LogType.Action, $"{ToPrettyString(actor):actor} bought {totalAmount} requisitions crate(s) for {totalCost}");
    }

    private void OnPlatform(Entity<RequisitionsComputerComponent> computer, ref RequisitionsPlatformMsg args)
    {
        if (GetElevator(computer) is not { } elevator)
            return;

        var comp = elevator.Comp;
        if (comp.NextMode != null || comp.Busy)
            return;

        if (comp.Mode == Lowering || comp.Mode == Raising)
            return;

        if (args.Raise && comp.Mode == Raised)
            return;

        if (!args.Raise && comp.Mode == Lowered)
            return;

        RequisitionsElevatorMode? nextMode = comp.Mode switch
        {
            Lowered => Raising,
            Raised => Lowering,
            _ => null
        };

        if (nextMode == null)
            return;

        if (nextMode == Lowering)
        {
            var mask = (int) (CollisionGroup.MobLayer | CollisionGroup.MobMask);
            foreach (var entity in _physics.GetEntitiesIntersectingBody(elevator, mask, false))
            {
                if (HasComp<MobStateComponent>(entity))
                    return;
            }
        }

        comp.ToggledAt = _timing.CurTime;
        comp.Busy = true;
        SetMode(elevator, Preparing, nextMode);
        Dirty(elevator);
    }

    public bool TryAddBudget(string group, int amount)
    {
        if (amount == 0)
            return false;

        var query = EntityQueryEnumerator<RequisitionsAccountComponent>();
        while (query.MoveNext(out var uid, out var account))
        {
            if (account.Group != group)
                continue;

            account.Balance += amount;
            Dirty(uid, account);
            SendUIStateAll();
            return true;
        }

        return false;
    }

    private Entity<RequisitionsAccountComponent> GetAccount(string group, EntProtoId proto)
    {
        var query = EntityQueryEnumerator<RequisitionsAccountComponent>();
        while (query.MoveNext(out var uid, out var account))
        {
            if (account.Group == group)
                return (uid, account);
        }

        var newAccount = Spawn(proto, MapCoordinates.Nullspace);
        var newAccountComp = EnsureComp<RequisitionsAccountComponent>(newAccount);
        newAccountComp.Group = group;

        if (!newAccountComp.Started)
        {
            newAccountComp.Started = true;
            newAccountComp.Balance = newAccountComp.StartingBalance;
            newAccountComp.NextGain = _timing.CurTime + newAccountComp.GainEvery;
        }

        Dirty(newAccount, newAccountComp);
        return (newAccount, newAccountComp);
    }

    private void UpdateRailings(Entity<RequisitionsElevatorComponent> elevator, RequisitionsRailingMode mode)
    {
        var coordinates = _transform.GetMapCoordinates(elevator);
        var railings = _lookup.GetEntitiesInRange<RequisitionsRailingComponent>(coordinates, elevator.Comp.Radius + 5);
        foreach (var railing in railings)
        {
            SetRailingMode(railing, mode);
        }
    }

    private void UpdateGears(Entity<RequisitionsElevatorComponent> elevator, RequisitionsGearMode mode)
    {
        var coordinates = _transform.GetMapCoordinates(elevator);
        var railings = _lookup.GetEntitiesInRange<RequisitionsGearComponent>(coordinates, elevator.Comp.Radius + 5);
        foreach (var railing in railings)
        {
            if (railing.Comp.Mode == mode)
                continue;

            railing.Comp.Mode = mode;
            Dirty(railing);
        }
    }

    private void SendUIFeedback(Entity<RequisitionsComputerComponent> computerEnt, string flavorText)
    {
        if (!TryComp(computerEnt, out RequisitionsComputerComponent? computerComp))
            return;

        _chatSystem.TrySendInGameICMessage(computerEnt,
            flavorText,
            InGameICChatType.Speak,
            ChatTransmitRange.GhostRangeLimit,
            nameOverride: Loc.GetString("n14-requisition-paperwork-receiver-name"));

        _audio.PlayPvs(computerComp.IncomingSurplus, computerEnt);
    }

    private void SendUIFeedback(string group, string flavorText)
    {
        var query = EntityQueryEnumerator<RequisitionsComputerComponent>();
        while (query.MoveNext(out var uid, out var computer))
        {
            if (computer.Group == group && computer.IsLastInteracted)
                SendUIFeedback((uid, computer), flavorText);
        }
    }

    private void SetUILastInteracted(Entity<RequisitionsComputerComponent> computerEnt)
    {
        if (!TryComp(computerEnt, out RequisitionsComputerComponent? selectedComputer))
            return;

        var query = EntityQueryEnumerator<RequisitionsComputerComponent>();
        while (query.MoveNext(out _, out var otherComputer))
        {
            if (otherComputer.Group == selectedComputer.Group)
                otherComputer.IsLastInteracted = false;
        }

        selectedComputer.IsLastInteracted = true;
    }

    private void TryPlayAudio(Entity<RequisitionsElevatorComponent> elevator)
    {
        var comp = elevator.Comp;
        if (comp.Audio != null)
            return;

        var time = _timing.CurTime;
        if (comp.NextMode == Lowering || comp.Mode == Lowering)
        {
            if (time < comp.ToggledAt + comp.LowerSoundDelay)
                return;

            comp.Audio = _audio.PlayPvs(comp.LoweringSound, elevator)?.Entity;
            return;
        }

        if (comp.NextMode == Raising || comp.Mode == Raising)
        {
            if (time < comp.ToggledAt + comp.RaiseSoundDelay)
                return;

            comp.Audio = _audio.PlayPvs(comp.RaisingSound, elevator)?.Entity;
        }
    }

    private void SetMode(Entity<RequisitionsElevatorComponent> elevator, RequisitionsElevatorMode mode, RequisitionsElevatorMode? nextMode)
    {
        elevator.Comp.Mode = mode;
        elevator.Comp.NextMode = nextMode;
        Dirty(elevator);

        RequisitionsGearMode? gearMode = mode switch
        {
            Lowered or Raised or Preparing => RequisitionsGearMode.Static,
            Lowering or Raising => RequisitionsGearMode.Moving,
            _ => null
        };

        if (gearMode != null)
            UpdateGears(elevator, gearMode.Value);

        RequisitionsRailingMode? railingMode = (mode, nextMode) switch
        {
            (Lowered, _) => RequisitionsRailingMode.Raised,
            (Raised, _) => RequisitionsRailingMode.Lowering,
            (_, Lowering) => RequisitionsRailingMode.Raising,
            _ => null
        };

        if (railingMode != null)
            UpdateRailings(elevator, railingMode.Value);

        SendUIStateAll();
    }

    private void SpawnOrders(Entity<RequisitionsElevatorComponent> elevator)
    {
        var comp = elevator.Comp;
        if (comp.Mode == Raised)
        {
            var coordinates = _transform.GetMoverCoordinates(elevator);
            var xOffset = comp.Radius;
            var yOffset = comp.Radius;
            foreach (var order in comp.Orders)
            {
                var crate = SpawnAtPosition(order.Crate, coordinates.Offset(new Vector2(xOffset, yOffset)));

                foreach (var prototype in order.Entities)
                {
                    var entity = Spawn(prototype, MapCoordinates.Nullspace);
                    _entityStorage.Insert(entity, crate);
                }

                foreach (var (prototype, amount) in order.Contents)
                {
                    if (amount <= 0)
                        continue;

                    if (_prototypeManager.TryIndex<EntityPrototype>(prototype, out var entProto) &&
                        entProto.TryGetComponent<StackComponent>(out _))
                    {
                        foreach (var entity in _stack.SpawnMultiple(prototype, amount, new EntityCoordinates(crate, Vector2.Zero)))
                            _entityStorage.Insert(entity, crate);
                    }
                    else
                    {
                        for (var i = 0; i < amount; i++)
                        {
                            var entity = Spawn(prototype, MapCoordinates.Nullspace);
                            _entityStorage.Insert(entity, crate);
                        }
                    }
                }

                PrintInvoice(crate, coordinates, PaperRequisitionInvoice);

                yOffset--;
                if (yOffset < -comp.Radius)
                {
                    yOffset = comp.Radius;
                    xOffset--;
                }

                if (xOffset < -comp.Radius)
                    xOffset = comp.Radius;
            }

            comp.Orders.Clear();
        }
    }

    private bool Sell(Entity<RequisitionsElevatorComponent> elevator)
    {
        var account = GetAccount(elevator.Comp.Group, elevator.Comp.AccountProto);
        var sellEntries = GetSellEntries(elevator.Comp.Group);
        var entities = _lookup.GetEntitiesIntersecting(elevator);
        var soldAny = false;
        var rewards = 0;
        var exchanged = new List<EntProtoId>();
        var delivered = new Dictionary<string, int>();
        var deliveredReagents = new Dictionary<string, FixedPoint2>();
        var soldLog = new Dictionary<string, (int Count, int Value)>();
        foreach (var entity in entities)
        {
            if (!IsSellableEntity(elevator, entity))
                continue;

            var proto = MatchKey(entity);
            var qty = TryComp(entity, out StackComponent? stack) ? stack.Count : 1;
            if (proto != null)
                delivered[proto] = delivered.GetValueOrDefault(proto) + qty;

            ScanDeliveredReagents(entity, deliveredReagents);

            var (value, exchange, matched) = AppraiseSale(entity, qty, sellEntries);
            rewards += value;

            for (var i = 0; i < qty; i++)
                exchanged.AddRange(exchange);

            if (matched || value > 0)
            {
                soldAny = true;
                LogSold(soldLog, proto, qty, value);
            }

            QueueDel(entity);
        }

        rewards += CompleteBounties(elevator, account, delivered);
        CompleteRandomRequests(elevator, account, delivered, deliveredReagents);
        RecordSales(account, soldLog);

        var storageFull = false;
        foreach (var proto in exchanged)
        {
            var (key, units) = ResolveStorageUnit(proto);
            storageFull |= AddToStorage(account.Comp, key, units);
        }

        if (storageFull)
            SendUIFeedback(elevator.Comp.Group, Loc.GetString("n14-requisition-storage-full"));

        if (rewards > 0)
            SendUIFeedback(elevator.Comp.Group, Loc.GetString("n14-requisition-paperwork-reward-message", ("amount", rewards)));

        account.Comp.Balance += rewards;

        Dirty(account);

        if (soldAny || rewards > 0 || exchanged.Count > 0)
        {
            _adminLogs.Add(LogType.Action,
                $"Requisitions account {elevator.Comp.Group} processed a sale: +{rewards} budget, {soldLog.Count} item type(s) sold, {exchanged.Count} item(s) banked into storage");
        }

        return soldAny;
    }

    private static void LogSold(Dictionary<string, (int Count, int Value)> log, string? proto, int count, int value)
    {
        if (proto == null)
            return;

        var existing = log.GetValueOrDefault(proto);
        log[proto] = (existing.Count + count, existing.Value + value);
    }

    private void RecordSales(Entity<RequisitionsAccountComponent> account, Dictionary<string, (int Count, int Value)> soldLog)
    {
        if (soldLog.Count == 0)
            return;

        foreach (var (proto, info) in soldLog)
        {
            account.Comp.History.Insert(0, new RequisitionsHistoryEntry(string.Empty, proto, info.Count, info.Value, sold: true));
        }

        if (account.Comp.History.Count > 30)
            account.Comp.History.RemoveRange(30, account.Comp.History.Count - 30);
    }

    private int CompleteBounties(Entity<RequisitionsElevatorComponent> elevator, Entity<RequisitionsAccountComponent> account, Dictionary<string, int> delivered)
    {
        var group = elevator.Comp.Group;
        var query = EntityQueryEnumerator<RequisitionsComputerComponent>();
        List<RequisitionsBounty>? bounties = null;
        string? pool = null;
        var maxActive = 0;
        while (query.MoveNext(out _, out var comp))
        {
            if (comp.Group != group)
                continue;

            // A console with a pool drives a rotating board on the account; otherwise the
            // console's own hand-written list is used, unchanged.
            if (comp.BountyPool is { } configured)
            {
                pool = configured;
                maxActive = comp.MaxActiveBounties;
                bounties = account.Comp.ActiveBounties;
            }
            else
            {
                bounties = comp.Bounties;
            }

            break;
        }

        if (bounties == null || bounties.Count == 0)
            return 0;

        var finished = pool != null ? new List<string>() : null;

        var reward = 0;
        var changed = false;
        // Copied because completing a pooled bounty removes it from the live list.
        foreach (var bounty in bounties.ToList())
        {
            if (account.Comp.CompletedBounties.Contains(bounty.Id))
                continue;

            var delivCount = delivered.GetValueOrDefault(bounty.Item.Id);
            if (delivCount <= 0)
                continue;

            var progress = account.Comp.BountyProgress.GetValueOrDefault(bounty.Id) + delivCount;
            if (progress >= bounty.Amount)
            {
                var amount = Math.Max(1, bounty.Amount);
                var completions = bounty.Repeatable ? progress / amount : 1;

                reward += bounty.Reward * completions;

                if (bounty.RewardCrate is { } rewardCrate)
                    AddToStorage(account.Comp, rewardCrate, completions);

                if (bounty.Repeatable)
                {
                    var remainder = progress - completions * amount;
                    if (remainder > 0)
                        account.Comp.BountyProgress[bounty.Id] = remainder;
                    else
                        account.Comp.BountyProgress.Remove(bounty.Id);
                }
                else
                {
                    account.Comp.CompletedBounties.Add(bounty.Id);
                    account.Comp.BountyProgress.Remove(bounty.Id);
                    finished?.Add(bounty.Id);
                }

                _adminLogs.Add(LogType.Action,
                    $"Requisitions account {group} completed bounty {bounty.Id} x{completions} (reward {bounty.Reward * completions}{(bounty.RewardCrate is { } rc ? $", crate {rc.Id}" : "")})");
            }
            else
            {
                account.Comp.BountyProgress[bounty.Id] = progress;
            }

            changed = true;
        }

        // Pull completed jobs off the board and post fresh ones in their place.
        if (pool != null && finished is { Count: > 0 })
        {
            account.Comp.ActiveBounties.RemoveAll(b => finished.Contains(b.Id));
            RefillBounties(account, pool, maxActive);
            changed = true;
        }

        if (changed)
            Dirty(account);

        return reward;
    }

    private void ScanDeliveredReagents(EntityUid entity, Dictionary<string, FixedPoint2> deliveredReagents)
    {
        if (!TryComp(entity, out SolutionContainerManagerComponent? solutionManager))
            return;

        foreach (var (_, soln) in _solutionContainers.EnumerateSolutions((entity, solutionManager)))
        {
            foreach (var reagentQuantity in soln.Comp.Solution.Contents)
            {
                var reagentId = reagentQuantity.Reagent.Prototype;
                deliveredReagents[reagentId] = deliveredReagents.GetValueOrDefault(reagentId) + reagentQuantity.Quantity;
            }
        }
    }

    private RequisitionsComputerComponent? GetRequestBoardConfig(string group)
    {
        var query = EntityQueryEnumerator<RequisitionsComputerComponent>();
        while (query.MoveNext(out _, out var comp))
        {
            if (comp.Group == group && comp.RequestPool != null)
                return comp;
        }

        return null;
    }

    private static void EnsureRandomRequestSlots(RequisitionsAccountComponent account, int slotCount, TimeSpan now)
    {
        while (account.RandomRequests.Count < slotCount)
            account.RandomRequests.Add(new RequisitionsRandomSlot { NextRollAt = now });
    }

    private bool ProcessRandomRequestSlots(Entity<RequisitionsAccountComponent> account, TimeSpan time)
    {
        var config = GetRequestBoardConfig(account.Comp.Group);
        if (config?.RequestPool is not { } poolId || !_prototypeManager.TryIndex(poolId, out var pool))
            return false;

        if (config.RewardPool is not { } rewardPoolId || !_prototypeManager.TryIndex(rewardPoolId, out var rewardPool))
            return false;

        EnsureRandomRequestSlots(account.Comp, config.RandomRequestSlots, time);

        var changed = false;
        for (var i = 0; i < account.Comp.RandomRequests.Count; i++)
        {
            var slot = account.Comp.RandomRequests[i];
            if (slot.Request != null || time < slot.NextRollAt)
                continue;

            RollRequestIntoSlot(slot, pool, rewardPool, config.RandomRequestRerollDelay, IsHardSlot(i, config), config.HardRequestScoreMultiplier,
                CollectActiveTargetIds(account.Comp.RandomRequests, slot), config.RandomRequestMinTargets, config.RandomRequestMaxTargets,
                IsDirectBudgetSlot(i, config));
            changed = true;
        }

        return changed;
    }

    private static bool IsDirectBudgetSlot(int index, RequisitionsComputerComponent config)
    {
        return index < config.DirectBudgetRequestSlots;
    }

    private static bool IsHardSlot(int index, RequisitionsComputerComponent config)
    {
        return index >= config.DirectBudgetRequestSlots && index < config.DirectBudgetRequestSlots + config.HardRequestSlots;
    }

    private static HashSet<string> CollectActiveTargetIds(List<RequisitionsRandomSlot> slots, RequisitionsRandomSlot excludeSlot)
    {
        var set = new HashSet<string>();
        foreach (var other in slots)
        {
            if (ReferenceEquals(other, excludeSlot))
                continue;

            if (other.Request is not { } request)
                continue;

            foreach (var target in request.Targets)
                set.Add(target.TargetId);
        }

        return set;
    }

    private readonly record struct RolledRequestTarget(string TargetId, bool IsReagent, int Amount, int Score, bool DirectBudget);

    private RolledRequestTarget? TryRollSingleTarget(RequisitionsRequestPoolPrototype pool, HashSet<string> excludeIds, bool isHard, float hardScoreMultiplier)
    {
        var totalWeight = 0f;

        foreach (var item in pool.Items)
        {
            if (excludeIds.Contains(item.Item.Id))
                continue;
            totalWeight += item.Weight;
        }
        foreach (var material in pool.Materials)
        {
            if (excludeIds.Contains(material.Material.Id))
                continue;
            totalWeight += material.Weight;
        }
        foreach (var reagent in pool.Reagents)
        {
            if (excludeIds.Contains(reagent.Reagent.Id))
                continue;
            totalWeight += reagent.Weight;
        }

        if (totalWeight <= 0f)
            return null;

        var roll = _random.NextFloat(totalWeight);
        var accumulated = 0f;

        foreach (var item in pool.Items)
        {
            if (excludeIds.Contains(item.Item.Id))
                continue;

            accumulated += item.Weight;
            if (roll > accumulated)
                continue;

            var amount = _random.Next(item.MinAmount, item.MaxAmount + 1);
            var score = (int) MathF.Round(item.ValuePerUnit * amount);
            if (isHard)
                score = (int) MathF.Round(score * hardScoreMultiplier);
            return new RolledRequestTarget(item.Item.Id, false, amount, score, false);
        }

        foreach (var material in pool.Materials)
        {
            if (excludeIds.Contains(material.Material.Id))
                continue;

            accumulated += material.Weight;
            if (roll > accumulated)
                continue;

            var amount = _random.Next(material.MinAmount, material.MaxAmount + 1);
            var score = (int) MathF.Round(material.ValuePerUnit * amount);
            if (isHard)
                score = (int) MathF.Round(score * hardScoreMultiplier);
            return new RolledRequestTarget(material.Material.Id, false, amount, score, material.DirectBudget);
        }

        foreach (var reagent in pool.Reagents)
        {
            if (excludeIds.Contains(reagent.Reagent.Id))
                continue;

            accumulated += reagent.Weight;
            if (roll > accumulated)
                continue;

            var amount = _random.Next(reagent.MinAmount, reagent.MaxAmount + 1);
            var score = (int) MathF.Round(reagent.ValuePerUnit * amount);
            if (isHard)
                score = (int) MathF.Round(score * hardScoreMultiplier);
            return new RolledRequestTarget(reagent.Reagent.Id, true, amount, score, false);
        }

        return null;
    }

    private void RollRequestIntoSlot(
        RequisitionsRandomSlot slot,
        RequisitionsRequestPoolPrototype pool,
        RequisitionsRewardPoolPrototype rewardPool,
        TimeSpan rerollDelay,
        bool isHard = false,
        float hardScoreMultiplier = 1f,
        HashSet<string>? excludeTargetIds = null,
        int minTargets = 1,
        int maxTargets = 1,
        bool forceDirectBudget = false)
    {
        minTargets = Math.Max(1, minTargets);
        maxTargets = Math.Max(minTargets, maxTargets);
        var targetCount = _random.Next(minTargets, maxTargets + 1);

        var excludeIds = excludeTargetIds != null ? new HashSet<string>(excludeTargetIds) : new HashSet<string>();
        var targets = new List<RequisitionsRandomRequestTarget>();
        var totalScore = 0;
        var directBudget = forceDirectBudget;

        for (var i = 0; i < targetCount; i++)
        {
            if (TryRollSingleTarget(pool, excludeIds, isHard, hardScoreMultiplier) is not { } rolled)
                break;

            excludeIds.Add(rolled.TargetId);
            targets.Add(new RequisitionsRandomRequestTarget
            {
                IsReagent = rolled.IsReagent,
                TargetId = rolled.TargetId,
                Amount = rolled.Amount,
                Progress = 0,
            });

            totalScore += rolled.Score;
            directBudget |= rolled.DirectBudget;
        }

        if (targets.Count == 0)
            return;

        if (directBudget)
        {
            totalScore = (int) MathF.Round(totalScore * _random.NextFloat(DirectBudgetMinMultiplier, DirectBudgetMaxMultiplier));
            totalScore = Math.Max(totalScore, DirectBudgetMinReward);
        }

        slot.Request = new RequisitionsRandomRequest
        {
            Targets = targets,
            Score = totalScore,
            IsHard = isHard,
            DirectBudget = directBudget,
            RewardItems = directBudget ? new Dictionary<string, int>() : PickRewardItems(rewardPool, totalScore),
            RerollAvailableAt = _timing.CurTime + rerollDelay,
        };
    }

    private void CompleteRandomRequests(
        Entity<RequisitionsElevatorComponent> elevator,
        Entity<RequisitionsAccountComponent> account,
        Dictionary<string, int> delivered,
        Dictionary<string, FixedPoint2> deliveredReagents)
    {
        var group = elevator.Comp.Group;
        var config = GetRequestBoardConfig(group);
        if (config == null)
            return;

        var changed = false;
        foreach (var slot in account.Comp.RandomRequests)
        {
            if (slot.Request is not { } request || request.Targets.Count == 0)
                continue;

            var progressed = false;
            foreach (var target in request.Targets)
            {
                var needed = target.Amount - target.Progress;
                if (needed <= 0)
                    continue;

                var delivCount = target.IsReagent
                    ? deliveredReagents.GetValueOrDefault(target.TargetId).Int()
                    : delivered.GetValueOrDefault(target.TargetId);

                if (delivCount <= 0)
                    continue;

                target.Progress += Math.Min(delivCount, needed);
                progressed = true;
            }

            if (!progressed)
                continue;

            changed = true;

            if (request.Targets.Any(t => t.Progress < t.Amount))
                continue;

            string rewardLog;
            if (request.DirectBudget)
            {
                account.Comp.Balance += request.Score;
                rewardLog = $"{request.Score} budget";
            }
            else
            {
                foreach (var (item, amount) in request.RewardItems)
                    AddToStorage(account.Comp, item, amount);

                rewardLog = request.RewardItems.Count > 0
                    ? string.Join(", ", request.RewardItems.Select(kv => $"{kv.Value}x {kv.Key}"))
                    : "none";
            }

            var targetsLog = string.Join(", ", request.Targets.Select(t => $"{t.Amount}x {t.TargetId}"));
            _adminLogs.Add(LogType.Action,
                $"Requisitions account {group} completed random request for {targetsLog} " +
                $"(score {request.Score}, reward {rewardLog})");

            slot.Request = null;
            slot.NextRollAt = _timing.CurTime + config.RandomRequestRefillDelay;
        }

        if (changed)
            Dirty(account);
    }

    private Dictionary<string, int> PickRewardItems(RequisitionsRewardPoolPrototype rewardPool, int score)
    {
        var result = new Dictionary<string, int>();
        if (rewardPool.Items.Count == 0)
            return result;

        var budget = (float) score;
        var affordable = new List<RequisitionsRewardItemEntry>();
        var guard = 0;

        while (budget > 0f && guard++ < 12)
        {
            affordable.Clear();
            var totalWeight = 0f;
            foreach (var entry in rewardPool.Items)
            {
                if (entry.Cost > budget)
                    continue;

                affordable.Add(entry);
                totalWeight += entry.Weight;
            }

            if (affordable.Count == 0 || totalWeight <= 0f)
                break;

            var roll = _random.NextFloat(totalWeight);
            var accumulated = 0f;
            RequisitionsRewardItemEntry? picked = null;
            foreach (var entry in affordable)
            {
                accumulated += entry.Weight;
                if (roll > accumulated)
                    continue;

                picked = entry;
                break;
            }

            if (picked == null)
                break;

            var amount = _random.Next(picked.MinAmount, picked.MaxAmount + 1);
            result[picked.Item.Id] = result.GetValueOrDefault(picked.Item.Id) + amount;
            budget -= picked.Cost * amount;
        }

        if (result.Count == 0)
        {
            var cheapest = rewardPool.Items[0];
            foreach (var entry in rewardPool.Items)
            {
                if (entry.Cost < cheapest.Cost)
                    cheapest = entry;
            }

            result[cheapest.Item.Id] = cheapest.MinAmount;
        }

        return result;
    }

    protected override int AppraisePlatform(Entity<RequisitionsElevatorComponent> elevator, out int count, out List<RequisitionsSaleItem> items)
    {
        count = 0;
        var value = 0;
        var agg = new Dictionary<string, (int Count, int Value, List<string> Outputs)>();
        var sellEntries = GetSellEntries(elevator.Comp.Group);
        foreach (var entity in _lookup.GetEntitiesIntersecting(elevator))
        {
            if (!IsSellableEntity(elevator, entity))
                continue;

            var key = MatchKey(entity);
            if (key == null)
                continue;

            var qty = TryComp(entity, out StackComponent? stack) ? stack.Count : 1;
            var (entityValue, exchange, _) = AppraiseSale(entity, qty, sellEntries);
            var exchangeOutputs = exchange.Count > 0 ? exchange.Select(e => e.Id).ToList() : null;

            if (entityValue <= 0 && exchangeOutputs == null)
                continue;

            if (!agg.TryGetValue(key, out var existing))
                existing = (0, 0, new List<string>());

            if (exchangeOutputs != null)
            {
                foreach (var output in exchangeOutputs)
                {
                    if (!existing.Outputs.Contains(output))
                        existing.Outputs.Add(output);
                }
            }

            agg[key] = (existing.Count + qty, existing.Value + entityValue, existing.Outputs);
            value += entityValue;
        }

        items = new List<RequisitionsSaleItem>();
        foreach (var kvp in agg)
        {
            var info = kvp.Value;
            items.Add(new RequisitionsSaleItem(kvp.Key, info.Count, info.Value) { Outputs = info.Outputs });
            count += info.Count;
        }

        return value;
    }

    private bool IsSellableEntity(Entity<RequisitionsElevatorComponent> elevator, EntityUid entity)
    {
        return entity != elevator.Comp.Audio
               && !HasComp<CargoSellBlacklistComponent>(entity)
               && !HasComp<MobStateComponent>(entity);
    }

    private (int Value, List<EntProtoId> Exchange, bool Matched) AppraiseSale(EntityUid entity, int qty, List<RequisitionsSellEntry> sellEntries)
    {
        if (TryGetSellEntry(sellEntries, entity, out var sellEntry))
            return (sellEntry.Value * qty, sellEntry.Exchange, true);

        return (SubmitInvoices(entity) + SellValue(entity), EmptyExchange, false);
    }

    private static readonly List<EntProtoId> EmptyExchange = new();

    private int SellValue(EntityUid entity)
    {
        if (TryComp(entity, out RequisitionsCrateComponent? crate))
            return Math.Max(crate.Reward, CrateResaleMinimum);

        return 0;
    }

    private List<RequisitionsSellEntry> GetSellEntries(string group)
    {
        var query = EntityQueryEnumerator<RequisitionsComputerComponent>();
        while (query.MoveNext(out _, out var comp))
        {
            if (comp.Group == group)
                return comp.SellEntries;
        }

        return new List<RequisitionsSellEntry>();
    }

    private bool TryGetSellEntry(List<RequisitionsSellEntry> entries, EntityUid entity, out RequisitionsSellEntry entry)
    {
        entry = default!;
        if (entries.Count == 0)
            return false;

        var key = MatchKey(entity);
        if (key == null)
            return false;

        foreach (var candidate in entries)
        {
            if (candidate.Item.Id == key)
            {
                entry = candidate;
                return true;
            }
        }

        return false;
    }

    private string? MatchKey(EntityUid entity)
    {
        if (TryComp(entity, out StackComponent? stack))
            return stack.StackTypeId;

        return MetaData(entity).EntityPrototype?.ID;
    }

    private (string Key, int Units) ResolveStorageUnit(EntProtoId proto)
    {
        if (_prototypeManager.TryIndex(proto, out var entProto) &&
            entProto.TryGetComponent<StackComponent>(out var stack) &&
            !string.IsNullOrEmpty(stack.StackTypeId) &&
            _prototypeManager.TryIndex<StackPrototype>(stack.StackTypeId, out var stackProto))
        {
            return (stackProto.Spawn, stack.Count);
        }

        return (proto.Id, 1);
    }

    private bool IsCrateProto(string proto)
    {
        return _prototypeManager.TryIndex<EntityPrototype>(proto, out var entProto) &&
               entProto.TryGetComponent<EntityStorageComponent>(out _);
    }

    private bool AddToStorage(RequisitionsAccountComponent account, string key, int units)
    {
        if (units <= 0)
            return false;

        var current = account.Storage.GetValueOrDefault(key);
        var room = account.StorageLimit - current;
        if (room <= 0)
            return true;

        var added = Math.Min(units, room);
        account.Storage[key] = current + added;
        return added < units;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var time = _timing.CurTime;
        var updateUI = false;
        var accounts = EntityQueryEnumerator<RequisitionsAccountComponent>();
        while (accounts.MoveNext(out var uid, out var account))
        {
            if (account.Gain > 0 && time > account.NextGain)
            {
                account.NextGain = time + account.GainEvery;
                account.Balance += account.Gain;
                Dirty(uid, account);

                updateUI = true;
            }

            if (ProcessRandomRequestSlots((uid, account), time))
            {
                Dirty(uid, account);
                updateUI = true;
            }
        }

        var elevators = EntityQueryEnumerator<RequisitionsElevatorComponent>();
        while (elevators.MoveNext(out var uid, out var elevator))
        {
            if (ProcessElevator((uid, elevator)))
                updateUI = true;

            if (!_chasmQuery.TryComp(uid, out var chasm))
                continue;

            if (time < elevator.NextChasmCheck)
                continue;

            elevator.NextChasmCheck = time + elevator.ChasmCheckEvery;

            if (_net.IsClient)
                continue;

            if (elevator.Mode != Raised && elevator.Mode != Preparing)
            {
                _toPit.Clear();
                _lookup.GetEntitiesInRange(uid.ToCoordinates(), elevator.Radius + 0.25f, _toPit);

                foreach (var toPit in _toPit)
                {
                    if (_chasmFallingQuery.HasComp(toPit))
                        continue;

                    _chasm.StartFalling(uid, chasm, toPit);
                    _audio.PlayEntity(chasm.FallingSound, toPit, uid);
                }
            }
        }

        if (updateUI)
            SendUIStateAll();
    }

    private bool ProcessElevator(Entity<RequisitionsElevatorComponent> ent)
    {
        var time = _timing.CurTime;
        var elevator = ent.Comp;
        if (time > elevator.ToggledAt + elevator.ToggleDelay)
        {
            elevator.ToggledAt = null;
            elevator.Busy = false;
            Dirty(ent);
            SendUIStateAll();
            return false;
        }

        if (elevator.ToggledAt == null)
            return false;

        TryPlayAudio(ent);

        var delay = elevator.NextMode == Raising ? elevator.RaiseDelay : elevator.LowerDelay;
        if (elevator.Mode == Preparing &&
            elevator.NextMode != null &&
            time > elevator.ToggledAt + delay)
        {
            SetMode(ent, elevator.NextMode.Value, null);
            return false;
        }

        if (elevator.Mode != Lowering && elevator.Mode != Raising)
            return false;

        var startDelay = delay + elevator.NextMode switch
        {
            Lowering => elevator.LowerDelay,
            Raising => elevator.RaiseDelay,
            _ => TimeSpan.Zero,
        };

        var moveDelay = startDelay + elevator.Mode switch
        {
            Lowering => elevator.LowerDelay,
            Raising => elevator.RaiseDelay,
            _ => TimeSpan.Zero,
        };

        if (time > elevator.ToggledAt + moveDelay)
        {
            elevator.Audio = null;

            var mode = elevator.Mode switch
            {
                Raising => Raised,
                Lowering => Lowered,
                _ => elevator.Mode,
            };
            SetMode(ent, mode, elevator.NextMode);

            SpawnOrders(ent);

            return true;
        }

        if (elevator.Mode == Lowering &&
            time > elevator.ToggledAt + delay)
        {
            if (Sell(ent))
                return true;
        }

        return false;
    }
}
