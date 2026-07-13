using System.Numerics;
using Content.Shared._Misfits.Silicon;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Client.Silicons.StationAi;

/// <summary>
/// [Changed by MisfitsCrew/Operator] Draws Z.A.X command selection and movement feedback independently of the
/// Station AI camera-static overlay so the feedback follows a mind into a shunted chassis.
/// </summary>
public sealed class StationAiNpcCommandOverlay : Overlay
{
    [Dependency] private readonly IEntityManager _entManager = default!;
    [Dependency] private readonly IPlayerManager _player = default!;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    public StationAiNpcCommandOverlay()
    {
        IoCManager.InjectDependencies(this);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var playerEnt = _player.LocalEntity;
        if (playerEnt == null ||
            !_entManager.TryGetComponent(playerEnt.Value, out StationAiNpcCommanderComponent? commander))
        {
            return;
        }

        DrawMoveTargetPreviews(args.WorldHandle, args, commander);
        DrawSelectedNpcs(args.WorldHandle, args, commander);
    }

    // [Changed by MisfitsCrew/Operator] Shows queued and formation movement destinations for the currently controlled commander.
    private void DrawMoveTargetPreviews(
        DrawingHandleWorld worldHandle,
        in OverlayDrawArgs args,
        StationAiNpcCommanderComponent commander)
    {
        if (commander.MoveTargetPreviews.Count == 0)
            return;

        var xforms = _entManager.System<SharedTransformSystem>();
        var lookups = _entManager.System<EntityLookupSystem>();
        var maps = _entManager.System<SharedMapSystem>();
        var fill = new Color(0.35f, 1f, 0.35f, 0.18f);
        var outline = new Color(0.35f, 1f, 0.35f, 0.55f);

        foreach (var netCoords in commander.MoveTargetPreviews)
        {
            var coords = _entManager.GetCoordinates(netCoords);
            var mapCoords = coords.ToMap(_entManager, xforms);
            if (mapCoords.MapId != args.MapId)
                continue;

            var gridUid = xforms.GetGrid(coords);
            if (gridUid == null ||
                !_entManager.TryGetComponent(gridUid.Value, out MapGridComponent? grid))
            {
                var box = Box2.CenteredAround(mapCoords.Position, Vector2.One);
                worldHandle.DrawRect(box, fill);
                worldHandle.DrawRect(box, outline, filled: false);
                continue;
            }

            var tile = maps.LocalToTile(gridUid.Value, grid, coords);
            var localBounds = lookups.GetLocalBounds(tile, grid.TileSize).Enlarged(-0.05f);
            var gridMatrix = xforms.GetWorldMatrix(gridUid.Value);

            worldHandle.SetTransform(gridMatrix);
            worldHandle.DrawRect(localBounds, fill);
            worldHandle.DrawRect(localBounds, outline, filled: false);
            worldHandle.SetTransform(Matrix3x2.Identity);
        }
    }

    // [Changed by MisfitsCrew/Operator] Highlights selected NPCs for either the core brain or its shunted body.
    private void DrawSelectedNpcs(
        DrawingHandleWorld worldHandle,
        in OverlayDrawArgs args,
        StationAiNpcCommanderComponent commander)
    {
        var xforms = _entManager.System<SharedTransformSystem>();
        var fill = new Color(0.1f, 0.85f, 1f, 0.12f);
        var outline = new Color(0.1f, 0.85f, 1f, 0.85f);

        foreach (var selected in commander.SelectedNpcs)
        {
            if (!_entManager.TryGetComponent(selected, out TransformComponent? xform) ||
                xform.MapID != args.MapId)
            {
                continue;
            }

            var worldPos = xforms.GetWorldPosition(xform);
            if (!args.WorldAABB.Contains(worldPos))
                continue;

            worldHandle.DrawCircle(worldPos, 0.75f, fill);
            worldHandle.DrawCircle(worldPos, 0.75f, outline, filled: false);
        }
    }
}
