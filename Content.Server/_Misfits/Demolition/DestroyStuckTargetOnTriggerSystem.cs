using Content.Server.Destructible;
using Content.Server.Explosion.EntitySystems;
using Content.Server.Sticky.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;

namespace Content.Server._Misfits.Demolition;

/// <summary>
/// Converts a planted C4/Hawkins detonation into guaranteed destruction of its
/// attached target, while respecting objects intentionally configured as indestructible.
/// </summary>
public sealed class DestroyStuckTargetOnTriggerSystem : EntitySystem
{
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly DestructibleSystem _destructible = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<DestroyStuckTargetOnTriggerComponent, TriggerEvent>(OnTriggered);
    }

    private void OnTriggered(EntityUid uid, DestroyStuckTargetOnTriggerComponent component, TriggerEvent args)
    {
        if (!TryComp<StickyComponent>(uid, out var sticky) ||
            sticky.StuckTo is not { } target ||
            !TryComp<DestructibleComponent>(target, out var destructible))
        {
            return;
        }

        // DestroyedAt returns MaxValue when the target has no destruction/breakage
        // threshold. That is the immunity path used by Vault doors and similar objects.
        var threshold = _destructible.DestroyedAt(target, destructible);
        if (threshold == FixedPoint2.MaxValue ||
            threshold >= FixedPoint2.New(component.MaximumDestructibleThreshold))
            return;

        var structural = _prototypes.Index<DamageTypePrototype>("Structural");
        _damageable.TryChangeDamage(
            target,
            new DamageSpecifier(structural, threshold),
            ignoreResistances: true,
            origin: uid);
    }
}
