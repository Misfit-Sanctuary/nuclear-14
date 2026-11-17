using Content.Shared._Goobstation.Vehicles;
using Content.Shared.Clothing;
using Content.Shared.Inventory.Events;
using Content.Shared.Mobs;

namespace Content.Shared._Misfits.Visual;


public sealed partial class AppearenceOverlaySystem : EntitySystem
{
    [Dependency] private SharedAppearanceSystem _appearanceSystem = default!;
    [Dependency] private SharedTransformSystem _xform = default!;
    // private const string StateSuffix = "-overlay";
    // private const string DefaultProtoId = "Error";
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<OverlayComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<OverlayComponent, EquipmentVisualsUpdatedEvent>(OnEquipVisuals);
        SubscribeLocalEvent<OverlayComponent, GotUnequippedEvent>(OnUnequipVisuals);
        SubscribeLocalEvent<OverlayComponent,MobStateChangedEvent>(OnMobStateChange);

    }

    private void OnStartup(Entity<OverlayComponent> ent, ref ComponentStartup args)
    {

        var overlayEnt = SpawnAttachedTo(ent.Comp.OverlayPrototype, Transform(ent).Coordinates);
        ent.Comp.OverlayEntity = overlayEnt;
        _appearanceSystem.SetData(overlayEnt, OverlayMaps.Base, true);
        _appearanceSystem.SetData(overlayEnt, OverlayMaps.Equippable, true);
        Dirty(ent);
        // if (_appearanceSystem.TryGetData(overlayEnt, OverlayMaps.Base, out var _))
        //     _appearanceSystem.SetData(overlayEnt, OverlayMaps.Base, true);
    }

    private void OnEquipVisuals(Entity<OverlayComponent> ent, ref EquipmentVisualsUpdatedEvent ev)
    {


        _appearanceSystem.SetData(ent.Comp.OverlayEntity, OverlayMaps.Equippable, true);
        _xform.SetCoordinates(ent.Comp.OverlayEntity,Transform(ev.Equipee).Coordinates);
        Dirty(ent);
    }
    private void OnUnequipVisuals(Entity<OverlayComponent> ent, ref GotUnequippedEvent ev)
    {

        _appearanceSystem.SetData(ent.Comp.OverlayEntity, OverlayMaps.Equippable, false);
        _xform.SetCoordinates(ent.Comp.OverlayEntity,Transform(ent).Coordinates);
        Dirty(ent);
    }

    private void OnMobStateChange(Entity<OverlayComponent> ent, ref MobStateChangedEvent ev)
    {

        switch (ev.NewMobState)
        {
            case MobState.Alive:
                if (_appearanceSystem.TryGetData(ent.Comp.OverlayEntity, OverlayMaps.Base, out var _))
                    _appearanceSystem.SetData(ent.Comp.OverlayEntity, OverlayMaps.Base, true);
                break;
            default:
                if (_appearanceSystem.TryGetData(ent.Comp.OverlayEntity, OverlayMaps.Base, out var _))
                    _appearanceSystem.SetData(ent.Comp.OverlayEntity, OverlayMaps.Base, false);
                break;
        }
        Dirty(ent);
    }
}
