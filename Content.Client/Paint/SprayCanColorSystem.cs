// #Misfits Add - tints a spray can's label layer from its Paint colour, so a recoloured can looks right.

using Content.Shared.Nutrition.Components;
using Content.Shared.Paint;
using Robust.Client.GameObjects;
using Robust.Shared.GameStates;

namespace Content.Client.Paint;

public sealed class SprayCanColorSystem : EntitySystem
{
    [Dependency] private readonly SpriteSystem _sprite = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PaintComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<PaintComponent, AfterAutoHandleStateEvent>(OnHandleState);
    }

    private void OnStartup(Entity<PaintComponent> ent, ref ComponentStartup args) => ApplyColor(ent);

    private void OnHandleState(Entity<PaintComponent> ent, ref AfterAutoHandleStateEvent args) => ApplyColor(ent);

    private void ApplyColor(Entity<PaintComponent> ent)
    {
        if (!TryComp<SpriteComponent>(ent, out var sprite))
            return;

        if (!sprite.LayerMapTryGet(OpenableVisuals.Layer, out var layer))
            return;

        _sprite.LayerSetColor((ent.Owner, sprite), layer, ent.Comp.Color);
    }
}
