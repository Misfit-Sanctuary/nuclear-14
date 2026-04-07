using Robust.Client.Graphics;

namespace Content.Client._Misfits.Warcry;

/// <summary>
/// Registers the persistent warcry radius overlay.
/// </summary>
public sealed class WarcryOverlaySystem : EntitySystem
{
    [Dependency] private readonly IOverlayManager _overlay = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;

    public override void Initialize()
    {
        base.Initialize();
        _overlay.AddOverlay(new WarcryOverlay(EntityManager, _lookup));
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlay.RemoveOverlay<WarcryOverlay>();
    }
}