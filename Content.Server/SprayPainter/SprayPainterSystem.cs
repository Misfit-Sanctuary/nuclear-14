using Content.Server.Atmos.Piping.Components;
using Content.Server.Atmos.Piping.EntitySystems;
using Content.Server.Decals;
using Content.Server.Popups;
using Content.Shared.Administration.Logs;
using Content.Shared.Coordinates.Helpers;
using Content.Shared.Database;
using Content.Shared.Decals;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.SprayPainter;
using Content.Shared.SprayPainter.Components;
using Robust.Server.GameObjects;
using System.Linq;
using System.Numerics;

namespace Content.Server.SprayPainter;

/// <summary>
/// Handles spraying pipes and floor decals using a spray painter.
/// Airlocks are handled in shared.
/// </summary>
public sealed class SprayPainterSystem : SharedSprayPainterSystem
{
    [Dependency] private readonly AtmosPipeColorSystem _pipeColor = default!;
    [Dependency] private readonly ISharedAdminLogManager _adminLogger = default!;
    [Dependency] private readonly DecalSystem _decals = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly TransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SprayPainterComponent, SprayPainterPipeDoAfterEvent>(OnPipeDoAfter);
        SubscribeLocalEvent<SprayPainterComponent, AfterInteractEvent>(OnFloorAfterInteract);

        SubscribeLocalEvent<AtmosPipeColorComponent, InteractUsingEvent>(OnPipeInteract);
    }

    private void OnFloorAfterInteract(Entity<SprayPainterComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || args.Target != null)
            return;

        if (ent.Comp.ColorPickerEnabled)
        {
            PickColor(ent, ref args);
            return;
        }

        if (!args.CanReach || ent.Comp.DecalMode == DecalPaintMode.Off)
            return;

        args.Handled = true;
        var position = args.ClickLocation;
        if (ent.Comp.SnapDecals)
            position = position.SnapToGrid(EntityManager);

        if (ent.Comp.DecalMode == DecalPaintMode.Add)
        {
            position = position.Offset(new Vector2(-0.5f));
            if (!_decals.TryAddDecal(
                    ent.Comp.SelectedDecal,
                    position,
                    out _,
                    ent.Comp.SelectedDecalColor,
                    Angle.FromDegrees(ent.Comp.SelectedDecalAngle),
                    cleanable: false))
                return;

            _adminLogger.Add(LogType.CrayonDraw,
                LogImpact.Low,
                $"{ToPrettyString(args.User):user} painted a {ent.Comp.SelectedDecal}");
        }
        else
        {
            if (_transform.GetGrid(args.ClickLocation) is not { } grid ||
                !TryComp<DecalGridComponent>(grid, out var decalGrid))
            {
                _popup.PopupEntity(Loc.GetString("spray-painter-interact-nothing-to-remove"), args.User, args.User);
                return;
            }

            var decals = _decals.GetDecalsInRange(grid, position.Position, validDelegate: IsDecalValid);
            if (decals.Count == 0)
            {
                _popup.PopupEntity(Loc.GetString("spray-painter-interact-nothing-to-remove"), args.User, args.User);
                return;
            }

            foreach (var decal in decals)
                _decals.RemoveDecal(grid, decal.Index, decalGrid);
        }

        Audio.PlayPvs(ent.Comp.SpraySound, ent);
    }

    private bool IsDecalValid(Decal decal)
    {
        if (!Proto.TryIndex<DecalPrototype>(decal.Id, out var prototype))
            return false;

        return (prototype.Tags.Contains("station") || prototype.Tags.Contains("markings")) &&
               !prototype.Tags.Contains("dirty");
    }

    private void PickColor(Entity<SprayPainterComponent> ent, ref AfterInteractEvent args)
    {
        args.Handled = true;
        if (!args.ClickLocation.IsValid(EntityManager) || _transform.GetGrid(args.ClickLocation) is not { } grid)
            return;

        var clickPosition = args.ClickLocation.Position;
        var decals = _decals.GetDecalsInRange(grid, clickPosition, validDelegate: IsDecalValid);
        if (decals.Count == 0)
        {
            _popup.PopupEntity(Loc.GetString("spray-painter-interact-no-color-pick"), args.User, args.User);
            return;
        }

        var closest = decals.MinBy(decal => Vector2.Distance(decal.Decal.Coordinates, clickPosition)).Decal;
        _popup.PopupEntity(
            Loc.GetString("spray-painter-interact-color-picked", ("id", closest.Id)),
            args.User,
            args.User);

        ent.Comp.SelectedDecalColor = closest.Color;
        ent.Comp.ColorPickerEnabled = false;
        Dirty(ent, ent.Comp);
    }

    private void OnPipeDoAfter(Entity<SprayPainterComponent> ent, ref SprayPainterPipeDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled)
            return;

        if (args.Args.Target is not {} target)
            return;

        if (!TryComp<AtmosPipeColorComponent>(target, out var color))
            return;

        Audio.PlayPvs(ent.Comp.SpraySound, ent);

        _pipeColor.SetColor(target, color, args.Color);

        args.Handled = true;
    }

    private void OnPipeInteract(Entity<AtmosPipeColorComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        if (!TryComp<SprayPainterComponent>(args.Used, out var painter))
            return;

        Color color;
        if (painter.PickedCustomColor is { } custom)
            color = custom;
        else if (painter.PickedColor is { } colorName && painter.ColorPalette.TryGetValue(colorName, out var paletteColor))
            color = paletteColor;
        else
            return;

        var doAfterEventArgs = new DoAfterArgs(EntityManager, args.User, painter.PipeSprayTime, new SprayPainterPipeDoAfterEvent(color), args.Used, target: ent, used: args.Used)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            CancelDuplicate = true,
            // multiple pipes can be sprayed at once just not the same one
            DuplicateCondition = DuplicateConditions.SameTarget,
            NeedHand = true
        };

        args.Handled = DoAfter.TryStartDoAfter(doAfterEventArgs);
    }
}
