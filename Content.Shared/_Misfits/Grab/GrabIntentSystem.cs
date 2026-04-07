// #Misfits Add - Core shared grab stage logic layered on top of the existing pull system
using Content.Shared._Misfits.MartialArts;
using Content.Shared.CombatMode;
using Content.Shared.Contests;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Hands;
using Content.Shared.Inventory.VirtualItem;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Events;
using Content.Shared.Movement.Systems;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;

namespace Content.Shared._Misfits.Grab;

/// <summary>
/// Handles grab stage escalation on top of the pull system.
/// Grabs are initiated when a puller attacks their pulled target while in combat mode.
/// Grabs escalate from Soft → Hard → Suffocate and apply increasing
/// movement penalties and disable the grabbed entity's actions.
/// </summary>
public sealed class GrabIntentSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly ContestsSystem _contests = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _speedMod = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedCombatModeSystem _combatMode = default!;
    [Dependency] private readonly SharedVirtualItemSystem _virtualItem = default!;
    [Dependency] private readonly StaminaSystem _stamina = default!;

    public override void Initialize()
    {
        base.Initialize();

        // Listen for grab attempts raised by the melee system when puller attacks their pull target
        SubscribeLocalEvent<GrabbableComponent, GrabAttemptEvent>(OnGrabAttempt);

        // When the pull stops (puller releases or pull breaks), clear grab state
        SubscribeLocalEvent<PullableComponent, PullStoppedMessage>(OnPullStopped);

        // Virtual item shutdown means the grab hold item was dropped → lower grab stage
        SubscribeLocalEvent<GrabIntentComponent, VirtualItemDeletedEvent>(OnVirtualItemDeleted);

        // Puller moving speed refresh hook
        SubscribeLocalEvent<GrabIntentComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshMoveSpeed);
    }

    // ---- Primary grab escalation ----

    /// <summary>
    /// Attempts to escalate the grab on the target. Called when the puller attacks the entity they are pulling.
    /// </summary>
    private void OnGrabAttempt(EntityUid uid, GrabbableComponent grabbable, ref GrabAttemptEvent args)
    {
        var puller = args.Puller;

        // Must be in combat mode to escalate the grab
        if (!_combatMode.IsInCombatMode(puller))
            return;

        // Must actually be pulling this entity
        if (!TryComp<PullerComponent>(puller, out var pullerComp) || pullerComp.Pulling != uid)
            return;

        // Ensure puller has a GrabIntentComponent
        if (!EnsureComp<GrabIntentComponent>(puller, out var grabComp))
        {
            // freshly added; initialise stage to No
            grabComp.GrabStage = GrabStage.No;
        }

        var result = TryGrab(puller, grabComp, uid, grabbable);
        if (result == GrabAttemptResult.Succeeded)
        {
            // Signal the melee system to NOT do a normal light attack
            args.Cancel();
        }
    }

    /// <summary>
    /// Core grab logic. Escalates the grab stage or applies suffocate-stage stamina damage instead.
    /// </summary>
    public GrabAttemptResult TryGrab(EntityUid puller, GrabIntentComponent grabComp, EntityUid target, GrabbableComponent? grabbable = null)
    {
        if (!Resolve(target, ref grabbable, false))
            return GrabAttemptResult.Failed;

        var now = _timing.CurTime;

        if (grabComp.NextStageChange > now)
            return GrabAttemptResult.OnCooldown;

        grabComp.NextStageChange = now + grabComp.StageChangeCooldown;

        // At Suffocate, deal stamina damage instead of escalating further
        if (grabComp.GrabStage == GrabStage.Suffocate)
        {
            _stamina.TakeStaminaDamage(target, grabComp.SuffocateGrabStaminaDamage, source: puller);
            _audio.PlayPredicted(grabComp.SuffocateSound, puller, puller);
            return GrabAttemptResult.Succeeded;
        }

        // Calculate next stage
        var newStage = (GrabStage)((int)grabComp.GrabStage + 1);

        // Allow martial arts to override starting stage (e.g. skip straight to Hard)
        var overrideEv = new CheckGrabOverridesEvent(puller, target, newStage);
        RaiseLocalEvent(target, ref overrideEv);
        if (overrideEv.OverrideStage.HasValue)
            newStage = overrideEv.OverrideStage.Value;

        TrySetGrabStages(puller, grabComp, target, grabbable, newStage);

        return GrabAttemptResult.Succeeded;
    }

    /// <summary>
    /// Sets grab stage on both puller and pulled, applies speed modifiers and virtual items.
    /// Also raises ComboAttackPerformedEvent(Grab) for the martial arts combo engine.
    /// </summary>
    public void TrySetGrabStages(EntityUid puller, GrabIntentComponent grabComp, EntityUid target, GrabbableComponent grabbable, GrabStage newStage)
    {
        var oldStage = grabComp.GrabStage;

        grabComp.GrabStage = newStage;
        grabbable.GrabStage = newStage;

        // Update escape chance for the grabbed entity
        if (grabComp.EscapeChances.TryGetValue(newStage, out var baseEscape))
        {
            // Modify by mass ratio: heavier target escapes easier
            var massRatio = _contests.MassContest(target);
            grabbable.GrabEscapeChance = Math.Clamp(baseEscape * massRatio, 0.05f, 1f);
        }

        Dirty(puller, grabComp);
        Dirty(target, grabbable);

        // Apply/refresh speed modifiers on puller
        _speedMod.RefreshMovementSpeedModifiers(puller);

        // Manage virtual items (spawned at Suffocate to occupy puller hands)
        UpdateVirtualItems(puller, grabComp, target, oldStage, newStage);

        // Play grab audio
        _audio.PlayPredicted(grabComp.GrabSound, puller, puller);

        var stageChangedEv = new GrabStageChangedEvent(puller, target, oldStage, newStage);
        RaiseLocalEvent(puller, stageChangedEv);

        // Raise combo event so martial arts engine can process Grab-type input
        if (HasComp<Content.Shared._Misfits.MartialArts.CanPerformComboComponent>(puller))
        {
            RaiseLocalEvent(puller, new MisfitsComboAttackPerformedEvent(puller, target, puller, MisfitsComboAttackType.Grab));
        }
    }

    /// <summary>
    /// Handles refresh speed modifier event — applies grab speed penalty to the puller.
    /// </summary>
    private void OnRefreshMoveSpeed(EntityUid uid, GrabIntentComponent comp, RefreshMovementSpeedModifiersEvent args)
    {
        if (comp.GrabStage == GrabStage.No)
            return;

        if (comp.GrabberSpeedModifiers.TryGetValue(comp.GrabStage, out var mod))
            args.ModifySpeed(mod, mod);
    }

    // ---- Grab release ----

    /// <summary>
    /// Lowers the grab stage by one. Called when the puller drops out of combat mode with a grabbed entity.
    /// </summary>
    public void TryLowerGrabStage(EntityUid puller, GrabIntentComponent grabComp, EntityUid target)
    {
        if (!TryComp<GrabbableComponent>(target, out var grabbable))
            return;

        if (grabComp.GrabStage == GrabStage.No)
            return;

        var newStage = (GrabStage)Math.Max(0, (int)grabComp.GrabStage - 1);
        TrySetGrabStages(puller, grabComp, target, grabbable, newStage);
    }

    /// <summary>
    /// Force-sets the grab stage to a specific value, bypassing cooldowns.
    /// Used by martial arts combo effects (e.g. ShadowStrike instant neck grab).
    /// Silently returns if the puller is not currently grabbing the target.
    /// </summary>
    public void TryForceGrabStage(EntityUid puller, EntityUid target, GrabStage stage)
    {
        if (!TryComp<GrabIntentComponent>(puller, out var grabComp))
            return;

        if (!TryComp<GrabbableComponent>(target, out var grabbable))
            return;

        // Only applies if this puller is currently grabbing this target
        if (!TryComp<PullableComponent>(target, out var pullable) || pullable.Puller != puller)
            return;

        TrySetGrabStages(puller, grabComp, target, grabbable, stage);
    }

    /// <summary>
    /// Fully releases the grab, returning both entities to GrabStage.No.
    /// </summary>
    public void ReleaseGrab(EntityUid puller, GrabIntentComponent grabComp, EntityUid target)
    {
        if (grabComp.GrabStage == GrabStage.No)
        {
            RemCompDeferred<GrabIntentComponent>(puller);
            return;
        }

        if (!TryComp<GrabbableComponent>(target, out var grabbable))
        {
            RemCompDeferred<GrabIntentComponent>(puller);
            return;
        }

        var wasStage = grabComp.GrabStage;
        grabComp.GrabStage = GrabStage.No;
        grabbable.GrabStage = GrabStage.No;

        Dirty(puller, grabComp);
        Dirty(target, grabbable);

        // Remove virtual items
        ClearVirtualItems(puller, grabComp);

        _speedMod.RefreshMovementSpeedModifiers(puller);

        var ev = new GrabReleasedEvent(puller, target, wasStage);
        RaiseLocalEvent(puller, ev);
        RaiseLocalEvent(target, ev);

        RemCompDeferred<GrabIntentComponent>(puller);
    }

    // ---- Pull stopped ----

    /// <summary>
    /// When the pull relationship ends (any reason), clear grab state.
    /// </summary>
    private void OnPullStopped(EntityUid uid, PullableComponent pullable, PullStoppedMessage args)
    {
        var puller = args.PullerUid;
        if (!TryComp<GrabIntentComponent>(puller, out var grabComp))
            return;

        ReleaseGrab(puller, grabComp, uid);
    }

    // ---- Virtual item management ----

    private void UpdateVirtualItems(EntityUid puller, GrabIntentComponent grabComp, EntityUid target, GrabStage oldStage, GrabStage newStage)
    {
        var newCount = grabComp.GrabVirtualItemStageCount.GetValueOrDefault(newStage, 0);
        var currentCount = grabComp.SpawnedVirtualItems.Count;

        // Spawn more virtual items if needed
        while (currentCount < newCount)
        {
            if (_virtualItem.TrySpawnVirtualItemInHand(target, puller, out var virtItem))
            {
                grabComp.SpawnedVirtualItems.Add(virtItem.Value);
                currentCount++;
            }
            else
            {
                break; // No free hands
            }
        }

        // Remove virtual items if stage lowered
        while (grabComp.SpawnedVirtualItems.Count > newCount)
        {
            var last = grabComp.SpawnedVirtualItems[^1];
            grabComp.SpawnedVirtualItems.RemoveAt(grabComp.SpawnedVirtualItems.Count - 1);
            if (TryComp<VirtualItemComponent>(last, out var lastVirt))
                _virtualItem.DeleteVirtualItem((last, lastVirt), puller);
        }
    }

    private void ClearVirtualItems(EntityUid puller, GrabIntentComponent grabComp)
    {
        foreach (var vi in grabComp.SpawnedVirtualItems)
        {
            if (TryComp<VirtualItemComponent>(vi, out var virtComp))
                _virtualItem.DeleteVirtualItem((vi, virtComp), puller);
        }
        grabComp.SpawnedVirtualItems.Clear();
    }

    /// <summary>
    /// When a virtual item representing the grab hold is deleted, lower the grab stage.
    /// Fired via VirtualItemDeletedEvent raised by DeleteVirtualItem on the user (puller) entity.
    /// </summary>
    private void OnVirtualItemDeleted(EntityUid uid, GrabIntentComponent grabComp, VirtualItemDeletedEvent args)
    {
        // Only care if the deleted blocking entity is one we spawned (i.e. the grabbed entity)
        // SpawnedVirtualItems tracks the virtual item EntityUid; args.BlockingEntity is the grabbed entity.
        // We need to check if any of our spawned virtual items were deleted.
        // VirtualItemDeletedEvent.BlockingEntity = the entity whose slot was blocked (the grabbed target).
        // We need to instead remove from SpawnedVirtualItems if the deleted VI uid isn't present.
        // The event is raised BEFORE deletion, so we can check current held items against our list.
        // Simplest: clear any destroyed virtual items from the list.
        grabComp.SpawnedVirtualItems.RemoveAll(vi => TerminatingOrDeleted(vi));

        if (grabComp.SpawnedVirtualItems.Count < grabComp.GrabVirtualItemStageCount.GetValueOrDefault(grabComp.GrabStage, 0))
        {
            // A virtual item was removed — lower the grab stage
            if (!TryComp<PullerComponent>(uid, out var pullerComp) || pullerComp.Pulling == null)
                return;

            TryLowerGrabStage(uid, grabComp, pullerComp.Pulling.Value);
        }
    }

    // ---- Grabbed entity escape ----

    /// <summary>
    /// Attempt by the grabbed entity to break free. Called when they try to move at Hard/Suffocate.
    /// </summary>
    public GrabResistResult TryEscape(EntityUid grabbed, GrabbableComponent grabbable)
    {
        var now = _timing.CurTime;

        if (grabbable.NextEscapeAttempt > now)
            return GrabResistResult.TooSoon;

        grabbable.NextEscapeAttempt = now + grabbable.EscapeAttemptCooldown;
        Dirty(grabbed, grabbable);

        // Soft grab: always escape (movement breaks the pull entirely)
        if (grabbable.GrabStage == GrabStage.Soft)
            return GrabResistResult.Succeeded;

        // Seeded RNG for prediction: use entity net ID + current tick
        var seed = (int)(now.Ticks % int.MaxValue) ^ GetNetEntity(grabbed).Id;
        var rng = new System.Random(seed);
        var roll = (float)rng.NextDouble();

        if (roll <= grabbable.GrabEscapeChance * grabbable.EscapeAttemptModifier)
            return GrabResistResult.Succeeded;

        return GrabResistResult.Failed;
    }
}
