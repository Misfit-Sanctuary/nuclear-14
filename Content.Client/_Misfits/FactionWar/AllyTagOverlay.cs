// #Misfits Add - Screen-space overlay that draws [ALLY] and [ENEMY] tags above in-world entities
// when the local player's faction is engaged in an active war.
// Uses a server-provided participant dictionary (NetEntity → faction side) because
// NpcFactionMemberComponent.Factions is NOT synced to clients.
// Pattern mirrors AdminNameOverlay (Content.Client/Administration/AdminNameOverlay.cs).

using System.Numerics;
using Content.Shared.Examine;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.ResourceManagement;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.Client._Misfits.FactionWar;

/// <summary>
/// Draws green <c>[ALLY]</c> or red <c>[ENEMY]</c> tags above entities whose faction is relevant
/// to the current war state. Active only while the local player is involved in at least one war.
/// All ally/enemy classification comes from the server-broadcast participant dict.
/// </summary>
internal sealed class AllyTagOverlay : Overlay
{
    private readonly FactionWarClientSystem _warSystem;
    private readonly IEntityManager         _entityManager;
    private readonly IPlayerManager         _playerManager;
    private readonly IEyeManager            _eyeManager;
    private readonly IGameTiming            _timing;
    private readonly EntityLookupSystem     _entityLookup;
    private readonly ExamineSystemShared    _examine;
    private readonly SharedTransformSystem  _transform;
    private readonly Font                   _font;

    // Cache LOS results briefly so the overlay still feels realtime without rechecking
    // every participant on every draw call during large wars.
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
        IEntityManager         entityManager,
        IPlayerManager         playerManager,
        IEyeManager            eyeManager,
        IGameTiming            timing,
        IResourceCache         resourceCache,
        EntityLookupSystem     entityLookup,
        ExamineSystemShared    examine,
        SharedTransformSystem  transform)
    {
        _warSystem     = warSystem;
        _entityManager = entityManager;
        _playerManager = playerManager;
        _eyeManager    = eyeManager;
        _timing        = timing;
        _entityLookup  = entityLookup;
        _examine       = examine;
        _transform     = transform;

        ZIndex = 195; // just below AdminNameOverlay (200) so admin tags render on top
        _font = new VectorFont(
            resourceCache.GetResource<FontResource>("/Fonts/NotoSans/NotoSans-Regular.ttf"), 10);
    }

    private sealed class VisibilityCacheEntry
    {
        public bool Visible;
        public MapId MapId;
        public Vector2 Position;
        public TimeSpan NextRefresh;
    }

    private bool NeedsVisibilityRefresh(VisibilityCacheEntry entry, MapCoordinates coords, TimeSpan now)
    {
        if (now >= entry.NextRefresh)
            return true;

        if (entry.MapId != coords.MapId)
            return true;

        return (coords.Position - entry.Position).LengthSquared() >= PositionRefreshThresholdSquared;
    }

    private void CleanupCache(IReadOnlyDictionary<NetEntity, string> participants, TimeSpan now)
    {
        if (now < _nextCleanup)
            return;

        _nextCleanup = now + CacheCleanupInterval;

        var cachedEntities = new List<NetEntity>(_visibilityCache.Keys);

        foreach (var netEntity in cachedEntities)
        {
            if (!participants.ContainsKey(netEntity))
                _visibilityCache.Remove(netEntity);
        }
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var localEntity = _playerManager.LocalSession?.AttachedEntity;
        if (localEntity == null)
            return;

        var activeWars = _warSystem.ActiveWars;
        if (activeWars.Count == 0)
            return;

        var participants = _warSystem.WarParticipants;
        if (participants.Count == 0)
            return;

        // Determine the local player's side from the participant dict itself.
        // This stays accurate across respawns/faction-swaps since the server rebuilds it every 2s.
        var localNet = _entityManager.GetNetEntity(localEntity.Value);
        if (!participants.TryGetValue(localNet, out var effectiveFaction))
        {
            // Fallback: /warjoin side if the local entity isn't in the dict yet.
            effectiveFaction = _warSystem.LocalWarJoinSide;
        }

        if (effectiveFaction == null)
            return;

        // Get local player's position for line-of-sight checks.
        var localPos = _transform.GetMapCoordinates(localEntity.Value);
        var now = _timing.CurTime;
        var losRefreshBudget = MaxLosRefreshPerFrame;

        CleanupCache(participants, now);

        var viewport = args.WorldAABB;

        // Iterate the server-provided dict of war-relevant entities and their side.
        foreach (var (netEntity, side) in participants)
        {
            var uid = _entityManager.GetEntity(netEntity);

            // Skip self, non-existent, and entities without sprites.
            if (uid == localEntity.Value || !_entityManager.EntityExists(uid))
                continue;

            if (!_entityManager.HasComponent<SpriteComponent>(uid))
                continue;

            var otherPos = _transform.GetMapCoordinates(uid);
            if (otherPos.MapId != localPos.MapId)
                continue;

            // Cheap range rejection first. This preserves the existing 50-tile behavior
            // while avoiding an occlusion trace for obviously distant targets.
            if ((otherPos.Position - localPos.Position).LengthSquared() > MaxTagDistanceSquared)
                continue;

            var aabb = _entityLookup.GetWorldAABB(uid);
            if (!aabb.Intersects(viewport))
                continue;

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

            // Determine ally or enemy relative to the local player's side.
            string tag;
            Color tagColor;

            if (side == effectiveFaction)
            {
                tag      = "[ALLY]";
                tagColor = Color.LimeGreen;
            }
            else
            {
                tag      = "[ENEMY]";
                tagColor = new Color(1f, 0.3f, 0.3f);
            }

            // Position the tag at the top-right of the entity's AABB, same as AdminNameOverlay.
            var screenCoords = _eyeManager.WorldToScreen(
                aabb.Center + new Angle(-_eyeManager.CurrentEye.Rotation)
                    .RotateVec(aabb.TopRight - aabb.Center)) + new Vector2(1f, 7f);

            args.ScreenHandle.DrawString(_font, screenCoords, tag, tagColor);
        }
    }
}
