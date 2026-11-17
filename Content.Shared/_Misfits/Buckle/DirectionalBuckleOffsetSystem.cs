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
    private ISawmill _sawmill = default!;
    public override void Initialize()
    {
        base.Initialize();
        _sawmill = _logManager.GetSawmill("strap.directional_buckle");
        SubscribeLocalEvent<DirectionalBuckleOffsetComponent, ComponentStartup>(OnCompStart);
        SubscribeLocalEvent<DirectionalBuckleOffsetComponent, UnstrappedEvent>(OnUnstrapped);
        SubscribeLocalEvent<DirectionalBuckleOffsetComponent, StrappedEvent>(OnStrapped);
        SubscribeLocalEvent<DirectionalBuckleOffsetComponent, BuckleOffsetOverride>(OnBuckleOffsetOverride);

    }

    private const int NumDirections = 7;
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
    /// We add strapped entity to buckleOffsetSlot. Slot to use is the first slot with an invalid entity
    /// </summary>
    private void OnStrapped(Entity<DirectionalBuckleOffsetComponent> ent, ref StrappedEvent ev)
    {
        if (!_timing.IsFirstTimePredicted || _timing.ApplyingState)
            return;
        for (var i = 0; i < ent.Comp.BuckleOffsetSlots.Count; i++)
            if (ent.Comp.BuckleOffsetSlots[i] == EntityUid.Invalid)
            {
                ent.Comp.BuckleOffsetSlots[i] = ev.Buckle.Owner;
                var offset = ent.Comp.Offsets[i, (int) ev.Rotation.GetDir()];
                ev.RelativeOffset = offset;
                Dirty(ent, ent.Comp);
                return;
            }
        // this is possible since max amount of entities on a strap can be higher than supplied offsets
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
        DebugTools.Assert(ent.Comp.BuckleOffsetSlots.Contains(ev.Buckle.Owner));
        for (var i = 0; i < ent.Comp.BuckleOffsetSlots.Count; i++)
            if ( ent.Comp.BuckleOffsetSlots[i] == ev.Buckle.Owner)
            {
                ent.Comp.BuckleOffsetSlots[i] = EntityUid.Invalid;
                Dirty(ent, ent.Comp);
                return;
            }
        _sawmill.Error($"Entity {MetaData(ev.Buckle.Owner).EntityName} could not be found in strap. Shouldnt be possible when unstrapped called");
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
        var dir = ev.Xform.LocalRotation.GetDir();
        var count = ent.Comp.BuckleOffsetSlots.Count;
        for (var i = 0; i < count; i++)
        {
            if (ent.Comp.BuckleOffsetSlots[i] != ev.Buckle.Owner)
                continue;

            var offset = ent.Comp.Offsets[i, (int) dir];
            _buckle.BuckleTransformCheck(ev.Buckle, ev.Strap, ev.Xform, offset);
            ev.Handled = true;
            return;
        }
        _sawmill.Error($"Entity {MetaData(ev.Buckle.Owner).EntityName} could not be found in strap. Shouldnt be possible");
    }
}
