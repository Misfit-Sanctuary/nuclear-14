using System.Numerics;
using Content.Shared._Misfits.Vehicles.Vertibird;
using Content.Shared._MultiZ.Core.Components;
using Content.Shared._MultiZ.Core.EntitySystems;
using Robust.Client.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Client._Misfits.Vehicles.Vertibird;

public sealed partial class VertibirdVisualsSystem : EntitySystem
{
    private const float HoverVisualLift = 0.22f;
    private const float HoverBobAmplitude = 0.08f;
    private const float HoverBobSpeed = 2.2f;

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private MZSharedSystem _multiZ = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SpriteSystem _sprite = default!;

    private readonly Dictionary<EntityUid, TimeSpan> _nextFlightEffect = new();
    private readonly Dictionary<EntityUid, bool> _hiddenOccupantVisibility = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<VertibirdComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<VertibirdComponent, AfterAutoHandleStateEvent>(OnStateChanged);
        SubscribeLocalEvent<VertibirdComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<VertibirdHiddenOccupantComponent, ComponentStartup>(OnOccupantHidden);
        SubscribeLocalEvent<VertibirdHiddenOccupantComponent, ComponentShutdown>(OnOccupantRevealed);
    }

    private void OnStartup(Entity<VertibirdComponent> ent, ref ComponentStartup args)
    {
        UpdateSprite(ent);
    }

    private void OnStateChanged(Entity<VertibirdComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        UpdateSprite(ent);
    }

    private void OnShutdown(Entity<VertibirdComponent> ent, ref ComponentShutdown args)
    {
        _nextFlightEffect.Remove(ent.Owner);
    }

    private void OnOccupantHidden(Entity<VertibirdHiddenOccupantComponent> ent, ref ComponentStartup args)
    {
        if (!TryComp<SpriteComponent>(ent, out var sprite))
            return;

        _hiddenOccupantVisibility.TryAdd(ent.Owner, sprite.Visible);
        _sprite.SetVisible((ent.Owner, sprite), false);
    }

    private void OnOccupantRevealed(Entity<VertibirdHiddenOccupantComponent> ent, ref ComponentShutdown args)
    {
        if (!_hiddenOccupantVisibility.Remove(ent.Owner, out var wasVisible) ||
            !TryComp<SpriteComponent>(ent, out var sprite))
        {
            return;
        }

        _sprite.SetVisible((ent.Owner, sprite), wasVisible);
    }

    private void UpdateSprite(Entity<VertibirdComponent> ent)
    {
        if (!TryComp<SpriteComponent>(ent, out var sprite) ||
            !_sprite.LayerMapTryGet((ent.Owner, sprite), VertibirdVisualLayers.Base, out _, false))
        {
            return;
        }

        var state = IsAirborne(ent.Comp.State) ? ent.Comp.FlyingSpriteState : ent.Comp.GroundedSpriteState;
        _sprite.LayerSetRsiState((ent.Owner, sprite), VertibirdVisualLayers.Base, state);
        UpdateGroundHoverVisuals(ent, sprite);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_timing.IsFirstTimePredicted)
            return;

        var query = EntityQueryEnumerator<VertibirdComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var vertibird, out var xform))
        {
            if (TryComp<SpriteComponent>(uid, out var sprite))
                UpdateGroundHoverVisuals((uid, vertibird), sprite, xform);

            if (!IsAirborne(vertibird.State) || xform.MapUid is not { } mapUid)
            {
                _nextFlightEffect.Remove(uid);
                continue;
            }

            if (_nextFlightEffect.TryGetValue(uid, out var nextEffect) && _timing.CurTime < nextEffect)
                continue;

            _nextFlightEffect[uid] = _timing.CurTime + TimeSpan.FromSeconds(vertibird.FlightEffectInterval);
            SpawnFlightEffect((uid, vertibird), mapUid, xform);
        }
    }

    private void UpdateGroundHoverVisuals(
        Entity<VertibirdComponent> ent,
        SpriteComponent sprite,
        TransformComponent? xform = null)
    {
        if (!_sprite.LayerMapTryGet((ent.Owner, sprite), VertibirdVisualLayers.Base, out _, false) ||
            !_sprite.LayerMapTryGet((ent.Owner, sprite), VertibirdVisualLayers.Shadow, out _, false))
        {
            return;
        }

        xform ??= Transform(ent.Owner);
        var groundHover = IsAirborne(ent.Comp.State) &&
            xform.MapUid is { } mapUid &&
            TryComp<MZMapComponent>(mapUid, out var zMap) &&
            zMap.Depth == 0;

        _sprite.LayerSetVisible((ent.Owner, sprite), VertibirdVisualLayers.Shadow, groundHover);

        var offset = Vector2.Zero;
        if (groundHover)
        {
            var elapsed = (float) _timing.RealTime.TotalSeconds;
            offset.Y = HoverVisualLift + MathF.Sin(elapsed * HoverBobSpeed) * HoverBobAmplitude;
        }

        _sprite.LayerSetOffset((ent.Owner, sprite), VertibirdVisualLayers.Base, offset);
    }

    private void SpawnFlightEffect(Entity<VertibirdComponent> ent, EntityUid mapUid, TransformComponent xform)
    {
        if (ent.Comp.FlightEffectPrototype is not { } effectProto || effectProto.Length == 0)
            return;

        var worldPosition = _transform.GetWorldPosition(xform);
        var worldRotation = _transform.GetWorldRotation(xform);
        var effectMap = GetVisibleEffectMap(mapUid);

        foreach (var offset in ent.Comp.FlightEffectOffsets)
        {
            var jitter = _random.NextVector2(0.2f);
            var position = worldPosition + worldRotation.RotateVec(offset + jitter);
            Spawn(effectProto, new EntityCoordinates(effectMap, position));
        }

        // On an empty sky layer, the craft itself is composited separately.
        // Project a heavy moving shadow onto the visible lower map so players
        // underneath can immediately read that an aircraft is overhead.
        if (effectMap != mapUid)
        {
            var jitter = _random.NextVector2(0.08f);
            Spawn("VertibirdOverheadShadowEffect", new EntityCoordinates(effectMap, worldPosition + jitter));
        }
    }

    /// <summary>
    /// Empty sky maps render their lower level as the visible world pass. Local
    /// effects placed on the sky map would be omitted from both that pass and
    /// the manually composited vehicle, so project them onto the visible map.
    /// </summary>
    private EntityUid GetVisibleEffectMap(EntityUid mapUid)
    {
        if (HasRenderableGrids(mapUid) ||
            !TryComp<MZMapComponent>(mapUid, out var zMap) ||
            !_multiZ.TryMapDown((mapUid, zMap), out var belowMap))
        {
            return mapUid;
        }

        return belowMap.Value.Owner;
    }

    private bool HasRenderableGrids(EntityUid mapUid)
    {
        var query = EntityQueryEnumerator<TransformComponent, MapGridComponent>();
        while (query.MoveNext(out _, out var xform, out _))
        {
            if (xform.MapUid == mapUid)
                return true;
        }

        return false;
    }

    private static bool IsAirborne(VertibirdFlightState state)
    {
        return state is VertibirdFlightState.TakingOff or
            VertibirdFlightState.Cruising or
            VertibirdFlightState.ChangingAltitude or
            VertibirdFlightState.Landing;
    }
}
