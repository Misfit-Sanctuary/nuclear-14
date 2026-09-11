using Robust.Client.GameObjects;

namespace Content.Client._Misfits.Areas;

public sealed class AreaMarkerSystem : EntitySystem
{
    private bool _areaMarkersVisible;

    public bool AreaMarkersVisible
    {
        get => _areaMarkersVisible;
        set
        {
            _areaMarkersVisible = value;
            UpdateAreaMarkers();
        }
    }

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<AreaMarkerComponent, ComponentStartup>(OnStartup);
    }

    private void OnStartup(EntityUid uid, AreaMarkerComponent marker, ComponentStartup args)
    {
        UpdateVisibility(uid);
    }

    private void UpdateVisibility(EntityUid uid)
    {
        if (EntityManager.TryGetComponent(uid, out SpriteComponent? sprite))
        {
            sprite.Visible = AreaMarkersVisible;
        }
    }

    private void UpdateAreaMarkers()
    {
        var query = AllEntityQuery<AreaMarkerComponent>();

        while (query.MoveNext(out var uid, out _))
        {
            UpdateVisibility(uid);
        }
    }
}
