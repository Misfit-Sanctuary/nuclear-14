using Content.Server.Explosion.Components;
using Content.Shared.Explosion.Components;
using Content.Shared.Implants;
using Content.Shared.Interaction.Events;
using Content.Shared.Mobs;
using Content.Shared._Misfits.Genetics.Abilities;
using Content.Shared.Inventory;

namespace Content.Server.Explosion.EntitySystems;

public sealed partial class TriggerSystem
{
    private void InitializeMobstate()
    {
        SubscribeLocalEvent<TriggerOnMobstateChangeComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<TriggerOnMobstateChangeComponent, SuicideEvent>(OnSuicide);

        SubscribeLocalEvent<TriggerOnMobstateChangeComponent, ImplantRelayEvent<SuicideEvent>>(OnSuicideRelay);
        SubscribeLocalEvent<TriggerOnMobstateChangeComponent, ImplantRelayEvent<MobStateChangedEvent>>(OnMobStateRelay);


        SubscribeLocalEvent<UnequipOnTriggerComponent, MobStateChangedEvent>(OnMobStateUnequip);
    }
    /// <summary>
    /// Attempt to unequip every slot defined in SlotsToUnequip
    /// exit early and not throw if ent doesnt have inventoryComp incase other systems want to mess with inventory
    /// <see cref="UnequipOnTriggerComponent"/>
    /// </summary>
    private void OnMobStateUnequip(EntityUid uid, UnequipOnTriggerComponent comp, MobStateChangedEvent args)
    {
        if (!HasComp<InventoryComponent>(uid))
            return;

        for (int i = 0; i < comp.SlotsToUnequip.Length; i++)
        {
            _inventory.TryUnequip(uid, comp.SlotsToUnequip[i],
            force: comp.Forced, reparent: comp.DropToNearestParent,
            checkDoafter: comp.CheckForDoAfter, silent: comp.IsSilent);
        }
    }
    private void OnMobStateChanged(EntityUid uid, TriggerOnMobstateChangeComponent component, MobStateChangedEvent args)
    {
        if (!component.MobState.Contains(args.NewMobState))
            return;

        // Misfits: genetics uses the legacy mob-state trigger with its own entity effects.
        if (HasComp<EffectOnTriggerMutationComponent>(uid))
            EntitySystem.Get<MutationTriggerSystem>().Apply(uid);

        //This chains Mobstate Changed triggers with OnUseTimerTrigger if they have it
        //Very useful for things that require a mobstate change and a timer
        if (TryComp<OnUseTimerTriggerComponent>(uid, out var timerTrigger))
        {
            HandleTimerTrigger(
                uid,
                args.Origin,
                timerTrigger.Delay,
                timerTrigger.BeepInterval,
                timerTrigger.InitialBeepDelay,
                timerTrigger.BeepSound);
        }
        else
            Trigger(uid);
    }

    /// <summary>
    /// Checks if the user has any implants that prevent suicide to avoid some cheesy strategies
    /// Prevents suicide by handling the event without killing the user
    /// </summary>
    private void OnSuicide(EntityUid uid, TriggerOnMobstateChangeComponent component, SuicideEvent args)
    {
        if (args.Handled)
            return;

        if (!component.PreventSuicide)
            return;

        _popupSystem.PopupEntity(Loc.GetString("suicide-prevented"), args.Victim, args.Victim);
        args.Handled = true;
    }

    private void OnSuicideRelay(EntityUid uid, TriggerOnMobstateChangeComponent component, ImplantRelayEvent<SuicideEvent> args)
    {
        OnSuicide(uid, component, args.Event);
    }

    private void OnMobStateRelay(EntityUid uid, TriggerOnMobstateChangeComponent component, ImplantRelayEvent<MobStateChangedEvent> args)
    {
        OnMobStateChanged(uid, component, args.Event);
    }
}
