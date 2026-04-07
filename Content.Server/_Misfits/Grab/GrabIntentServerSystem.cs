// #Misfits Add - Server-side grab: table slam, suffocation tick, and entity throw
using System.Numerics;
using Content.Server.NPC.Systems;
using Content.Shared._Misfits.Grab;
using Content.Shared.Climbing.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Popups;
using Content.Shared.Stunnable;
using Content.Shared.Throwing;
using Content.Shared.Verbs;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Server._Misfits.Grab;

/// <summary>
/// Server-only logic for the grab stage system:
/// table-slam verb, periodic suffocation damage, and grab throwing mechanics.
/// </summary>
public sealed class GrabIntentServerSystem : EntitySystem
{
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly GrabIntentSystem _grabIntent = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedStunSystem _stun = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly StaminaSystem _stamina = default!;
    [Dependency] private readonly ThrowingSystem _throwing = default!;

    // Suffocation tick interval in seconds
    private const float SuffocationDamageInterval = 2f;
    private float _suffocationTimer;

    public override void Initialize()
    {
        base.Initialize();

        // Table slam verb on the puller when they have a Hard+ grab
        SubscribeLocalEvent<GrabIntentComponent, GetVerbsEvent<Verb>>(AddTableSlamVerb);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _suffocationTimer += frameTime;
        if (_suffocationTimer < SuffocationDamageInterval)
            return;

        _suffocationTimer = 0f;

        // Apply periodic brute damage (neck compression) at Suffocate level
        var query = EntityQueryEnumerator<GrabIntentComponent, PullerComponent>();
        while (query.MoveNext(out var uid, out var grabComp, out var pullerComp))
        {
            if (grabComp.GrabStage != GrabStage.Suffocate || pullerComp.Pulling == null)
                continue;

            var target = pullerComp.Pulling.Value;
            if (!TryComp<GrabbableComponent>(target, out _))
                continue;

            // Deal brute damage to neck — simulating choke
            var chokeSpecs = new DamageSpecifier();
            chokeSpecs.DamageDict.Add("Blunt", 2);
            _damageable.TryChangeDamage(target, chokeSpecs, origin: uid);
        }
    }

    // ---- Table slam ----

    private void AddTableSlamVerb(EntityUid uid, GrabIntentComponent grabComp, GetVerbsEvent<Verb> args)
    {
        // Must have at least Hard grab
        if (grabComp.GrabStage < GrabStage.Hard)
            return;

        if (!TryComp<PullerComponent>(uid, out var pullerComp) || pullerComp.Pulling == null)
            return;

        var target = pullerComp.Pulling.Value;
        var user = args.User;

        if (user != uid)
            return;

        // Check for a climbable (table) within range
        if (!TryFindTableInRange(uid, grabComp.TableSlamRange, out var tableUid))
            return;

        if (_timing.CurTime < grabComp.NextTableSlam)
            return;

        var tableUidValue = tableUid!.Value; // captured before lambda to avoid nullable warning
        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString("grab-table-slam-verb"),
            Priority = 3,
            Act = () => TrySlamTable(uid, grabComp, target, tableUidValue),
        });
    }

    private bool TryFindTableInRange(EntityUid puller, float range, out EntityUid? tableUid)
    {
        tableUid = null;
        var pos = _transform.GetMapCoordinates(puller);
        var nearby = _lookup.GetEntitiesInRange(pos, range);

        foreach (var ent in nearby)
        {
            if (HasComp<ClimbableComponent>(ent))
            {
                tableUid = ent;
                return true;
            }
        }

        return false;
    }

    private void TrySlamTable(EntityUid puller, GrabIntentComponent grabComp, EntityUid target, EntityUid table)
    {
        grabComp.NextTableSlam = _timing.CurTime + TimeSpan.FromSeconds(grabComp.TableSlamCooldown);

        if (TryComp<GrabbableComponent>(target, out var grabbable))
            grabbable.BeingTabled = true;

        // Deal blunt and stamina damage
        var slamDamage = new DamageSpecifier();
        if (TryComp<GrabbableComponent>(target, out var gc))
        {
            slamDamage.DamageDict.Add("Blunt", gc.TabledDamage);
            _stamina.TakeStaminaDamage(target, gc.TabledStaminaDamage, source: puller);
        }
        else
        {
            slamDamage.DamageDict.Add("Blunt", 5);
            _stamina.TakeStaminaDamage(target, 40f, source: puller);
        }

        _damageable.TryChangeDamage(target, slamDamage, origin: puller);

        // Knock them down briefly
        var stunDuration = TryComp<GrabbableComponent>(target, out var grabbableComp)
            ? TimeSpan.FromSeconds(grabbableComp.PostTabledDuration)
            : TimeSpan.FromSeconds(3f);

        _stun.TryKnockdown(target, stunDuration, true);

        _popup.PopupEntity(Loc.GetString("grab-table-slam-pop-puller", ("target", target)), puller, puller);
        _popup.PopupEntity(Loc.GetString("grab-table-slam-pop-target", ("puller", puller)), target, target);

        if (TryComp<GrabbableComponent>(target, out var g2))
            g2.BeingTabled = false;
    }

    // ---- Throw grabbed entity ----

    /// <summary>
    /// Throws the grabbed entity in the given direction. Called from GrabIntentComponent context.
    /// The throw is triggered by the puller dropping/throwing the virtual item while at Hard+ stage.
    /// </summary>
    public void ThrowGrabbedEntity(EntityUid puller, GrabIntentComponent grabComp, EntityUid target, Vector2 direction)
    {
        if (grabComp.GrabStage < GrabStage.Hard)
            return;

        // Calculate total throw damage
        var throwDamage = new DamageSpecifier();
        throwDamage.DamageDict.Add(grabComp.GrabThrowDamageModifier >= 1
            ? "Blunt"
            : "Blunt",
            (double)(grabComp.GrabThrowDamage * grabComp.GrabThrowDamageModifier));

        // Deal damage to target when thrown
        _damageable.TryChangeDamage(target, throwDamage, origin: puller);

        // Release the grab before throwing
        _grabIntent.ReleaseGrab(puller, grabComp, target);

        // Throw the entity
        _throwing.TryThrow(target, direction, grabComp.GrabThrownSpeed, puller);
    }
}
