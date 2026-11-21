using System.Numerics;
using Content.Client.Inventory;
using Content.Shared._Misfits.Clothing;
using Content.Shared.Clothing;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Utility;

namespace Content.Client._Misfits.Clothing;

// #Misfits Add - clothing is cut for a human head, so a single inventory slot offset cannot put a hat on a short,
// side-on mob from every side: this slides the slot's layers onto the head for the direction it is facing.
public sealed class MisfitsClothingDirectionOffsetSystem : EntitySystem
{
    [Dependency] private readonly IEyeManager _eyeManager = default!;
    [Dependency] private readonly SpriteSystem _sprite = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    // Where each clothing layer sat before we moved it, so direction changes never stack up.
    private readonly Dictionary<(EntityUid Mob, string Key), Vector2> _baseOffsets = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<EquipmentVisualsUpdatedEvent>(OnEquipmentVisualsUpdated);
    }

    private void OnEquipmentVisualsUpdated(EquipmentVisualsUpdatedEvent args)
    {
        // Rebuilt layers come back with the offsets the clothing system gave them, so forget the old ones.
        foreach (var key in args.RevealedLayers)
            _baseOffsets.Remove((args.Equipee, key));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<MisfitsClothingDirectionOffsetComponent, SpriteComponent, InventorySlotsComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var offsets, out var sprite, out var slots, out var xform))
        {
            // The angle the renderer picks the frame from, so the nudge cannot disagree with the frame it is on.
            var angle = (_transform.GetWorldRotation(xform) + _eyeManager.CurrentEye.Rotation).Reduced().FlipPositive();

            foreach (var (slot, byDirection) in offsets.Slots)
            {
                if (!slots.VisualLayerKeys.TryGetValue(slot, out var keys))
                    continue;

                foreach (var key in keys)
                {
                    if (!sprite.LayerMapTryGet(key, out var index) || sprite[index] is not SpriteComponent.Layer layer)
                        continue;

                    var directions = _sprite.LayerGetDirections(layer);
                    var direction = SpriteComponent.Layer.GetDirection(directions, angle);
                    if (sprite.EnableDirectionOverride)
                        direction = sprite.DirectionOverride.Convert(directions);
                    direction = direction.OffsetRsiDir(layer.DirOffset);

                    var id = (uid, key);
                    if (!_baseOffsets.TryGetValue(id, out var baseOffset))
                    {
                        baseOffset = layer.Offset;
                        _baseOffsets[id] = baseOffset;
                    }

                    var wanted = baseOffset + byDirection.GetValueOrDefault(direction.Convert());
                    if (layer.Offset == wanted)
                        continue;

                    _sprite.LayerSetOffset((uid, sprite), index, wanted);
                }
            }
        }
    }
}
