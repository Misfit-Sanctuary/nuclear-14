// #Misfits Refactor - Screen-space overlay that draws [ALLY] and [ENEMY] tags above
// entities participating in active player wars.

using System.Numerics;
using Content.Shared._Misfits.FactionWar;
using Content.Shared.Examine;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.ResourceManagement;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Timing;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Client._Misfits.FactionWar;

internal sealed class AllyTagOverlay : Overlay
{
    private readonly FactionWarClientSystem _warSystem;
    private readonly IEntityManager _entityManager;
    private readonly IPlayerManager _playerManager;
    private readonly IEyeManager _eyeManager;
    private readonly IGameTiming _timing;
    private readonly EntityLookupSystem _entityLookup;
    private readonly ExamineSystemShared _examine;
    private readonly SharedTransformSystem _transform;
    private readonly Font _font;

    private readonly Dictionary<NetEntity, VisibilityCacheEntry> _visibilityCache = new();
    private TimeSpan _nextCleanup;

    private static readonly TimeSpan VisibilityCacheLifetime = TimeSpan.FromSeconds(0.15);
    private static readonly TimeSpan CacheCleanupInterval = TimeSpan.FromSeconds(2);
    private const float MaxTagDistance = 50f;
    private const float MaxTagDistanceSquared = MaxTagDistance * MaxTagDistance;
    private const float PositionRefreshThresholdSquared = 1f;
    private const int MaxLosRefreshPerFrame = 12;

    public override OverlaySpace Space => OverlaySpace.ScreenSpace;

    public AllyTagOverlay(
        FactionWarClientSystem warSystem,
        IEntityManager entityManager,
        IPlayerManager playerManager,
        IEyeManager eyeManager,
        IGameTiming timing,
        IResourceCache resourceCache,
        EntityLookupSystem entityLookup,
        ExamineSystemShared examine,
        SharedTransformSystem transform)
    {
        _warSystem = warSystem;
        _entityManager = entityManager;
        _playerManager = playerManager;
        _eyeManager = eyeManager;
        _timing = timing;
        _entityLookup = entityLookup;
        _examine = examine;
        _transform = transform;

        ZIndex = 195;
        _font = new VectorFont(resourceCache.GetResource<FontResource>("/Fonts/NotoSans/NotoSans-Regular.ttf"), 10);
    }

    private sealed class VisibilityCacheEntry
    {
        public bool Visible;
        public MapId MapId;
        public Vector2 Position;
        public TimeSpan NextRefresh;
    }

    private static bool NeedsVisibilityRefresh(VisibilityCacheEntry entry, MapCoordinates coords, TimeSpan now)
    {
        if (now >= entry.NextRefresh)
            return true;

        if (entry.MapId != coords.MapId)
            return true;

        return (coords.Position - entry.Position).LengthSquared() >= PositionRefreshThresholdSquared;
    }

    private void CleanupCache(TimeSpan now)
    {
        if (now < _nextCleanup)
            return;

        _nextCleanup = now + CacheCleanupInterval;

        var cachedEntities = new List<NetEntity>(_visibilityCache.Keys);
        foreach (var netEntity in cachedEntities)
        {
            var uid = _entityManager.GetEntity(netEntity);
            if (!_entityManager.EntityExists(uid))
                _visibilityCache.Remove(netEntity);
        }
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var localEntity = _playerManager.LocalSession?.AttachedEntity;
        if (localEntity == null)
            return;

        var participants = _warSystem.WarParticipants;
        if (_warSystem.ActiveWars.Count == 0 || participants.Count == 0)
            return;

        var localWarKey = _warSystem.LocalWarKey;
        if (localWarKey == null)
            return;

        var localNet = _entityManager.GetNetEntity(localEntity.Value);
        byte? effectiveSide;
        if (!participants.TryGetValue(localNet, out var tmpSide))
            effectiveSide = _warSystem.LocalWarJoinSide;
        else
            effectiveSide = tmpSide.Side;

        if (effectiveSide == null)
            return;

        var localPos = _transform.GetMapCoordinates(localEntity.Value);
        var now = _timing.CurTime;
        var losRefreshBudget = MaxLosRefreshPerFrame;

        CleanupCache(now);

        var viewport = args.WorldAABB;

        // Draw a relationship tag for every nearby player. A local non-participant
        // does not get an overlay at all; only someone enlisted in this war sees
        // non-participants as neutral.
        var query = _entityManager.EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (uid == localEntity.Value)
                continue;

            if (!_entityManager.HasComponent<SpriteComponent>(uid))
                continue;

            if (xform.MapID != localPos.MapId)
                continue;

            var otherPos = _transform.GetMapCoordinates(uid, xform);
            if (otherPos.MapId != localPos.MapId)
                continue;

            if ((otherPos.Position - localPos.Position).LengthSquared() > MaxTagDistanceSquared)
                continue;

            var aabb = _entityLookup.GetWorldAABB(uid);
            if (!aabb.Intersects(viewport))
                continue;

            var netEntity = _entityManager.GetNetEntity(uid);
            VisibilityCacheEntry? cacheEntry = null;
            if (_visibilityCache.TryGetValue(netEntity, out var cached))
                cacheEntry = cached;

            if (cacheEntry == null || NeedsVisibilityRefresh(cacheEntry, otherPos, now))
            {
                if (losRefreshBudget > 0)
                {
                    losRefreshBudget--;

                    var visible = _examine.InRangeUnOccluded(localPos, otherPos, MaxTagDistance,
                        e => e == localEntity.Value || e == uid);

                    cacheEntry = new VisibilityCacheEntry
                    {
                        Visible = visible,
                        MapId = otherPos.MapId,
                        Position = otherPos.Position,
                        NextRefresh = now + VisibilityCacheLifetime,
                    };

                    _visibilityCache[netEntity] = cacheEntry;
                }
                else if (cacheEntry == null)
                {
                    continue;
                }
            }

            if (!cacheEntry.Visible)
                continue;

            string tag;
            Color color;

            if (!participants.TryGetValue(netEntity, out var info) || info.WarKey != localWarKey)
            {
                tag = "[NEUTRAL]";
                color = Color.White;
            }
            // #Misfits Add - Observer participants see tags but don't get one rendered on them.
            else if (info.IsObserver)
            {
                continue;
            }
            else if (info.Surrendered)
            {
                tag = "[SURRENDERED]";
                color = Color.White;
            }
            else
            {
                var isAlly = info.Side == effectiveSide.Value;
                tag = isAlly ? "[ALLY]" : "[ENEMY]";
                color = isAlly ? Color.LimeGreen : new Color(1f, 0.3f, 0.3f);
            }

            var screenCoords = _eyeManager.WorldToScreen(
                aabb.Center + new Angle(-_eyeManager.CurrentEye.Rotation)
                    .RotateVec(aabb.TopRight - aabb.Center)) + new Vector2(1f, 7f);

            args.ScreenHandle.DrawString(_font, screenCoords, tag, color);
        }
    }
}
