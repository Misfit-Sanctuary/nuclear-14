using Content.Shared._NC.Mountable.Components;
using Content.Shared.Buckle.Components;
using Content.Shared.Vehicles;

namespace Content.Shared._Misfits.Mounts;

/// <summary>
/// Make the riders camera follow the mount instead of their own sprite
/// Because of the offsets it causes jittery camera shake otherwise
/// </summary>
public sealed class RiderEyeSystem : EntitySystem
{
    [Dependency] private readonly SharedEyeSystem _eye = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<EyeComponent, BuckledEvent>(OnBuckled);
        SubscribeLocalEvent<EyeComponent, UnbuckledEvent>(OnUnbuckled);
    }

    private void OnBuckled(Entity<EyeComponent> ent, ref BuckledEvent args)
    {
        var strap = args.Strap.Owner;
        // if (!HasComp<MountableComponent>(strap) && !HasComp<VehicleComponent>(strap))
        //     return;

        if (ent.Comp.Target != null)
            return;

        _eye.SetTarget(ent, strap, ent.Comp);
    }

    private void OnUnbuckled(Entity<EyeComponent> ent, ref UnbuckledEvent args)
    {
        if (ent.Comp.Target == args.Strap.Owner)
            _eye.SetTarget(ent, null, ent.Comp);
    }
}
