using System.Numerics;
using System.Collections.Generic;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.GameObjects;
using Content.Shared.Vehicles;
using Robust.Shared.Timing;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;

namespace Content.Client.Vehicles;

public sealed class VehicleSystem : SharedVehicleSystem
{
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly IEyeManager _eye = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SpriteSystem _sprite = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<VehicleComponent, AppearanceChangeEvent>(OnAppearanceChange);
        SubscribeLocalEvent<VehicleComponent, MoveEvent>(OnMove);
    }



    private void OnAppearanceChange(Entity<VehicleComponent> ent, ref AppearanceChangeEvent args)
    {
        if (args.Sprite == null
            || !_appearance.TryGetData(ent, VehicleState.Animated, out bool animated)
            || !TryComp<SpriteComponent>(ent, out var spriteComp))
            return;

        SpritePos(ent);

        if(!_sprite.TryGetLayer((ent,spriteComp),0,out var layer, false))
            return;

        _sprite.LayerSetAutoAnimated(layer, animated);

        if (_appearance.TryGetData(ent, MotorbikeVisuals.Burning, out bool burning))
            spriteComp.LayerSetColor(0, burning ? Color.Red : Color.White);
    }

    private void OnMove(Entity<VehicleComponent> ent, ref MoveEvent args)
    {
        SpritePos(ent);
    }

    private void SpritePos(Entity<VehicleComponent> ent)
    {
        if (!TryComp<SpriteComponent>(ent, out var spriteComp))
            return;

        // Misfits DrawOver always exists, so check its value. restore the original depth when nobody is riding.
        if (!_appearance.TryGetData(ent, VehicleState.DrawOver, out bool drawOver) || !drawOver)
        {
            if (ent.Comp.OriginalDrawDepth is { } originalDepth)
            {
                _sprite.SetDrawDepth((ent, spriteComp), originalDepth);
                ent.Comp.OriginalDrawDepth = null;
            }

            return;
        }

        ent.Comp.OriginalDrawDepth ??= spriteComp.DrawDepth;
        _sprite.SetDrawDepth((ent, spriteComp), (int)Content.Shared.DrawDepth.DrawDepth.Objects);

        if (ent.Comp.RenderOver == VehicleRenderOver.None)
            return;

        var dir = (Transform(ent).LocalRotation + _eye.CurrentEye.Rotation).GetCardinalDir();
        var renderOverFlag = dir switch
        {
            Direction.North => VehicleRenderOver.North,
            Direction.NorthEast => VehicleRenderOver.NorthEast,
            Direction.East => VehicleRenderOver.East,
            Direction.SouthEast => VehicleRenderOver.SouthEast,
            Direction.South => VehicleRenderOver.South,
            Direction.SouthWest => VehicleRenderOver.SouthWest,
            Direction.West => VehicleRenderOver.West,
            Direction.NorthWest => VehicleRenderOver.NorthWest,
            _ => VehicleRenderOver.None,
        };

        if ((ent.Comp.RenderOver & renderOverFlag) == renderOverFlag)
            _sprite.SetDrawDepth((ent, spriteComp), (int) Content.Shared.DrawDepth.DrawDepth.OverMobs);
    }
}
