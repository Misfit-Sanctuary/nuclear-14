using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using Robust.Shared.Map;
using Robust.Shared.Timing;


namespace Content.Shared._Misfits.Buckle;

/// <summary>
/// use case is when placing passangers/drivers for a drivable with default strapOffset isnt enough(sprite is not accomodating, looks weird, ect...)
/// so you need to "manually" define each offset for each direction the sprite faces
/// Should not be used for anything that needs to offset more than 4-6 visible passangers
/// Need to develop another system to have that be feasible(prolly a vehicle made up of multiple entities)
/// </summary>
public sealed partial class DirectionalBuckleOffsetSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private ILogManager _logManager = default!;
    [Dependency] private SharedBuckleSystem _buckle = default!;
    [Dependency] private SharedTransformSystem _xform = default!;

    private ISawmill _sawmill = default!;
//      attempt to correct local pos offsets. Need to figure out how to fix diagonals
//     private static float Diagonal = 1 / MathF.Sqrt(2);
//
//     public static readonly Vector2[] RotMult =
//         [new(1,1), new(2*Diagonal, 0), // south, southEast
//         new(1,-1), new(0, -2*Diagonal) , // east, NorthEast
//         new(-1,-1),new(-2*Diagonal, 0), // north, northwest
//         new(-1,1), new(0, 2*Diagonal)]; // west, southwest

    public override void Initialize()
    {
        base.Initialize();
        _sawmill = _logManager.GetSawmill("strap.directional_buckle");
        SubscribeLocalEvent<DirectionalBuckleOffsetComponent, ComponentStartup>(OnCompStart);
        SubscribeLocalEvent<DirectionalBuckleOffsetComponent, UnstrappedEvent>(OnUnstrapped);
        SubscribeLocalEvent<DirectionalBuckleOffsetComponent, StrappedEvent>(OnStrapped);
        SubscribeLocalEvent<DirectionalBuckleOffsetComponent, BuckleOffsetOverride>(OnBuckleOffsetOverride);
        SubscribeLocalEvent<DirectionalBuckleOffsetComponent, MoveEvent>(OnMove);

    }
    /// <summary>
    /// OnMove we setCoords of all non-invlaid entities by offset
    /// </summary>
    /// <param name="ent"> often some entity being ridden that has specific offsets for the rider(s)</param>
    private void OnMove(Entity<DirectionalBuckleOffsetComponent> ent, ref MoveEvent ev)
    {
        if (ev.OldRotation == ev.NewRotation)
            return;
        var dir = (int) ev.NewRotation.GetDir();
        var entList = ent.Comp.BuckleOffsetSlots;

        for (int i = 0; i < entList.Count; i++)
        {
            if (entList[i] == EntityUid.Invalid)
                continue;
            var offset = ent.Comp.Offsets[i, dir];
            var coords = new EntityCoordinates(ent, offset);
            var entXform = Transform(entList[i]);
            // we do not go by local position since it transforms the offsets by the rotation of parent
            // This makes setting these via yaml alot more annoying. Still an issue with diagonals
            _xform.SetCoordinates(entList[i], entXform, coords, rotation: Angle.Zero);
            entXform.ActivelyLerping = ent.Comp.EnableLerping;
            // _xform.SetLocalPosition(entList[i], offset);
        }

    }

    private const int NumDirections = 8;
    /// <summary>
    /// Init procedure where we fill out arrays Offsets with YamlOffsets/defaults
    /// and BuckleOffsetSlots by defaults (starts "empty" so full of invalidEntityUid)
    /// </summary>
    private void OnCompStart(EntityUid uid, DirectionalBuckleOffsetComponent comp, ComponentStartup args)
    {
        if (!TryComp<StrapComponent>(uid, out var strap))
        {
            _sawmill.Error($"Entity has DirectionalBuckleOffsetComponent but no StrapComponent {MetaData(uid).EntityName} removing comp");
            RemComp<DirectionalBuckleOffsetComponent>(uid);
            return;
        }

        // Offsets is supplied by YamlOffsets where not all directions might have a value
        // so uses strapOffset as a default for missing direction
        var numSlots = comp.YamlOffsets.Length;
        comp.Offsets = new Vector2[numSlots, NumDirections];
        for (var i = 0; i < numSlots; i++)
        {
            for (var j = 0; j < NumDirections; j++)
            {
                if (comp.YamlOffsets[i].TryGetValue((Direction) j, out var pos) && pos != Vector2.Zero)
                {
                    comp.Offsets[i, j] = pos;
                    continue;
                }

                comp.Offsets[i, j] = strap.BuckleOffset;
            }
        }

        comp.BuckleOffsetSlots = new(numSlots);
        for (var i = 0; i < numSlots; i++)
            comp.BuckleOffsetSlots.Add(EntityUid.Invalid);
        Dirty(uid, comp);
    }
    /// <summary>
    /// We add strapped entity to buckleOffsetSlot. Slot(index in array) to use is the first slot with an empty aka invalid entity, so swap that
    /// </summary>
    private void OnStrapped(Entity<DirectionalBuckleOffsetComponent> ent, ref StrappedEvent ev)
    {
        if (!_timing.IsFirstTimePredicted || _timing.ApplyingState)
            return;
        var entList = ent.Comp.BuckleOffsetSlots;
        // gets earliest free index. Treat invaild as "empty"
        var index = entList.IndexOf(EntityUid.Invalid);

        // not an error if this happens. amount of entities a strap supports vs direction offset slots are different
        if (index == -1)
            return;

        entList[index] = ev.Buckle.Owner;
        Dirty(ent, ent.Comp);

    }
    /// <summary>
    /// Swap ent out in buckleOffsetSlot with invalidEntity to mark as empty
    /// </summary>
    /// <remarks>
    /// notably we dont use another data structure like a hashset
    /// we want to preserve the ordering of any enitites in the buckled list
    /// since that index is what determines their offset and "spot" in like a car
    /// person riding shotgun needs a different offset than the driver
    /// also iterating through a list is less expensive than hashset/dictionary FOR SMALL SIZED lists
    /// </remarks>
    private void OnUnstrapped(Entity<DirectionalBuckleOffsetComponent> ent, ref UnstrappedEvent ev)
    {
        if (!_timing.IsFirstTimePredicted || _timing.ApplyingState)
            return;

        var entList = ent.Comp.BuckleOffsetSlots;
        var index = entList.IndexOf(ev.Buckle.Owner);
        if (index == -1)
        {
            _sawmill.Error($"Entity {MetaData(ev.Buckle.Owner).EntityName} could not be found in strap. Shouldnt be possible");
            return;
        }

        entList[index] = EntityUid.Invalid;
        Dirty(ent, ent.Comp);
    }
    /// <summary>
    /// Override for BuckleTransformCheck <see cref="SharedBuckleSystem.BuckleTransformCheck"/>
    /// does the same thing but we use SetCoordinates instead of SetLocalPosition
    /// </summary>
    private void OnBuckleOffsetOverride(
        Entity<DirectionalBuckleOffsetComponent> ent,
        ref BuckleOffsetOverride ev
    )
    {

        if (ev.Handled)
            return;
        // Clientside will be lerping so their pos will not match
        // the direction slot they are supposed to be, causing fake unbuckling
        if (Transform(ev.Buckle).ActivelyLerping)
        {
            ev.Handled = true;
            return;
        }
        var entList = ent.Comp.BuckleOffsetSlots;
        var index = entList.IndexOf(ev.Buckle.Owner);
        if (index == -1)
        {
            _sawmill.Error($"Entity {MetaData(ev.Buckle.Owner).EntityName} could not be found in strap. Shouldnt be possible");
            return;
        }

        var dir = (int) Transform(ev.Strap).LocalRotation.GetDir();
        var offset = ent.Comp.Offsets[index, dir];
        DirectionalTransformCheck(ev.Buckle, ev.Strap, ev.Xform, offset);
        ev.Handled = true;
    }

    private void DirectionalTransformCheck(
        Entity<BuckleComponent> buckle,
        Entity<StrapComponent> strap,
        TransformComponent xform,
        Vector2 offset)
    {
        var strapComp = strap.Comp;
        var delta = (xform.LocalPosition - offset).LengthSquared();

        if (!(delta > 1e-5))
            return;

        if (strapComp.LockToBuckleOffset)
        {
            var coords = new EntityCoordinates(strap, offset);
            _xform.SetCoordinates(buckle.Owner, Transform(buckle.Owner), coords, rotation: Angle.Zero);
            return;
        }
        _buckle.Unbuckle(buckle, (strap.Owner, strapComp), null);
    }
}
