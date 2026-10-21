using System.Runtime.CompilerServices;
using Content.Shared.Buckle.Components;
using Content.Shared.Stunnable;


namespace Content.Shared.Buckle;


public partial class SharedBuckleSystem
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void BuckleRequiredSetup(Entity<BuckleComponent> buckle, Entity<StrapComponent> strap)
    {
        _joints.SetRelay(buckle, strap);
        SetStance(buckle, strap);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SetStance(Entity<BuckleComponent> buckle, Entity<StrapComponent> strap)
    {
        switch (strap.Comp.Position)
        {
            case StrapPosition.Stand:
                _standing.Stand(buckle, force: true);
                break;
            case StrapPosition.Down:
                _standing.Down(buckle, false, false);
                break;
        }
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void BuckleVisual(Entity<BuckleComponent> buckle, Entity<StrapComponent> strap)
    {
        _audio.PlayPredicted(strap.Comp.BuckleSound, strap, buckle);
        Appearance.SetData(strap, StrapVisuals.State, true);
        Appearance.SetData(buckle, BuckleVisuals.Buckled, true);
        _rotationVisuals.SetHorizontalAngle(buckle.Owner, strap.Comp.Rotation);
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void UnbuckleCoord(Entity<BuckleComponent> buckle, Entity<StrapComponent> strap)
    {
        var buckleXform = Transform(buckle);
        var oldBuckledXform = Transform(strap);
        // if (buckleXform.ParentUid == strap.Owner && !Terminating(buckleXform.ParentUid))
        //     return;
        // We use EntityCoords to only get offset but to also get the parent of the strap
        // which will be grid or map most of the time
        var entCoord = oldBuckledXform.Coordinates.Offset(strap.Comp.BuckleOffset);
        var newParent = Transform(entCoord.EntityId); // EntityId of entcoords is parent
        _transform.SetCoordinates(buckle, buckleXform, entCoord,Angle.Zero, true, newParent, oldBuckledXform);

        buckleXform.ActivelyLerping = false; // I guess to give that POP effect
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void UnbuckleVisuals(Entity<BuckleComponent> buckle, Entity<StrapComponent> strap)
    {
        _rotationVisuals.ResetHorizontalAngle(buckle.Owner);
        Appearance.SetData(strap, StrapVisuals.State, strap.Comp.BuckledEntities.Count != 0);
        Appearance.SetData(buckle, BuckleVisuals.Buckled, false);
        _audio.PlayPredicted(strap.Comp.UnbuckleSound, strap, buckle);
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void UnbuckleRequiredSetup(Entity<BuckleComponent> buckle, Entity<StrapComponent> strap)
    {
        SetBuckledTo(buckle, null);
        _joints.RefreshRelay(buckle);
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void UnbuckleKnockdown(Entity<BuckleComponent> buckle, Entity<StrapComponent> strap)
    {
        if (HasComp<KnockedDownComponent>(buckle) || _mobState.IsIncapacitated(buckle))
            _standing.Down(buckle, playSound: false);
        else
            _standing.Stand(buckle);
    }
}










// why was it like this lmao?
// [MethodImpl(MethodImplOptions.AggressiveInlining)]
// public void UnbuckleCoordOld(Entity<BuckleComponent> buckle, Entity<StrapComponent> strap)
// {
//
//     var buckleXform = Transform(buckle);
//     var oldBuckledXform = Transform(strap);
//
//     if (buckleXform.ParentUid == strap.Owner && !Terminating(buckleXform.ParentUid))
//         return;
//
//     var entCoord = oldBuckledXform.Coordinates.Offset(strap.Comp.BuckleOffset);
//     var newParent = Transform(entCoord.EntityId);
//     //_transform.SetCoordinates(buckle, buckleXform, entCoord,Angle.Zero, true, newParent, oldBuckledXform);
//     _transform.PlaceNextTo((buckle, buckleXform), (strap.Owner, oldBuckledXform));
//     buckleXform.ActivelyLerping = false;
//
//     var oldBuckledToWorldRot = _transform.GetWorldRotation(strap);
//     _transform.SetWorldRotationNoLerp((buckle, buckleXform), oldBuckledToWorldRot);
//
//     // TODO: This is doing 4 moveevents this is why I left the warning in, if you're going to remove it make it only do 1 moveevent.
//     if (strap.Comp.BuckleOffset != Vector2.Zero)
//         _transform.SetCoordinates(buckle, buckleXform, oldBuckledXform.Coordinates.Offset(strap.Comp.BuckleOffset));
//
// }
