using Content.Shared._Misfits.Weapons.Throwable;
using Robust.Client.GameObjects;
using Robust.Shared.Utility;

namespace Content.Client._Misfits.Weapons.Throwable;

/// <summary>
/// Adds the molotov wick as a separate sprite layer so converted bottles retain their original sprite, avoiding
/// having to resprite every single bottle. The wick layer switches to an animated burning state when the molotov is ignited.
/// </summary>
public sealed class MolotovVisualSystem : EntitySystem
{
    private static readonly ResPath Rsi = new("_Misfits/Objects/Weapons/Throwable/molotov_wick.rsi");
    private static readonly SpriteSpecifier.Rsi UnlitWick = new(Rsi, "wick");
    private static readonly SpriteSpecifier.Rsi BurningWick = new(Rsi, "wick-burning");

    public override void Initialize()
    {
        // Update the visuals immediately when this component appears and whenever
        // its ignition state changes.
        SubscribeLocalEvent<MolotovComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<MolotovComponent, AfterAutoHandleStateEvent>(OnState);
        SubscribeLocalEvent<MolotovComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnStartup(Entity<MolotovComponent> ent, ref ComponentStartup args) => UpdateVisual(ent);
    private void OnState(Entity<MolotovComponent> ent, ref AfterAutoHandleStateEvent args) => UpdateVisual(ent);

    private void UpdateVisual(Entity<MolotovComponent> ent)
    {
        if (!TryComp<SpriteComponent>(ent, out var sprite))
            return;

        if (!sprite.LayerMapTryGet(MolotovLayer.Wick, out var layer))
        {
            layer = sprite.AddLayer(UnlitWick);
            sprite.LayerMapSet(MolotovLayer.Wick, layer);
        }

        sprite.LayerSetSprite(layer, ent.Comp.Ignited ? BurningWick : UnlitWick);
        sprite.LayerSetOffset(layer, ent.Comp.WickOffset);
    }

    private void OnShutdown(Entity<MolotovComponent> ent, ref ComponentShutdown args)
    {
        if (TryComp<SpriteComponent>(ent, out var sprite) &&
            sprite.LayerMapTryGet(MolotovLayer.Wick, out var layer))
            sprite.RemoveLayer(layer);
    }

    private enum MolotovLayer : byte
    {
        Wick,
    }
}
