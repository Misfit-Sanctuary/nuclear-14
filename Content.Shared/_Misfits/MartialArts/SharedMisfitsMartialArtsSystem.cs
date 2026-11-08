// #Misfits Add - Shared martial arts system: combo input buffer, matching engine, and Hug detection
// Combo engine concept (ring-buffer input tracking, tail-sequence matching) inspired by
// Goob-Station's MartialArts system — https://github.com/Goob-Station/Goob-Station (AGPL-3.0).
// All game content, styles, architecture, and implementation are original to Misfits Sanctuary.
using System.Diagnostics.CodeAnalysis;
using Content.Shared.Damage;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Components;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
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
    [Dependency] private readonly IRobustRandom _random = default!;

    public override void Initialize()
    {
        base.Initialize();

        // The combo input arrives as a directed local event on the performer
        SubscribeLocalEvent<CanPerformComboComponent, MisfitsComboAttackPerformedEvent>(OnComboAttackPerformed);

        // Empty-hand interaction → Hug input type for styles like ShadowStrike
        SubscribeLocalEvent<CanPerformComboComponent, InteractHandEvent>(OnInteractHand);

        // Client-side combo widget query — copy the ring buffer into the event for the overlay
        SubscribeLocalEvent<CanPerformComboComponent, GetPerformedAttackTypesEvent>(OnGetPerformedAttackTypes);

        // #Misfits Fix - style passives: the melee damage event is raised on the user too, so the
        // art's unarmed damage bonus finally applies (baseDamageModifier was previously dead data).
        SubscribeLocalEvent<MartialArtsKnowledgeComponent, GetMeleeDamageEvent>(OnGetMeleeDamage);
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

        // #Misfits Fix - only record a Hug input when one of the entity's combos actually uses it.
        // Every friendly empty-hand click on another player used to push a Hug into the buffer, so
        // the combo widget drew a stray icon at the cursor for styles with no Hug combo at all.
        if (!AnyComboUses(comp, MisfitsComboAttackType.Hug))
            return;

        // Emit a Hug combo event so style systems can intercept it (e.g. ShadowStrike neck grab)
        var ev = new MisfitsComboAttackPerformedEvent(uid, args.Target, uid, MisfitsComboAttackType.Hug);
        RaiseLocalEvent(uid, ev);
    }

    /// <summary>
    /// Copies the current combo attack history into the event for the client-side combo widget overlay.
    /// </summary>
    private void OnGetPerformedAttackTypes(EntityUid uid, CanPerformComboComponent comp, ref GetPerformedAttackTypesEvent args)
    {
        // #Misfits Fix - expire the buffer before the widget reads it. The engine only reset the buffer
        // when the next input arrived, so the last icons stayed pinned to the cursor indefinitely.
        if (comp.ResetTime != TimeSpan.Zero && _timing.CurTime > comp.ResetTime)
        {
            comp.LastAttacks.Clear();
            comp.ResetTime = TimeSpan.Zero;
        }

        args.AttackTypes = new(comp.LastAttacks);
    }

    /// <summary>
    /// Returns true when one of the entity's known combos contains the given input type.
    /// </summary>
    private bool AnyComboUses(CanPerformComboComponent comp, MisfitsComboAttackType type)
    {
        foreach (var comboId in comp.AllowedCombos)
        {
            if (_proto.TryIndex(comboId, out var combo) && combo.AttackTypes.Contains(type))
                return true;
        }

        return false;
    }

    // ---- Style passives ----

    /// Adds the style's unarmed damage bonus. The melee damage event is raised on the weapon and, for
    /// armed attacks, again on the user, so this only fires for the unarmed case (weapon == user).
    private void OnGetMeleeDamage(EntityUid uid, MartialArtsKnowledgeComponent comp, ref GetMeleeDamageEvent args)
    {
        if (args.Weapon != args.User || comp.Blocked)
            return;

        if (!TryGetStyle(comp.MartialArtsForm, out var style) || style.BaseDamageModifier <= 0)
            return;

        var bonus = style.BaseDamageModifier;
        if (style.RandomDamageModifier)
            bonus += _random.Next(style.MinRandomDamageModifier, style.MaxRandomDamageModifier + 1);

        if (bonus <= 0)
            return;

        var damage = new DamageSpecifier();
        damage.DamageDict.Add(style.DamageModifierType, bonus);
        args.Damage += damage;
    }

    /// Finds the style prototype that matches a form value.
    public bool TryGetStyle(MisfitsMartialArtsForms form, [NotNullWhen(true)] out MisfitsMartialArtPrototype? style)
    {
        foreach (var proto in _proto.EnumeratePrototypes<MisfitsMartialArtPrototype>())
        {
            if (proto.MartialArtsForm != form)
                continue;

            style = proto;
            return true;
        }

        style = null;
        return false;
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
