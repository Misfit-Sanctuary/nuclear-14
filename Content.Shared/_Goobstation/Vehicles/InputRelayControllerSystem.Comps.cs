using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory.VirtualItem;


namespace Content.Shared.Vehicles;

public sealed partial class InputRelayControllerSystem : EntitySystem
{
    [Dependency] private SharedVirtualItemSystem _virtualItem = default!;
    [Dependency] private SharedHandsSystem _handsSystem = default!;
    [Dependency] private INetManager _net = default!;
    private void CompListeners()
    {
        SubscribeLocalEvent<RequireFreeHandComponent, InputRelayTrySetEv>(OnTryFreeHands);
        SubscribeLocalEvent<RequireFreeHandComponent, InputRelaySetEv>(OnSetRelay);
        SubscribeLocalEvent<RequireFreeHandComponent, InputRelayRemovedEv>(OnRemoveRelay);
        SubscribeLocalEvent<EngineNeedsFuelComponent, InputRelayTrySetEv>(OnTryEngineFuelOn);
    }

    private void OnTryEngineFuelOn(Entity<EngineNeedsFuelComponent> ent, ref InputRelayTrySetEv args)
    {
        args.Cancel = !HasComp<ActiveSolutionConsumerComponent>(ent);
    }

    private void OnTryFreeHands(Entity<RequireFreeHandComponent> ent, ref InputRelayTrySetEv ev)
    {
        if (TryComp<HandsComponent>(ent, out var handComp) &&
            handComp.CountFreeHands() >= ent.Comp.RequiredHands)
            return;
        ev.Cancel = true;
    }

    private void OnSetRelay(Entity<RequireFreeHandComponent> ent, ref InputRelaySetEv ev)
    {
        if (_net.IsClient)
            return;

        var hands = Comp<HandsComponent>(ev.Driver).GetFreeHands();

        var handsCounted = 0;
        foreach (var hand in hands)
        {
            if (!_virtualItem.TrySpawnVirtualItem(ent, ev.Driver, out var virtualItem))
                throw new InvalidOperationException($"virtual item failed to spawn for {this}");

            _handsSystem.DoPickup(ev.Driver, hand, virtualItem.Value);
            handsCounted++;
            if (handsCounted == ent.Comp.RequiredHands)
                break;
        }

        if (handsCounted != ent.Comp.RequiredHands)
            throw new InvalidOperationException($"counted hands dont equal required hands in {this}");
    }
    private void OnRemoveRelay(Entity<RequireFreeHandComponent> ent, ref InputRelayRemovedEv ev)
    {
        if (_net.IsClient)
            return;
        _virtualItem.DeleteInHandsMatching(ev.Driver, ent);
    }
}
