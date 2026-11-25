using Content.Client.Clothing;
using Content.Shared._Misfits.Mounts;
using Content.Shared._NC.Mountable.Components;
using Content.Shared.Clothing;
using Content.Shared.Inventory.Events;
using Robust.Client.GameObjects;
using Robust.Shared.Utility;

namespace Content.Client._Misfits.Mounts;

public sealed class MountRiderOverlaySystem : EntitySystem
{
    [Dependency] private readonly SpriteSystem _sprite = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MountRiderOverlayComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<MountRiderOverlayComponent, EntParentChangedMessage>(OnParentChanged);

        SubscribeLocalEvent<EquipmentVisualsUpdatedEvent>(OnEquipmentVisualsUpdated);
        SubscribeLocalEvent<MountableComponent, DidUnequipEvent>(OnMountUnequipped, after: [typeof(ClientClothingSystem)]);
    }

    private void OnStartup(Entity<MountRiderOverlayComponent> ent, ref ComponentStartup args)
    {
        Rebuild(ent);
    }

    private void OnParentChanged(Entity<MountRiderOverlayComponent> ent, ref EntParentChangedMessage args)
    {
        Rebuild(ent);
    }

    private void OnEquipmentVisualsUpdated(EquipmentVisualsUpdatedEvent args)
    {
        if (HasComp<MountableComponent>(args.Equipee))
            RebuildOverlaysOn(args.Equipee);
    }

    private void OnMountUnequipped(Entity<MountableComponent> ent, ref DidUnequipEvent args)
    {
        RebuildOverlaysOn(ent);
    }

    private void RebuildOverlaysOn(EntityUid mount)
    {
        var children = Transform(mount).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            if (TryComp<MountRiderOverlayComponent>(child, out var overlay))
                Rebuild((child, overlay));
        }
    }

    private void Rebuild(Entity<MountRiderOverlayComponent> ent)
    {
        if (!TryComp<SpriteComponent>(ent, out var sprite)
            || !TryComp<SpriteComponent>(Transform(ent).ParentUid, out var mountSprite))
            return;

        Entity<SpriteComponent?> overlaySprite = (ent.Owner, sprite);

        for (var i = sprite.AllLayers.Count() - 1; i >= 0; i--)
        {
            _sprite.RemoveLayer(overlaySprite, i);
        }

        _sprite.SetScale(overlaySprite, mountSprite.Scale);
        _sprite.SetOffset(overlaySprite, mountSprite.Offset);
        _sprite.SetColor(overlaySprite, mountSprite.Color);

        foreach (var layer in mountSprite.AllLayers)
        {
            if (!layer.Visible || layer.ActualRsi is not { } rsi || layer.RsiState.Name is not { } state)
                continue;

            var overlayState = state + ent.Comp.StateSuffix;
            if (!rsi.TryGetState(overlayState, out _))
                continue;

            var index = _sprite.AddLayer(overlaySprite, new SpriteSpecifier.Rsi(rsi.Path, overlayState));
            _sprite.LayerSetColor(overlaySprite, index, layer.Color);
            _sprite.LayerSetScale(overlaySprite, index, layer.Scale);
            _sprite.LayerSetRotation(overlaySprite, index, layer.Rotation);
            if (layer is SpriteComponent.Layer concrete)
                _sprite.LayerSetOffset(overlaySprite, index, concrete.Offset);
        }
    }
}
