using System.Runtime.CompilerServices;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Actions;
using Content.Shared.Audio;
using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using Content.Shared.Hands;
using Content.Shared.Inventory.VirtualItem;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Weapons.Melee.Events;


namespace Content.Shared.Vehicles;

/// <summary>
/// misfits current iteration. Depends on entity with vehicle comp having a strap comp.
/// Very basically this just relays input from the strapped entity to the 'vehicle' entity so it moves
/// does this on strap/buckle. Then handles other vehicle things like visuals, engine logic(cant move da car if it isnt on), ect...
/// </summary>

public abstract partial class SharedVehicleSystem : EntitySystem
{
    [Robust.Shared.IoC.Dependency] private SharedActionsSystem _actions = default!;
    [Robust.Shared.IoC.Dependency] private SharedAmbientSoundSystem _ambientSound = default!;
    [Robust.Shared.IoC.Dependency] private SharedAppearanceSystem _appearance = default!;
    [Robust.Shared.IoC.Dependency] private SharedAudioSystem _audio = default!;
    [Robust.Shared.IoC.Dependency] private SharedBuckleSystem _buckle = default!;
    [Robust.Shared.IoC.Dependency] private SharedMoverController _mover = default!;
    [Robust.Shared.IoC.Dependency] private SharedVirtualItemSystem _virtualItem = default!;
    [Robust.Shared.IoC.Dependency] private SharedTransformSystem _transform = default!;
    [Robust.Shared.IoC.Dependency] private ActionContainerSystem _actionContainerSystem = default!;
    [Robust.Shared.IoC.Dependency] private SharedActionsSystem _sharedAct = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<VehicleComponent, ComponentInit>(OnInit);
        // TODO: reset appearence data, rem actions, and other needed things on remove
        // SubscribeLocalEvent<VehicleComponent, ComponentRemove>(OnRemove);

        SubscribeLocalEvent<VehicleComponent, HornActionEvent>(OnHorn);
        SubscribeLocalEvent<VehicleComponent, SirenActionEvent>(OnSiren);

        SubscribeLocalEvent<VehicleComponent, StrappedEvent>(OnStrap);
        SubscribeLocalEvent<VehicleComponent, UnstrappedEvent>(OnUnstrapped);
        SubscribeLocalEvent<VehicleComponent, EngineOnEvent>(EngineOn);
        SubscribeLocalEvent<VehicleComponent, EngineOffEvent>(EngineOff);
        // TODO: prolly change to some access comp?
        // SubscribeLocalEvent<VehicleComponent, GetAdditionalAccessEvent>(OnGetAdditionalAccess);

        // TODO: comp for ActiveTimerTriggerEvent with simple light, sparkle effects, and loud hisss fuse sound
    }

    protected virtual void OnStrapSetAnimate(Entity<VehicleComponent> ent){}
    protected virtual void OnUnstrapSetAnimate(Entity<VehicleComponent> ent){}

    private void OnStrap(Entity<VehicleComponent> ent, ref StrappedEvent args)
    {
        _appearance.SetData(ent, VehicleState.Animated, true);
        OnStrapSetAnimate(ent);
        // LayerSetAutoAnimated(ent, VehicleVisualLayers.AutoAnimate, true);
    }

    private void OnUnstrapped(Entity<VehicleComponent> ent, ref UnstrappedEvent args)
    {
        _appearance.SetData(ent, VehicleState.Animated, false);
        OnUnstrapSetAnimate(ent);
    }
    private void OnInit(EntityUid uid, VehicleComponent component, ComponentInit args)
    {
        _appearance.SetData(uid, VehicleState.Animated, false);
        // _appearance.SetData(uid, VehicleState.DrawOver, false);
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EngineOn(Entity<VehicleComponent> vehicle, ref EngineOnEvent args)
    {
        // _appearance.SetData(vehicle.Owner, VehicleState.Animated, true);
        // _appearance.SetData(vehicle.Owner, VehicleState.Animated, true);
        _ambientSound.SetAmbience(vehicle.Owner, true);
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EngineOff(Entity<VehicleComponent> vehicle, ref EngineOffEvent args)
    {
        // _appearance.SetData(vehicle.Owner, VehicleState.Animated, false);
        _ambientSound.SetAmbience(vehicle.Owner, false);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void DriverEnable(Entity<VehicleComponent>  vehicle, ref InputRelaySetEv ev)
    {
        _actionContainerSystem.TransferAllActions(vehicle.Owner, ev.Driver);
        SetupDriverOverlay(vehicle);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SetupDriverOverlay(Entity<VehicleComponent> ent)
    {
        if (ent.Comp.OverlayPrototype == null)
            return;

        // // Misfits server spawns the overlay & networks it a predicted client spawn would leave duplicate that never gets deleted (cuz its not networked)
        // if (_net.IsClient)
        //     return;
        var overlay = Spawn(ent.Comp.OverlayPrototype, Transform(ent).Coordinates);
        _transform.SetParent(overlay, ent);
        _transform.SetLocalPosition(overlay, Vector2.Zero);
        _transform.SetLocalRotation(overlay, Angle.Zero);
        ent.Comp.ActiveOverlay = overlay;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void DriverDisable(Entity<VehicleComponent>  vehicle, ref InputRelayRemovedEv ev)
    {
        _sharedAct.RemoveProvidedActions(vehicle.Owner, ev.Driver);
        TryQueueDel(vehicle.Comp.ActiveOverlay);
        vehicle.Comp.ActiveOverlay = null;
    }


    /// <summary>
    /// For vehicles that trigger doors and maybe other access tied entities on collide.
    /// Will go by the driver's access perms <see cref="AccessReaderSystem"/>
    /// </summary>
    // private static void OnGetAdditionalAccess(EntityUid vehicle, VehicleComponent component, ref GetAdditionalAccessEvent args)
    // {
    //     var driver = args.
    //     if (driver == null)
    //         return;
    //
    //     args.Entities.Add(driver.Value);
    // }

    private void OnHorn(EntityUid uid, VehicleComponent component, InstantActionEvent args)
    {
        if (args.Handled || component.HornSound == null)
            return;

        _audio.PlayPvs(component.HornSound, uid);
        args.Handled = true;
    }

    private void OnSiren(EntityUid uid, VehicleComponent comp, InstantActionEvent args)
    {
        if (args.Handled || comp.SirenSound == null)
            return;

        comp.SirenEnabled = !comp.SirenEnabled;
        comp.SirenStream = comp.SirenEnabled
            ? _audio.Stop(comp.SirenStream)
            : _audio.PlayPvs(comp.SirenSound, uid)?.Entity;
        args.Handled = true;
    }
}


public sealed partial class HornActionEvent : InstantActionEvent;

public sealed partial class SirenActionEvent : InstantActionEvent;
