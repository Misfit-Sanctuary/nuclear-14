// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Interaction;
using Content.Shared.Traits.Assorted.Components;
using Content.Shared.Vehicles;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;

namespace Content.Shared._Misfits.Interaction;

public sealed partial class ExtraReachSystem : EntitySystem
{
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedInteractionSystem _interact = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    public override void Initialize()
    {
        base.Initialize();

        // run before TK so it can use the extra reach for its check
        SubscribeLocalEvent<ExtraReachComponent, InRangeOverrideEvent>(OnRangeOverride);
        SubscribeLocalEvent<RangeBypassOverrideComponent, InRangeOverrideEvent>(OnRangeBypass);
        SubscribeLocalEvent<AccessByParentComponent, InRangeOverrideEvent>(OnParentAccessRange);
    }

    private void OnRangeBypass(Entity<RangeBypassOverrideComponent> ent, ref InRangeOverrideEvent args)
    {
        if (args.Handled)
            return;

        args.InRange = ent.Comp.Override;
        args.Handled = args.InRange;

    }
    private const float TOLERANCE = 0.1f;
    private void OnParentAccessRange(Entity<AccessByParentComponent> ent, ref InRangeOverrideEvent args)
    {
        if (args.Handled)
            return;
        // this function is awsome glad I found it
        _physics.TryGetNearest(ent.Owner, args.User, out var _, out var _, out float distance);
        // tolerance because of floating point error
        args.InRange = distance <= args.Range + TOLERANCE;
        args.Handled = args.InRange;
    }
    private void OnRangeOverride(Entity<ExtraReachComponent> ent, ref InRangeOverrideEvent args)
    {
        var userXform = Transform(args.User);
        var targetXform = Transform(args.Target);
        if (userXform.MapUid != targetXform.MapUid)
            return;

        var userPos = _transform.GetMapCoordinates(args.User, userXform).Position;
        var targetPos = _transform.GetMapCoordinates(args.Target, targetXform).Position;
        var range = SharedInteractionSystem.InteractionRange + ent.Comp.Bonus;
        args.Handled = true;
        args.InRange = (userPos - targetPos).LengthSquared() <= range * range;
    }

}
