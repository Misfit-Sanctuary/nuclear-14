using Content.Server.DeviceLinking.Events;
using Content.Server.Explosion.EntitySystems;
using Content.Shared._Misfits.SuicideVest;
using Content.Shared.Actions;
using Content.Shared.DeviceLinking;
using Content.Shared.DeviceLinking.Events;
using Content.Shared.Examine;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Shared.Audio.Systems;

namespace Content.Server._Misfits.SuicideVest;

public sealed class SuicideVestSystem : EntitySystem
{
    private const string TriggerPort = "Trigger";

    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly TriggerSystem _trigger = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SuicideVestComponent, GotEquippedEvent>(OnEquipped);
        SubscribeLocalEvent<SuicideVestComponent, GotUnequippedEvent>(OnUnequipped);
        SubscribeLocalEvent<SuicideVestComponent, BeingUnequippedAttemptEvent>(OnUnequipAttempt);
        SubscribeLocalEvent<SuicideVestComponent, ComponentShutdown>(OnShutdown);

        SubscribeLocalEvent<SuicideVestComponent, SuicideVestDetonateEvent>(OnDetonateAction);
        SubscribeLocalEvent<SuicideVestComponent, GetVerbsEvent<AlternativeVerb>>(OnGetAltVerbs);
        SubscribeLocalEvent<SuicideVestComponent, ExaminedEvent>(OnExamined);

        SubscribeLocalEvent<SuicideVestComponent, TriggerEvent>(OnTriggered);
        SubscribeLocalEvent<SuicideVestComponent, SignalReceivedEvent>(OnSignalReceived);

