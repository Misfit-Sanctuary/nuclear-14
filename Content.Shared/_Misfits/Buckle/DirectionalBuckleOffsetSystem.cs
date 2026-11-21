using System.Numerics;
using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using Robust.Shared.Timing;


namespace Content.Shared._Misfits.Buckle;

public sealed partial class DirectionalBuckleOffsetSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private ILogManager _logManager = default!;
    [Dependency] private SharedBuckleSystem _buckle = default!;
    [Dependency] private SharedTransformSystem _xform = default!;
    private ISawmill _sawmill = default!;

    private static float Diagonal = 1/MathF.Sqrt(2);

    public static readonly Vector2[] RotMult =
        [new(1,1), new(1, 1), // south, southEast
        new(1,-1), new(1, 1) , // east, NorthEast
        new(-1,-1),new(1, 1), // north, northwest
        new(-1,1), new(1, 1)]; // west, southwest

    // var dx = cos * vec.X - sin * vec.Y;
    // var dy = sin * vec.X + cos * vec.Y;
    // dx = 1/sqrt(2)*X - (-1/sqrt(2))*Y
    // dx = .5, X = dx*p
    // dx = cos * dx*p - sin * dy*d
    // dx = cos * dx*p - sin * dy*d
    // dx = 1/sqrt(2) * dx*p - (-1/sqrt(2)) * dy*d
    // dx = p (1/sqrt(2) * dx - (-1/sqrt(2)) * dy)
    // dx / (1/sqrt(2) * dx - (-1/sqrt(2)) * dy) = p
    //dx - 1/sqrt(2) * dx*p = - (-1/sqrt(2)) * dy*d

    // dy = sin*dx*p + cos*dy*d
    // dy = -1/sqrt(2) * dx*p + (1/sqrt(2)) * dy*p
    // dy = p(-1/sqrt(2) * dx + (1/sqrt(2)) * dy)
    // p = dy/(-1/sqrt(2) * dx + (1/sqrt(2)) * dy)

    // dx = 1/sqrt(2) * dx*p - (-1/sqrt(2)) * dy*d
    // dx = 1/-sqrt(2)*X + (1/sqrt(2))*Y

    private static Vector2 DiagMult(Vector2 offset, int index)
    {
        // dx / (1/sqrt(2) * dx - (-1/sqrt(2)) * dy) = p
        // p = dy/(-1/sqrt(2) * dx + (1/sqrt(2)) * dy)
        var xp = offset.X / (Diagonal * offset.X - Diagonal * offset.Y);
        var yp = offset.Y / (Diagonal * offset.X + Diagonal * offset.Y);

        var x = offset.X * RotMult[index].X + offset.X * RotMult[index].Y;
        var y = offset.Y * RotMult[index].X + offset.Y * RotMult[index].Y;
        return new Vector2(x, y);
    }
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
            //_xform.SetCoordinates()
            _xform.SetLocalPosition(entList[i], offset);
        }

    }

    private const int NumDirections = 8;
    /// <summary>
    /// Init procedure where we fill out arrays Offsets and BuckleOffsetSlots
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
        comp.Offsets = new Vector2[numSlots,NumDirections];
        for (var i = 0; i < numSlots; i++)
        {
            for (var j = 0; j < NumDirections; j++)
            {
                if (comp.YamlOffsets[i].TryGetValue((Direction)j, out var pos))
                {

                    comp.Offsets[i, j] = pos*RotMult[j];
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
    /// We add strapped entity to buckleOffsetSlot. Slot to use is the first slot with an invalid entity
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
    /// We remove entity from the buckleOffsetSlot, replacing it with invalid entity
    /// notably we dont use another data structure like a hashset
    /// to preserve the ordering of any enitites in the buckled list
    /// since that index is what determines their offset
    /// </summary>
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
    /// We offset the entity by the direction it is facing and what slot it occupies currently
    /// </summary>
    private void OnBuckleOffsetOverride(
        Entity<DirectionalBuckleOffsetComponent> ent,
        ref BuckleOffsetOverride ev
    )
    {
        if (ev.Handled)
            return;
        var entList = ent.Comp.BuckleOffsetSlots;
        var index = entList.IndexOf(ev.Buckle.Owner);
        if (index == -1)
        {
            _sawmill.Error($"Entity {MetaData(ev.Buckle.Owner).EntityName} could not be found in strap. Shouldnt be possible");
            return;
        }

        var dir = (int) Transform(ev.Strap).LocalRotation.GetDir();
        var offset = ent.Comp.Offsets[index, dir];
        _buckle.BuckleTransformCheck(ev.Buckle, ev.Strap, ev.Xform, offset);
        ev.Handled = true;
    }
}
