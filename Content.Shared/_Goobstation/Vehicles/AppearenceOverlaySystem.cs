using Content.Shared._Goobstation.Vehicles;
using Content.Shared.Clothing;
using Content.Shared.Inventory.Events;
using Content.Shared.Mobs;
using Robust.Shared.Map;
using Robust.Shared.Timing;


namespace Content.Shared._Misfits.Visual;


public sealed partial class AppearenceOverlaySystem : EntitySystem
{
    [Dependency] private SharedAppearanceSystem _appearanceSystem = default!;
    [Dependency] private SharedTransformSystem _xform = default!;
    [Dependency] private IGameTiming _time = default!;
    // private const string StateSuffix = "-overlay";
    // private const string DefaultProtoId = "Error";
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<OverlayComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<OverlayComponent, GotEquippedEvent>(OnEquipVisuals);
        SubscribeLocalEvent<OverlayComponent, GotUnequippedEvent>(OnUnequipVisuals);
        SubscribeLocalEvent<OverlayComponent,MobStateChangedEvent>(OnMobStateChange);

    }

    private void OnStartup(Entity<OverlayComponent> ent, ref ComponentStartup args)
    {

        var overlayEnt = PredictedSpawnAttachedTo(ent.Comp.OverlayPrototype, Transform(ent).Coordinates);
        ent.Comp.OverlayEntity = overlayEnt;
        UpdateAppearance(ent);
        // if (_appearanceSystem.TryGetData(overlayEnt, OverlayMaps.Base, out var _))
        //     _appearanceSystem.SetData(overlayEnt, OverlayMaps.Base, true);
    }

    private void UpdateAppearance(Entity<OverlayComponent> ent)
    {
        _appearanceSystem.SetData(ent.Comp.OverlayEntity, OverlayMaps.Equippable, ent.Comp.Equipped);
        _appearanceSystem.SetData(ent.Comp.OverlayEntity, OverlayMaps.Base, ent.Comp.Base);
        AppearanceComponent? comp = null;
        if(Resolve(ent.Comp.OverlayEntity, ref comp, false))
            _appearanceSystem.QueueUpdate(ent.Comp.OverlayEntity, comp);

    }
    // THIS WASN'T CALLED ON THE SERVER FOR FUCKS SAKE!!!!!!!
    private void OnEquipVisuals(Entity<OverlayComponent> ent, ref GotEquippedEvent ev)
    {

        ent.Comp.Equipped = true;
        UpdateAppearance(ent);
        Dirty(ent);

        if (_time.ApplyingState)
            return;
        var newParentXform = Transform(ev.Equipee);

        var coords = new EntityCoordinates(ev.Equipee, Vector2.Zero);
        _xform.SetCoordinates(ent.Comp.OverlayEntity, Transform(ent.Comp.OverlayEntity),coords, Angle.Zero,true,newParentXform);


    }
    private void OnUnequipVisuals(Entity<OverlayComponent> ent, ref GotUnequippedEvent ev)
    {

        ent.Comp.Equipped = false;
        UpdateAppearance(ent);
        Dirty(ent);

        if (_time.ApplyingState)
            return;

        var newParentXform = Transform(ent);

        var coords = new EntityCoordinates(ent, Vector2.Zero);
        _xform.SetCoordinates(ent.Comp.OverlayEntity, Transform(ent.Comp.OverlayEntity),coords, Angle.Zero,true,newParentXform);



    }

    private void OnMobStateChange(Entity<OverlayComponent> ent, ref MobStateChangedEvent ev)
    {
        switch (ev.NewMobState)
        {
            case MobState.Alive:
                ent.Comp.Base = true;
                break;
            default:
                ent.Comp.Base = false;
                break;
        }

        UpdateAppearance(ent);
        Dirty(ent);

    }
}
