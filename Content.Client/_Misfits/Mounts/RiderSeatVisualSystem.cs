using Content.Shared._Misfits.Buckle;
using Content.Shared._NC.Mountable;
using Content.Shared._NC.Mountable.Components;
using Content.Shared.Buckle.Components;
using Robust.Client.GameObjects;
using Robust.Shared;
using Robust.Shared.Configuration;

namespace Content.Client._Misfits.Mounts;

public sealed class RiderSeatVisualSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly DirectionalBuckleOffsetSystem _buckleOffset = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private EntityQuery<MountableComponent> _mountQuery;
    private EntityQuery<TransformComponent> _xformQuery;

    public override void Initialize()
    {
        base.Initialize();

        _mountQuery = GetEntityQuery<MountableComponent>();
        _xformQuery = GetEntityQuery<TransformComponent>();

        UpdatesAfter.Add(typeof(TransformSystem));

        Subs.CVar(_cfg, CVars.RenderSpriteDirectionBias, v => SpriteFacing.DirectionBias = v, true);
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        var riders = EntityQueryEnumerator<RiderComponent>();
        while (riders.MoveNext(out var uid, out var rider))
        {
            if (rider.Mount is not { } mount
                || !_mountQuery.TryComp(mount, out var mountable)
                || !_xformQuery.TryComp(mount, out var mountXform))
                continue;

            SnapRotation(mount, mountXform);
            _buckleOffset.SnapToOffset(mount, uid,
                SharedMountSystem.GetRiderLocalOffset(mountable, mountXform.LocalRotation));
        }

        var straps = EntityQueryEnumerator<DirectionalBuckleOffsetComponent, StrapComponent, TransformComponent>();
        while (straps.MoveNext(out var uid, out var offsets, out var strap, out var xform))
        {
            if (strap.BuckledEntities.Count == 0)
                continue;

            SnapRotation(uid, xform);
            var offset = DirectionalBuckleOffsetSystem.GetLocalOffset(offsets, xform.LocalRotation);
            foreach (var buckled in strap.BuckledEntities)
            {
                _buckleOffset.SnapToOffset(uid, buckled, offset);
            }
        }
    }

    // Misfits engine lerp causes a frame of wrong direction otherwise
    private void SnapRotation(EntityUid uid, TransformComponent xform)
    {
        if (xform.NextRotation is { } target && !xform.LocalRotation.EqualsApprox(target))
            _transform.SetLocalRotationNoLerp(uid, target, xform);
    }
}
