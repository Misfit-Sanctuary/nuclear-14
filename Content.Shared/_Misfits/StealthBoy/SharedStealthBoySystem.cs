// #Misfits Add - Shared Stealth Boy logic. Handles activation, opacity interpolation,
// and expiry for the Fallout Stealth Boy device. Ported/inspired by RMC-14 stealth system,
// simplified to remove evasion and skill dependencies.
// #Misfits Tweak - Now also tracks long-term "stealth radiation" exposure on the user,
// scaling hallucinations / damage by tier, and grants the Chinese-Stealth-Suit translucent
// shimmer instead of full invisibility (visibility clamped on the StealthComponent).
using Content.Shared.Actions;
using Content.Shared.Interaction.Events;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Stealth;
using Content.Shared.Stealth.Components;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Shared._Misfits.StealthBoy;

public abstract class SharedStealthBoySystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedStealthSystem _stealth = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;

    /// <summary>
    /// Server-only hook called on activation so subclasses can apply addiction
    /// or other side effects without pulling server systems into Shared.
    /// </summary>
    protected virtual void OnStealthBoyActivated(EntityUid user, StealthBoyExposureComponent exposure) { }

    /// <summary>
    /// Server-only hook called when the cloak ends so subclasses can re-evaluate
    /// derived state (e.g. clearing the Paracusia breakdown intensity).
    /// </summary>
    protected virtual void OnStealthBoyDeactivated(EntityUid user) { }

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<StealthBoyComponent, UseInHandEvent>(OnUseInHand);
        SubscribeLocalEvent<StealthBoyComponent, ActivateStealthBoyActionEvent>(OnActivateAction);
        SubscribeLocalEvent<StealthBoyActiveComponent, ComponentShutdown>(OnActiveShutdown);
    }

    private void OnUseInHand(Entity<StealthBoyComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        if (HasComp<StealthBoyActiveComponent>(args.User) || HasComp<StealthComponent>(args.User))
        {
            if (_net.IsServer)
                _popup.PopupEntity("You are already cloaked.", ent, args.User);
            return;
        }

        args.Handled = true;
        Activate(ent, args.User);
    }

    private void OnActivateAction(Entity<StealthBoyComponent> ent, ref ActivateStealthBoyActionEvent args)
    {
        if (args.Handled)
            return;

        if (HasComp<StealthBoyActiveComponent>(args.Performer) || HasComp<StealthComponent>(args.Performer))
        {
            if (_net.IsServer)
                _popup.PopupEntity("You are already cloaked.", ent, args.Performer);
            args.Handled = true;
            return;
        }

        args.Handled = true;
        Activate(ent, args.Performer);
    }

    protected void Activate(Entity<StealthBoyComponent> item, EntityUid user)
    {
        var now = _timing.CurTime;
        ActivateStealth(
            user,
            item.Comp.Duration,
            item.Comp.Visibility,
            item.Comp.FadeInTime,
            item.Comp.FadeOutTime,
            "The Stealth Boy hums and you feel yourself fade from view.",
            "You reappear as the Stealth Boy power fades.");
    }

    /// <summary>
    /// Activates Stealth Boy cloak behavior on an entity without requiring an item.
    /// Used by Nightkin's innate implant path.
    /// </summary>
    public void ActivateStealth(
        EntityUid user,
        TimeSpan duration,
        float visibility,
        TimeSpan fadeInTime,
        TimeSpan fadeOutTime,
        string activateMessage,
        string reappearMessage,
        float? stillVisibility = null)
    {
        var now = _timing.CurTime;
        var active = EnsureComp<StealthBoyActiveComponent>(user);
        active.StartTime = now;
        active.EndTime = now + duration;
        active.TargetVisibility = visibility;
        active.FadeInTime = fadeInTime;
        active.FadeOutTime = fadeOutTime;
        active.ReappearMessage = reappearMessage;
        active.FadingOut = false;
        active.FadeOutStart = TimeSpan.Zero;
        active.FadeInComplete = false;
        active.StillVisibility = stillVisibility ?? visibility;
        Dirty(user, active);

        // Spawn the stealth shader. Clamp MinVisibility to the prototype's target so
        // we keep the translucent "Chinese Stealth Suit" shimmer rather than going invisible.
        var stealth = EnsureComp<StealthComponent>(user);
        stealth.MinVisibility = Math.Min(stealth.MinVisibility, visibility);
        Dirty(user, stealth);
        _stealth.SetEnabled(user, true);
        _stealth.SetVisibility(user, 1f);

        // Track cumulative exposure across activations. The shared HallucinationsComponent
        // is owned by HallucinationsSystem; we just trigger a refresh from the server hook.
        var exposure = EnsureComp<StealthBoyExposureComponent>(user);
        exposure.LastUpdate = now;
        Dirty(user, exposure);

        // Server-only side effects (addiction, hallucination refresh).
        if (_net.IsServer)
        {
            OnStealthBoyActivated(user, exposure);
            _popup.PopupEntity(activateMessage, user, user);
        }
    }

    public bool TryBeginFadeOut(EntityUid user, StealthBoyActiveComponent? active = null)
    {
        if (!Resolve(user, ref active, false))
            return false;

        if (active.FadingOut)
            return false;

        ExitDeepConcealment(user);
        active.FadingOut = true;
        active.FadeOutStart = _timing.CurTime;
        Dirty(user, active);
        return true;
    }

    // Once faded in, let StealthOnMove take over: standing still sinks toward
    // StillVisibility, moving climbs back up. Max capped at the shimmer so
    // running around doesn't fully reveal you mid-cloak.
    private void EnterDeepConcealment(EntityUid uid, StealthBoyActiveComponent active)
    {
        if (active.StillVisibility >= active.TargetVisibility)
            return;

        if (!TryComp<StealthComponent>(uid, out var stealth))
            return;

        stealth.MinVisibility = active.StillVisibility;
        stealth.MaxVisibility = active.TargetVisibility;
        Dirty(uid, stealth);
        EnsureComp<StealthOnMoveComponent>(uid);
    }

    private void ExitDeepConcealment(EntityUid uid)
    {
        if (!HasComp<StealthOnMoveComponent>(uid))
            return;

        RemCompDeferred<StealthOnMoveComponent>(uid);

        // put the cap back or the fade-out can't reach full visibility
        if (TryComp<StealthComponent>(uid, out var stealth))
        {
            stealth.MaxVisibility = 1.5f;
            Dirty(uid, stealth);
        }
    }

    private void OnActiveShutdown(Entity<StealthBoyActiveComponent> ent, ref ComponentShutdown args)
    {
        if (Terminating(ent))
            return;

        // let others (nightkin implant cooldown) know the cloak fully ended.
        // can't subscribe to this shutdown twice so we relay it as our own event
        var ended = new StealthBoyCloakEndedEvent();
        RaiseLocalEvent(ent, ref ended);

        RemCompDeferred<StealthOnMoveComponent>(ent);
        RemCompDeferred<StealthComponent>(ent);
        // Hallucination intensity is reasserted by the server-side OnTierChanged path;
        // exposure stays so it can decay back down naturally.
        if (_net.IsServer)
            OnStealthBoyDeactivated(ent.Owner);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_timing.IsFirstTimePredicted)
            return;

        UpdateActive(frameTime);
        UpdateExposureDecay(frameTime);
    }

    private void UpdateActive(float frameTime)
    {
        var query = EntityQueryEnumerator<StealthBoyActiveComponent>();
        while (query.MoveNext(out var uid, out var active))
        {
            // Accumulate exposure for this user while the cloak is up.
            if (TryComp<StealthBoyExposureComponent>(uid, out var exposure))
            {
                exposure.ExposureSeconds += frameTime;
                exposure.LastUpdate = _timing.CurTime;
                if (UpdateTier((uid, exposure)))
                    Dirty(uid, exposure);
            }

            var now = _timing.CurTime;

            if (!active.FadingOut)
            {
                // Fade in phase: interpolate visibility down to the stealth threshold.
                var fadeElapsed = (now - active.StartTime) / active.FadeInTime;
                var visibility = active.FadeInTime > TimeSpan.Zero
                    ? (float)(1.0 + (active.TargetVisibility - 1.0) * Math.Min(1.0, fadeElapsed))
                    : active.TargetVisibility;

                if (!active.FadeInComplete)
                {
                    SetVisibility(uid, visibility);
                    if (fadeElapsed >= 1.0)
                    {
                        active.FadeInComplete = true;
                        Dirty(uid, active);
                        EnterDeepConcealment(uid, active);
                    }
                }

                if (now >= active.EndTime || _mobState.IsIncapacitated(uid))
                {
                    ExitDeepConcealment(uid);
                    active.FadingOut = true;
                    active.FadeOutStart = now;
                    Dirty(uid, active);
                    continue;
                }
            }
            else
            {
                // Fade out phase: interpolate visibility back to normal.
                var fadeOutElapsed = (now - active.FadeOutStart) / active.FadeOutTime;
                var visibility = active.FadeOutTime > TimeSpan.Zero
                    ? (float)(active.TargetVisibility + (1.0 - active.TargetVisibility) * Math.Min(1.0, fadeOutElapsed))
                    : 1f;

                SetVisibility(uid, visibility);

                if (fadeOutElapsed >= 1.0)
                {
                    _stealth.SetVisibility(uid, 1f);
                    RemCompDeferred<StealthComponent>(uid);
                    RemCompDeferred<StealthBoyActiveComponent>(uid);
                    if (_net.IsServer)
                        _popup.PopupEntity(active.ReappearMessage, uid, uid);
                    continue;
                }
            }
        }
    }

    private void SetVisibility(EntityUid uid, float visibility)
    {
        if (!HasComp<StealthComponent>(uid))
            return;

        _stealth.SetVisibility(uid, visibility);
    }

    /// <summary>
    /// Decays exposure for users that are not currently cloaked. Bounded by
    /// the Without filter so we never iterate the same set of users twice.
    /// </summary>
    private void UpdateExposureDecay(float frameTime)
    {
        // Only run on server — clients don't predict the long-term exposure value.
        if (!_net.IsServer)
            return;

        var query = EntityQueryEnumerator<StealthBoyExposureComponent, MetaDataComponent>();
        while (query.MoveNext(out var uid, out var exposure, out _))
        {
            if (HasComp<StealthBoyActiveComponent>(uid))
                continue;

            if (exposure.ExposureSeconds <= 0f)
            {
                // Fully clear users get the comp removed so the query shrinks again.
                if (exposure.CurrentTier == 0)
                    RemCompDeferred<StealthBoyExposureComponent>(uid);
                continue;
            }

            exposure.ExposureSeconds = Math.Max(0f, exposure.ExposureSeconds - exposure.DecayPerSecond * frameTime);
            if (UpdateTier((uid, exposure)))
                Dirty(uid, exposure);
        }
    }

    /// <summary>
    /// Recompute the cached tier from ExposureSeconds. Override-friendly hook for
    /// the server to send tier-transition popups.
    /// </summary>
    protected bool UpdateTier(Entity<StealthBoyExposureComponent> ent)
    {
        var thresholds = ent.Comp.TierThresholds;
        var newTier = 0;
        for (var i = thresholds.Length - 1; i >= 1; i--)
        {
            if (ent.Comp.ExposureSeconds >= thresholds[i])
            {
                newTier = i;
                break;
            }
        }

        if (newTier == ent.Comp.CurrentTier)
            return false;

        var oldTier = ent.Comp.CurrentTier;
        ent.Comp.CurrentTier = newTier;
        OnTierChanged(ent.Owner, oldTier, newTier);
        return true;
    }

    /// <summary>
    /// Hook for the server to surface tier transitions to the player (popup, alert, etc.).
    /// </summary>
    protected virtual void OnTierChanged(EntityUid user, int oldTier, int newTier) { }
}

/// <summary>
/// Fired when the Stealth Boy hotkey button is pressed so the item can activate from hand or worn slots.
/// </summary>
public sealed partial class ActivateStealthBoyActionEvent : InstantActionEvent;

/// <summary>
/// Raised on the user when their cloak fully ends.
/// </summary>
[ByRefEvent]
public readonly record struct StealthBoyCloakEndedEvent;
