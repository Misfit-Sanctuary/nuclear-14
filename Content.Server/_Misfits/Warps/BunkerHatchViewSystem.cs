// #Misfits Add - Camera view through a surface bunker hatch, for the tunnel door menu and the tac map.
using Robust.Server.GameObjects;
using Robust.Shared.Player;

namespace Content.Server._Misfits.Warps;

/// <summary>
/// Lets a player look through a surface hatch from somewhere else, like a security camera.
/// <para>
/// The hatch carries an Eye. Adding the player as a view subscriber makes the server send them
/// everything around the hatch, even from another map, and their UI draws that eye in a viewport.
/// Same trick as the Overwatch feed in <c>OverwatchConsoleSystem</c>.
/// </para>
/// <para>
/// Each player has at most one view. It is dropped as soon as the UI it was opened from closes,
/// the player leaves their body, or the hatch is deleted, so nobody keeps a stray feed.
/// </para>
/// </summary>
public sealed class BunkerHatchViewSystem : EntitySystem
{
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly ViewSubscriberSystem _viewSubscriber = default!;

    private const float ValidateInterval = 0.5f;
    private float _accumulator;

    private readonly Dictionary<EntityUid, HatchView> _views = new();
    private readonly List<EntityUid> _stale = new();

    private sealed record HatchView(ICommonSession Session, EntityUid Hatch, EntityUid Source, Enum UiKey);

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _accumulator += frameTime;
        if (_accumulator < ValidateInterval)
            return;

        _accumulator = 0f;

        foreach (var (viewer, view) in _views)
        {
            if (!IsStillValid(viewer, view))
                _stale.Add(viewer);
        }

        foreach (var viewer in _stale)
            StopViewing(viewer);

        _stale.Clear();
    }

    /// <summary>
    /// Start showing <paramref name="viewer"/> the hatch, replacing any view they already had.
    /// <paramref name="source"/> and <paramref name="uiKey"/> are the UI the request came from; the
    /// view ends when that UI closes.
    /// </summary>
    public bool StartViewing(EntityUid viewer, EntityUid hatch, EntityUid source, Enum uiKey)
    {
        if (!TryComp<ActorComponent>(viewer, out var actor)
            || !TryComp<BunkerTeleporterComponent>(hatch, out var teleporter)
            || !teleporter.IsSurface)
        {
            return false;
        }

        StopViewing(viewer);

        _viewSubscriber.AddViewSubscriber(hatch, actor.PlayerSession);
        _views[viewer] = new HatchView(actor.PlayerSession, hatch, source, uiKey);
        return true;
    }

    public void StopViewing(EntityUid viewer)
    {
        if (!_views.Remove(viewer, out var view))
            return;

        if (!Deleted(view.Hatch))
            _viewSubscriber.RemoveViewSubscriber(view.Hatch, view.Session);
    }

    public EntityUid? GetViewedHatch(EntityUid viewer)
    {
        return _views.TryGetValue(viewer, out var view) ? view.Hatch : null;
    }

    private bool IsStillValid(EntityUid viewer, HatchView view)
    {
        return !Deleted(viewer)
            && !Deleted(view.Hatch)
            && !Deleted(view.Source)
            && view.Session.AttachedEntity == viewer
            && _ui.IsUiOpen(view.Source, view.UiKey, viewer);
    }
}
