using System.Numerics;
using Content.Shared.Buckle.Components;

namespace Content.Shared._Misfits.Buckle;

public sealed class DirectionalBuckleOffsetSystem : EntitySystem
{
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private EntityQuery<TransformComponent> _xformQuery;

    public override void Initialize()
    {
        base.Initialize();

        _xformQuery = GetEntityQuery<TransformComponent>();

        SubscribeLocalEvent<DirectionalBuckleOffsetComponent, StrappedEvent>(OnStrapped);
        SubscribeLocalEvent<DirectionalBuckleOffsetComponent, MoveEvent>(OnMove);
    }

    private void OnStrapped(Entity<DirectionalBuckleOffsetComponent> ent, ref StrappedEvent args)
    {
        SnapToOffset(ent, args.Buckle.Owner, GetLocalOffset(ent.Comp, Transform(ent).LocalRotation));
    }

    private void OnMove(Entity<DirectionalBuckleOffsetComponent> ent, ref MoveEvent args)
    {
        if (args.NewRotation == args.OldRotation)
            return;

        if (!TryComp<StrapComponent>(ent, out var strap) || strap.BuckledEntities.Count == 0)
            return;

        var offset = GetLocalOffset(ent.Comp, args.NewRotation);
        foreach (var buckled in strap.BuckledEntities)
        {
            SnapToOffset(ent, buckled, offset);
        }
    }

    public void SnapToOffset(EntityUid strap, EntityUid buckled, Vector2 offset)
    {
        if (!_xformQuery.TryComp(buckled, out var xform) || xform.ParentUid != strap)
            return;

        if (!xform.LocalPosition.EqualsApprox(offset))
            _transform.SetLocalPositionNoLerp(buckled, offset, xform);
    }

    // because they are parented
    public static Vector2 GetLocalOffset(DirectionalBuckleOffsetComponent comp, Angle strapRotation)
    {
        var offset = comp.Offsets.GetValueOrDefault(SpriteFacing.GetCardinalDir(strapRotation), Vector2.Zero);
        return (-strapRotation).RotateVec(offset);
    }
}
