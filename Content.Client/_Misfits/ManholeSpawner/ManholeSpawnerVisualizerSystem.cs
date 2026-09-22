using Content.Shared._Misfits.ManholeSpawner;
using Robust.Client.GameObjects;

namespace Content.Client._Misfits.ManholeSpawner;

public sealed class ManholeSpawnerVisualizerSystem : VisualizerSystem<ManholeSpawnerComponent>
{
    protected override void OnAppearanceChange(EntityUid uid, ManholeSpawnerComponent component, ref AppearanceChangeEvent args)
    {
        if (args.Sprite == null)
            return;

        if (!AppearanceSystem.TryGetData(uid, ManholeSpawnerVisuals.State, out ManholeSpawnerState state, args.Component))
            return;

        var spriteState = state == ManholeSpawnerState.Open ? component.OpenState : component.ClosedState;
        args.Sprite.LayerSetState(0, spriteState);
    }
}