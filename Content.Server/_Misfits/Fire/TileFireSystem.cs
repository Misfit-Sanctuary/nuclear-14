using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Chemistry.Components;
using Content.Shared._Misfits.Fire;
using Content.Shared._RMC.Stubs;
using Content.Shared.Alert;
using Content.Shared.Atmos;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Clothing.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Maps;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage;

namespace Content.Server._Misfits.Fire;

public sealed class TileFireSystem : EntitySystem
{
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly FlammableSystem _flammable = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solution = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private static readonly ProtoId<ReagentPrototype> WaterReagent = "Water";
    private static readonly ProtoId<DamageTypePrototype> HeatDamage = "Heat";

    private EntityQuery<FlammableComponent> _flammableQuery;
    private EntityQuery<InventoryComponent> _inventoryQuery;
    private EntityQuery<SolutionContainerManagerComponent> _solutionManagerQuery;
    private EntityQuery<VaporComponent> _vaporQuery;

    public override void Initialize()
    {
        _flammableQuery = GetEntityQuery<FlammableComponent>();
        _inventoryQuery = GetEntityQuery<InventoryComponent>();
        _solutionManagerQuery = GetEntityQuery<SolutionContainerManagerComponent>();
        _vaporQuery = GetEntityQuery<VaporComponent>();

        SubscribeLocalEvent<TileFireComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(Entity<TileFireComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.SpawnedAt = _timing.CurTime;
        Dirty(ent);
        UpdateVisualStage(ent);
    }

    private void TryExtinguishFromVapor(Entity<TileFireComponent> fire, EntityUid vapor)
    {
        if (!_solutionManagerQuery.TryComp(vapor, out var manager))
            return;

        foreach (var (_, solution) in _solution.EnumerateSolutions((vapor, manager)))
        {
            if (!solution.Comp.Solution.ContainsPrototype(WaterReagent))
                continue;

            if (fire.Comp.ExtinguishInstantly)
            {
                QueueDel(fire);
                return;
            }

            fire.Comp.Duration -= TimeSpan.FromSeconds(fire.Comp.VaporExtinguishSeconds);
            Dirty(fire);
            UpdateVisualStage(fire);
            return;
        }
    }

    private void UpdateVisualStage(Entity<TileFireComponent> fire)
    {
        var elapsed = _timing.CurTime - fire.Comp.SpawnedAt;
        var remaining = fire.Comp.Duration - elapsed;

        if (remaining <= TimeSpan.Zero)
            return;

        var visual = TileFireVisuals.Three;
        if (elapsed < fire.Comp.BigFireDuration)
            visual = TileFireVisuals.Four;
        else if (remaining <= fire.Comp.Duration * 0.33f)
            visual = TileFireVisuals.One;
        else if (remaining <= fire.Comp.Duration * 0.66f)
            visual = TileFireVisuals.Two;

        _appearance.SetData(fire, TileFireLayers.Base, visual);
    }

    private void ProcessTouch(Entity<TileFireComponent> fire, EntityUid target)
    {
        var touch = EnsureComp<TileFireTouchComponent>(target);
        if (_timing.CurTime < touch.NextTouchAt)
            return;

        touch.NextTouchAt = _timing.CurTime + fire.Comp.TouchCooldown;
        Dirty(target, touch);

        if (_flammableQuery.TryComp(target, out var flammable))
        {
            _flammable.AdjustFireStacks(target, fire.Comp.FireStacks, flammable);
            _flammable.Ignite(target, fire.Owner, flammable);
        }

        if (fire.Comp.StandingDamage <= 0)
            return;

        var damage = fire.Comp.StandingDamage;
        if (_flammableQuery.TryComp(target, out var burnable) && !burnable.IgnoreFireProtection)
        {
            var ev = new GetFireProtectionEvent();
            RaiseLocalEvent(target, ref ev);

            if (_inventoryQuery.TryComp(target, out var inventory))
                _inventory.RelayEvent((target, inventory), ref ev);

            damage *= ev.Multiplier;
        }

        if (damage <= 0)
            return;

        var spec = new DamageSpecifier(_prototype.Index(HeatDamage), damage);
        _damageable.TryChangeDamage(target, spec, interruptsDoAfters: false);
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<TileFireComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var fire, out var xform))
        {
            var elapsed = _timing.CurTime - fire.SpawnedAt;
            if (elapsed >= fire.Duration)
            {
                QueueDel(uid);
                continue;
            }

            UpdateVisualStage((uid, fire));

            foreach (var entity in xform.Coordinates.GetEntitiesInTile(LookupFlags.Dynamic, _lookup))
            {
                if (entity == uid)
                    continue;

                if (_vaporQuery.HasComp(entity))
                {
                    TryExtinguishFromVapor((uid, fire), entity);
                    continue;
                }

                ProcessTouch((uid, fire), entity);
            }
        }
    }
}