        SubscribeLocalEvent<SuicideVestComponent, NewLinkEvent>(OnNewLink);
        SubscribeLocalEvent<SuicideVestComponent, PortDisconnectedEvent>(OnPortDisconnected);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<SuicideVestComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.TimeRemaining is not { } remaining)
                continue;

            remaining -= frameTime;

            if (!comp.DetonateSoundPlayed && remaining <= comp.MinDelay)
            {
                comp.DetonateSoundPlayed = true;
                _audio.PlayPvs(comp.DetonateSound, uid);
            }

            if (remaining <= 0)
            {
                comp.TimeRemaining = null;
                _trigger.Trigger(uid, comp.Wearer);
                continue;
            }

            comp.TimeRemaining = remaining;
        }
    }

    private void OnEquipped(Entity<SuicideVestComponent> ent, ref GotEquippedEvent args)
    {
        if ((args.SlotFlags & SlotFlags.OUTERCLOTHING) == 0)
            return;

        ent.Comp.Wearer = args.Equipee;

        if (!ent.Comp.Triggered && !IsRemoteLinked(ent))
            GrantAction(ent, args.Equipee);
    }

    private void OnUnequipped(Entity<SuicideVestComponent> ent, ref GotUnequippedEvent args)
    {
        if ((args.SlotFlags & SlotFlags.OUTERCLOTHING) == 0)
            return;

        if (ent.Comp.Wearer is { } wearer)
            RevokeAction(ent, wearer);

        ent.Comp.Wearer = null;
    }

    private void OnUnequipAttempt(Entity<SuicideVestComponent> ent, ref BeingUnequippedAttemptEvent args)
    {
        if (ent.Comp.Triggered)
        {
            args.Cancel();
            args.Reason = "misfits-suicide-vest-cannot-remove-triggered";
            _popup.PopupEntity(Loc.GetString("misfits-suicide-vest-cannot-remove-triggered"), ent.Owner, args.Unequipee);
            return;
        }

        if (args.Unequipee == args.UnEquipTarget)
        {
            args.Cancel();
            args.Reason = "misfits-suicide-vest-cannot-remove-self";
            _popup.PopupEntity(Loc.GetString("misfits-suicide-vest-cannot-remove-self"), ent.Owner, args.Unequipee);
        }
    }

    private void OnShutdown(Entity<SuicideVestComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.Wearer is { } wearer && ent.Comp.ActionEntity != null)
            _actions.RemoveAction(wearer, ent.Comp.ActionEntity);
    }

    private void OnDetonateAction(Entity<SuicideVestComponent> ent, ref SuicideVestDetonateEvent args)
    {
        if (args.Handled)
            return;

        if (ent.Comp.Wearer != args.Performer || IsRemoteLinked(ent))
            return;

        args.Handled = true;

        if (ent.Comp.Triggered)
            return;

        _popup.PopupEntity(Loc.GetString("misfits-suicide-vest-armed"), ent.Owner, args.Performer, PopupType.LargeCaution);
        Arm(ent, args.Performer, ent.Comp.Delay);
    }

    private void OnSignalReceived(Entity<SuicideVestComponent> ent, ref SignalReceivedEvent args)
    {
        if (args.Port != TriggerPort)
            return;

        Arm(ent, args.Trigger, ent.Comp.MinDelay);
    }

    private void Arm(Entity<SuicideVestComponent> ent, EntityUid? user, float delay)
    {
        if (ent.Comp.Triggered)
            return;

        ent.Comp.Triggered = true;
        ent.Comp.DetonateSoundPlayed = false;
        ent.Comp.TimeRemaining = MathF.Max(delay, ent.Comp.MinDelay);

        if (ent.Comp.Wearer is { } wearer)
            RevokeAction(ent, wearer);
    }

    private void OnGetAltVerbs(Entity<SuicideVestComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanInteract || !args.CanAccess)
            return;

        if (ent.Comp.Triggered || IsRemoteLinked(ent))
            return;

        if (ent.Comp.DelayOptions.Count < 2)
            return;

        var user = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("misfits-suicide-vest-verb-cycle-delay"),
            Priority = 1,
            Act = () => CycleDelay(ent, user),
        });
    }

    private void CycleDelay(Entity<SuicideVestComponent> ent, EntityUid user)
    {
        var options = ent.Comp.DelayOptions;
        options.Sort();

        var next = options[0];
        foreach (var option in options)
        {
            if (option > ent.Comp.Delay)
            {
                next = option;
                break;
            }
        }

        ent.Comp.Delay = next;
        _popup.PopupEntity(Loc.GetString("misfits-suicide-vest-delay-set", ("time", next)), ent.Owner, user);
    }

    private void OnExamined(Entity<SuicideVestComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        if (ent.Comp.Triggered)
            args.PushMarkup(Loc.GetString("misfits-suicide-vest-examine-triggered"));
        else if (IsRemoteLinked(ent))
            args.PushMarkup(Loc.GetString("misfits-suicide-vest-examine-remote-linked"));
        else
            args.PushMarkup(Loc.GetString("misfits-suicide-vest-examine-delay", ("time", ent.Comp.Delay)));
    }

    private void OnTriggered(Entity<SuicideVestComponent> ent, ref TriggerEvent args)
    {
        ent.Comp.Triggered = true;
    }

    private void OnNewLink(Entity<SuicideVestComponent> ent, ref NewLinkEvent args)
    {
        if (ent.Comp.Wearer is { } wearer)
            RevokeAction(ent, wearer);
    }

    private void OnPortDisconnected(Entity<SuicideVestComponent> ent, ref PortDisconnectedEvent args)
    {
        if (ent.Comp.Wearer is { } wearer && !ent.Comp.Triggered && !IsRemoteLinked(ent))
            GrantAction(ent, wearer);
    }

    private void GrantAction(Entity<SuicideVestComponent> ent, EntityUid wearer)
    {
        if (ent.Comp.ActionEntity != null)
            return;

        _actions.AddAction(wearer, ref ent.Comp.ActionEntity, ent.Comp.DetonateAction, ent.Owner);
    }

    private void RevokeAction(Entity<SuicideVestComponent> ent, EntityUid wearer)
    {
        _actions.RemoveAction(wearer, ent.Comp.ActionEntity);
        ent.Comp.ActionEntity = null;
    }

    private bool IsRemoteLinked(Entity<SuicideVestComponent> ent)
    {
        return TryComp<DeviceLinkSinkComponent>(ent.Owner, out var sink) && sink.LinkedSources.Count > 0;
    }
}
