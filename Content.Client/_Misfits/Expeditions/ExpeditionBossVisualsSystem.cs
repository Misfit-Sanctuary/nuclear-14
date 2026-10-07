using System.Collections.Generic;
using Content.Shared._Misfits.Expeditions;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;

namespace Content.Client._Misfits.Expeditions;

/// <summary>
/// Applies the replicated expedition-boss scale over the prototype sprite's
/// existing scale. This intentionally leaves fixtures and movement untouched.
/// </summary>
public sealed class ExpeditionBossVisualsSystem : EntitySystem
{
    [Dependency] private readonly SpriteSystem _sprite = default!;

    private readonly Dictionary<EntityUid, System.Numerics.Vector2> _baseScales = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ExpeditionBossVisualsComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<ExpeditionBossVisualsComponent, AfterAutoHandleStateEvent>(OnAfterHandleState);
        SubscribeLocalEvent<ExpeditionBossVisualsComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnStartup(EntityUid uid, ExpeditionBossVisualsComponent component, ComponentStartup args)
    {
        ApplyScale(uid, component);
    }

    private void OnAfterHandleState(EntityUid uid, ExpeditionBossVisualsComponent component, ref AfterAutoHandleStateEvent args)
    {
        ApplyScale(uid, component);
    }

    private void OnShutdown(EntityUid uid, ExpeditionBossVisualsComponent component, ComponentShutdown args)
    {
        if (_baseScales.Remove(uid, out var baseScale) && TryComp<SpriteComponent>(uid, out var sprite))
            _sprite.SetScale((uid, sprite), baseScale);
    }

    private void ApplyScale(EntityUid uid, ExpeditionBossVisualsComponent component)
    {
        if (!TryComp<SpriteComponent>(uid, out var sprite))
            return;

        if (!_baseScales.TryGetValue(uid, out var baseScale))
        {
            baseScale = sprite.Scale;
            _baseScales[uid] = baseScale;
        }

        _sprite.SetScale((uid, sprite), baseScale * component.ScaleMultiplier);
    }
}
