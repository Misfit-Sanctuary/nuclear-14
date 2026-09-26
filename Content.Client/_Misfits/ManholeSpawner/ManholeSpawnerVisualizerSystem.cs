using Content.Shared._Misfits.ManholeSpawner;
using Robust.Client.GameObjects;

namespace Content.Client._Misfits.ManholeSpawner;

public sealed class ManholeSpawnerVisualizerSystem : VisualizerSystem<ManholeSpawnerComponent>
{
    protected override void OnAppearanceChange(EntityUid uid, ManholeSpawnerComponent component, ref AppearanceChangeEvent args)
    {
        if (args.Sprite == null)
            return;

        if (!AppearanceSystem.TryGetData(uid, ManholeSpawnerVisuals.Open, out bool open, args.Component))
            return;

        SpriteSystem.LayerSetRsiState((uid, args.Sprite), 0, open ? component.OpenState : component.ClosedState);
    }
}