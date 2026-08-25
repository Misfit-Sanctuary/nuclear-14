// #Misfits Add - arm/disarm verb cycle, armed-gate on step trigger, appearance sync, ambient beep, unanchor-block, knockdown, thrown-item trigger
using Content.Server.DoAfter;
using Content.Server.Explosion.EntitySystems;
using Content.Shared._Misfits.LandMines;
using Content.Shared._Misfits.Special;
using Robust.Shared.GameObjects;
using Content.Shared.Audio;
using Content.Shared.Construction.Components;
using Content.Shared.DoAfter;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Popups;
using Content.Shared.StepTrigger.Systems;
using Content.Shared.Stunnable;
using Content.Shared.Throwing;
using Content.Shared.Verbs;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map.Components;
using Robust.Shared.Utility;

namespace Content.Server.LandMines;

public sealed class LandMineSystem : EntitySystem
{
    [Dependency] private readonly SharedAudioSystem _audioSystem = default!;
    [Dependency] private readonly SharedPopupSystem _popupSystem = default!;
    [Dependency] private readonly TriggerSystem _trigger = default!;
    [Dependency] private readonly DoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedAmbientSoundSystem _ambientSound = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedStunSystem _stun = default!;
    [Dependency] private readonly SharedSpecialSystem _special = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<LandMineComponent, StepTriggeredOnEvent>(HandleStepOnTriggered);
        SubscribeLocalEvent<LandMineComponent, StepTriggeredOffEvent>(HandleStepOffTriggered);
        SubscribeLocalEvent<LandMineComponent, StepTriggerAttemptEvent>(HandleStepTriggerAttempt);
        // #Misfits Add - arm/disarm verbs and related event handlers
        SubscribeLocalEvent<LandMineComponent, GetVerbsEvent<AlternativeVerb>>(AddVerbs);
        SubscribeLocalEvent<LandMineComponent, LandMineArmDoAfterEvent>(OnArmDoAfter);
        SubscribeLocalEvent<LandMineComponent, LandMineDisarmDoAfterEvent>(OnDisarmDoAfter);
        // #Misfits Add - block wrenching an armed mine loose
        SubscribeLocalEvent<LandMineComponent, UnanchorAttemptEvent>(OnUnanchorAttempt);
        // #Misfits Add - optional knockdown on trigger (used by concussion mine)
        SubscribeLocalEvent<LandMineComponent, TriggerEvent>(HandleKnockdownTrigger);
        // #Misfits Add - trigger when a thrown entity lands on an armed mine
        SubscribeLocalEvent<LandMineComponent, ThrowHitByEvent>(HandleThrowHit);
    }

    private void HandleStepOnTriggered(EntityUid uid, LandMineComponent component, ref StepTriggeredOnEvent args)
    {
        _popupSystem.PopupCoordinates(
            Loc.GetString("land-mine-triggered", ("mine", uid)),
            Transform(uid).Coordinates,
            args.Tripper,
            PopupType.LargeCaution);

        _audioSystem.PlayPvs(component.Sound, uid);
    }

    private void HandleStepOffTriggered(EntityUid uid, LandMineComponent component, ref StepTriggeredOffEvent args)
    {
        _trigger.Trigger(uid, args.Tripper);
    }

    // #Misfits Tweak - only allow trigger when armed; disarmed mines are inert
    private static void HandleStepTriggerAttempt(EntityUid uid, LandMineComponent component, ref StepTriggerAttemptEvent args)
    {
        args.Continue = component.Armed;
    }

    // #Misfits Add - detonate when a thrown entity hits an armed, anchored mine
    private void HandleThrowHit(EntityUid uid, LandMineComponent component, ThrowHitByEvent args)
    {
        // Only react when armed and still bolted to the floor
        if (!component.Armed || !Transform(uid).Anchored)
            return;

        // Show the warning popup to the thrower if one exists
        if (args.User is { } thrower)
        {
            _popupSystem.PopupCoordinates(
                Loc.GetString("land-mine-triggered", ("mine", uid)),
                Transform(uid).Coordinates,
                thrower,
                PopupType.LargeCaution);
        }

        _audioSystem.PlayPvs(component.Sound, uid);

        // Pass the thrower as the event user so knockdown (if any) applies to them
        _trigger.Trigger(uid, args.User);
    }

    // #Misfits Add - knock down the tripper if KnockdownDuration is configured (concussion mine)
    private void HandleKnockdownTrigger(EntityUid uid, LandMineComponent component, TriggerEvent args)
    {
        if (component.KnockdownDuration is not { } duration || args.User is not { } target)
            return;

        _stun.TryKnockdown(target, duration, refresh: true);
    }

    // #Misfits Add - block unanchoring an armed mine with a wrench
    private void OnUnanchorAttempt(EntityUid uid, LandMineComponent component, UnanchorAttemptEvent args)
    {
        if (!component.Armed)
            return;

        args.Cancel();
        _popupSystem.PopupEntity(
            Loc.GetString("land-mine-unanchor-blocked", ("mine", uid)),
            uid, args.User);
    }

    // #Misfits Add - show "Arm" when anchored+disarmed, "Disarm" when armed
    private void AddVerbs(EntityUid uid, LandMineComponent component, GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanInteract || !args.CanAccess || args.Hands == null)
            return;

        if (component.Armed)
        {
            // --- Disarm verb (4-second careful defuse) ---
            args.Verbs.Add(new AlternativeVerb
            {
                Text = Loc.GetString("land-mine-verb-disarm"),
                Icon = new SpriteSpecifier.Texture(new("/Textures/Interface/VerbIcons/pickup.svg.192dpi.png")),
                Act = () =>
                {
                    _popupSystem.PopupEntity(
                        Loc.GetString("land-mine-disarm-start", ("mine", uid)),
                        uid, args.User);

                    var da = new DoAfterArgs(EntityManager, args.User, GetPerceptionMineDelay(args.User, 4f),
                        new LandMineDisarmDoAfterEvent(), uid, target: uid)
                    {
                        BreakOnDamage = true,
                        BreakOnMove = true,
                        NeedHand = true,
                        BreakOnHandChange = true,
                    };
                    _doAfter.TryStartDoAfter(da);
                },
                Priority = 1,
            });
        }
        else
        {
            // --- Arm verb (only available when anchored) ---
            if (!Transform(uid).Anchored)
                return;

            args.Verbs.Add(new AlternativeVerb
            {
                Text = Loc.GetString("land-mine-verb-arm"),
                Icon = new SpriteSpecifier.Texture(new("/Textures/Interface/VerbIcons/exclamation.svg.192dpi.png")),
                Act = () =>
                {
                    if (_trigger.TryPacifiedBlockArm(uid, args.User))
                        return;

                    _popupSystem.PopupEntity(
                        Loc.GetString("land-mine-arm-start", ("mine", uid)),
                        uid, args.User);

                    var da = new DoAfterArgs(EntityManager, args.User, GetPerceptionMineDelay(args.User, 2f),
                        new LandMineArmDoAfterEvent(), uid, target: uid)
                    {
                        BreakOnDamage = true,
                        BreakOnMove = true,
                        NeedHand = true,
                        BreakOnHandChange = true,
                    };
                    _doAfter.TryStartDoAfter(da);
                },
                Priority = 1,
            });
        }
    }

    // #Misfits Add - on arm: mark armed, start beep (if component present), update sprite
    private void OnArmDoAfter(EntityUid uid, LandMineComponent component, LandMineArmDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || Deleted(uid))
            return;

        // Safety check: must still be anchored
        if (!Transform(uid).Anchored)
        {
            _popupSystem.PopupEntity(
                Loc.GetString("land-mine-arm-fail-unanchored", ("mine", uid)),
                uid, args.User);
            return;
        }

        args.Handled = true;
        component.Armed = true;

        _popupSystem.PopupEntity(
            Loc.GetString("land-mine-arm-success", ("mine", uid)),
            uid, args.User);

        // Start ambient beep for mines that carry an AmbientSoundComponent
        if (TryComp<AmbientSoundComponent>(uid, out _))
            _ambientSound.SetAmbience(uid, true);

        // Switch sprite to animated armed state
        if (TryComp<AppearanceComponent>(uid, out var appearance))
            _appearance.SetData(uid, LandMineVisuals.Armed, true, appearance);
    }

    // #Misfits Add - on disarm: clear armed flag, stop beep, unanchor, then hand mine to player
    private void OnDisarmDoAfter(EntityUid uid, LandMineComponent component, LandMineDisarmDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || Deleted(uid))
            return;

        args.Handled = true;
        component.Armed = false;

        _popupSystem.PopupEntity(
            Loc.GetString("land-mine-disarm-success", ("mine", uid)),
            uid, args.User);

        // Stop ambient beep
        if (TryComp<AmbientSoundComponent>(uid, out _))
            _ambientSound.SetAmbience(uid, false);

        // Switch sprite back to inactive state
        if (TryComp<AppearanceComponent>(uid, out var appearance))
            _appearance.SetData(uid, LandMineVisuals.Armed, false, appearance);

        // Unanchor so the mine can be moved and picked up
        var xform = Transform(uid);
        if (xform.Anchored)
            _transform.Unanchor(uid, xform);

        _hands.TryPickupAnyHand(args.User, uid);
    }

    private TimeSpan GetPerceptionMineDelay(EntityUid user, float baseSeconds)
    {
        var tuning = _special.GetTuning();
        var modifier = _special.GetCurvedEffectModifier(
            user,
            SpecialStat.Perception,
            -tuning.PerceptionMineDelayMultiplierPerPoint);
        var seconds = MathF.Max(0.5f, baseSeconds * (1f + modifier));

        return TimeSpan.FromSeconds(seconds);
    }
}
