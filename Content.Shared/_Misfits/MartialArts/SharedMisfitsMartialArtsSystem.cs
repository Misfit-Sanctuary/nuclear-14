// #Misfits Add - Shared martial arts system: combo input buffer, matching engine, and Hug detection
// Combo engine concept (ring-buffer input tracking, tail-sequence matching) inspired by
// Goob-Station's MartialArts system — https://github.com/Goob-Station/Goob-Station (AGPL-3.0).
// All game content, styles, architecture, and implementation are original to Misfits Sanctuary.
using Content.Shared.Interaction;
using Content.Shared.Mobs.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Shared._Misfits.MartialArts;

/// <summary>
/// Shared system for the martial arts combo engine.
/// Tracks attack history and fires combo events when a matching sequence is detected.
/// Also routes InteractHandEvent as a Hug for relevant combo styles.
/// </summary>
public sealed class SharedMisfitsMartialArtsSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;

    public override void Initialize()
    {
        base.Initialize();

        // The combo input arrives as a directed local event on the performer
        SubscribeLocalEvent<CanPerformComboComponent, MisfitsComboAttackPerformedEvent>(OnComboAttackPerformed);

        // Empty-hand interaction → Hug input type for styles like ShadowStrike
        SubscribeLocalEvent<CanPerformComboComponent, InteractHandEvent>(OnInteractHand);
    }

    // ---- Combo buffer management ----

    private void OnComboAttackPerformed(EntityUid uid, CanPerformComboComponent comp, MisfitsComboAttackPerformedEvent args)
    {
        var now = _timing.CurTime;

        // Reset buffer if the target changed
        if (comp.CurrentTarget != args.Target)
        {
            comp.LastAttacks.Clear();
            comp.CurrentTarget = args.Target;
        }

        // Reset buffer if the combo window expired
        if (comp.ResetTime != TimeSpan.Zero && now > comp.ResetTime)
            comp.LastAttacks.Clear();

        // Only count fist attacks for the combo engine (weapon != user means a weapon item hit, skip)
        if (args.Weapon != uid && args.Type != MisfitsComboAttackType.Grab && args.Type != MisfitsComboAttackType.Hug)
        {
            comp.LastAttacks.Clear();
            return;
        }

        // Push to ring buffer
        comp.LastAttacks.Add(args.Type);
        if (comp.LastAttacks.Count > comp.LastAttacksLimit)
            comp.LastAttacks.RemoveAt(0);

        // Extend the combo window
        comp.ResetTime = now + TimeSpan.FromSeconds(comp.ComboWindowSeconds);

        Dirty(uid, comp);

        // Check for a matching combo
        CheckCombo(uid, comp, args.Target, args.Weapon);
    }

    private void OnInteractHand(EntityUid uid, CanPerformComboComponent comp, InteractHandEvent args)
    {
        if (args.User != uid)
            return;

        // Emit a Hug combo event so style systems can intercept it (e.g. ShadowStrike neck grab)
        var ev = new MisfitsComboAttackPerformedEvent(uid, args.Target, uid, MisfitsComboAttackType.Hug);
        RaiseLocalEvent(uid, ev);
    }

    // ---- Combo matching ----

    private void CheckCombo(EntityUid uid, CanPerformComboComponent comp, EntityUid target, EntityUid weapon)
    {
        foreach (var comboId in comp.AllowedCombos)
        {
            if (!_proto.TryIndex(comboId, out var combo))
                continue;

            if (!TailMatches(comp.LastAttacks, combo.AttackTypes))
                continue;

            // Check optional constraint: performer must not be prone (unless combo allows it)
            if (!combo.CanDoWhileProne && TryComp<MobStateComponent>(uid, out _))
            {
                // TODO: Check if prone via standing state when needed
            }

            // Combo matched! Clear buffer and fire the result event
            comp.LastAttacks.Clear();
            comp.BeingPerformed = combo.ID;
            Dirty(uid, comp);

            var resultEv = new MisfitsComboTriggeredEvent(uid, target, weapon, combo);
            RaiseLocalEvent(uid, resultEv);
            break;
        }
    }

    /// <summary>
    /// Returns true if the tail of <paramref name="history"/> exactly matches <paramref name="pattern"/>.
    /// </summary>
    private static bool TailMatches(List<MisfitsComboAttackType> history, List<MisfitsComboAttackType> pattern)
    {
        if (pattern.Count == 0 || history.Count < pattern.Count)
            return false;

        var offset = history.Count - pattern.Count;
        for (var i = 0; i < pattern.Count; i++)
        {
            if (history[offset + i] != pattern[i])
                return false;
        }

        return true;
    }

    // ---- Public helper ----

    /// <summary>
    /// Loads the allowed combos for a form from the given combo list prototype onto an entity.
    /// </summary>
    public void LoadCombos(EntityUid uid, CanPerformComboComponent comp, MisfitsComboListPrototype comboList)
    {
        comp.AllowedCombos.Clear();
        comp.AllowedCombos.AddRange(comboList.Combos);
        Dirty(uid, comp);
    }
}

/// <summary>
/// Raised as a directed local event on the performer when a full combo sequence is matched.
/// Style systems subscribe to this to execute the actual combo effect.
/// </summary>
public sealed class MisfitsComboTriggeredEvent : EntityEventArgs
{
    public EntityUid Performer { get; }
    public EntityUid Target { get; }
    public EntityUid Weapon { get; }
    public MisfitsComboPrototype Combo { get; }

    public MisfitsComboTriggeredEvent(EntityUid performer, EntityUid target, EntityUid weapon, MisfitsComboPrototype combo)
    {
        Performer = performer;
        Target = target;
        Weapon = weapon;
        Combo = combo;
    }
}
