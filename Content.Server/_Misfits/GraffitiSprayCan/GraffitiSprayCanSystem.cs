// #Misfits Add - Server system for the graffiti spray can.
// Handles AfterInteract to start the spray do-after, and on completion places the decal via DecalSystem.
// When the entity also has a PaintComponent (e.g. SprayPaint), solution is drained like a normal paint use.
// When no solution container is present (standalone GraffitiSprayCan), the charge counter is used instead.

using Content.Server.Decals;
using Content.Shared._Misfits.GraffitiSprayCan;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Paint;
using Content.Shared.Popups;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using System.Numerics;

namespace Content.Server._Misfits.GraffitiSprayCan;

public sealed class GraffitiSprayCanSystem : EntitySystem
{
    [Dependency] private readonly DecalSystem _decals = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solutionContainer = default!;
    [Dependency] private readonly OpenableSystem _openable = default!;

    public override void Initialize()
    {
        base.Initialize();

        // Handle clicking anywhere with the spray can to start a do-after
        SubscribeLocalEvent<GraffitiSprayCanComponent, AfterInteractEvent>(OnAfterInteract);
        // On do-after completion, place the actual decal
        SubscribeLocalEvent<GraffitiSprayCanComponent, GraffitiSprayCanDoAfterEvent>(OnDoAfter);

        // BUI message: player selected a decal from the window
        Subs.BuiEvents<GraffitiSprayCanComponent>(GraffitiSprayCanUiKey.Key, subs =>
        {
            subs.Event<GraffitiDecalSelectedMessage>(OnDecalSelected);
        });
    }

    /// <summary>
    /// Stores the chosen decal ID on the component when selected via BUI.
    /// </summary>
    private void OnDecalSelected(Entity<GraffitiSprayCanComponent> ent, ref GraffitiDecalSelectedMessage args)
    {
        ent.Comp.SelectedDecalId = args.DecalId;
        Dirty(ent);
    }

    /// <summary>
    /// Starts a graffiti spray do-after only when clicking empty space (no entity target).
    /// Entity-target clicks are left for the PaintSystem to handle normally.
    /// </summary>
    private void OnAfterInteract(Entity<GraffitiSprayCanComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach)
            return;

        // Only handle floor/empty-tile clicks — entity targets are handled by PaintSystem
        if (args.Target.HasValue)
            return;

        // Prevent stacking multiple spray do-afters
        if (ent.Comp.ActiveDoAfter != null)
            return;

        // If the can has an Openable component (spray paint cans), it must be uncapped first
        if (HasComp<OpenableComponent>(ent.Owner) && !_openable.IsOpen(ent.Owner))
        {
            _popup.PopupEntity(Loc.GetString("graffiti-spray-can-not-open"), args.User, args.User);
            return;
        }

        // Check depletion via PaintComponent solution (for SprayPaint) or charge counter (standalone can)
        if (!HasPaintSolution(ent.Owner, out _) && ent.Comp.Charges <= 0)
        {
            _popup.PopupEntity(Loc.GetString("graffiti-spray-can-empty"), args.User, args.User);
            return;
        }

        if (ent.Comp.SelectedDecalId == null)
        {
            _popup.PopupEntity(Loc.GetString("graffiti-spray-can-no-decal-selected"), args.User, args.User);
            return;
        }

        var netCoords = GetNetCoordinates(args.ClickLocation);
        var doAfterArgs = new DoAfterArgs(
            EntityManager,
            args.User,
            ent.Comp.SprayTime,
            new GraffitiSprayCanDoAfterEvent(ent.Comp.SelectedDecalId, netCoords),
            ent.Owner,
            target: null,
            used: ent.Owner)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
        };

        if (!_doAfter.TryStartDoAfter(doAfterArgs, out var id))
            return;

        ent.Comp.ActiveDoAfter = id;
        _audio.PlayPvs(ent.Comp.SpraySound, ent.Owner);
        args.Handled = true;
    }

    /// <summary>
    /// On do-after completion, snap the coordinates to a tile center and place the decal.
    /// Drains solution (SprayPaint path) or decrements charges (standalone can path).
    /// </summary>
    private void OnDoAfter(Entity<GraffitiSprayCanComponent> ent, ref GraffitiSprayCanDoAfterEvent args)
    {
        ent.Comp.ActiveDoAfter = null;

        if (args.Cancelled || args.Handled)
            return;

        // Consume paint — either from solution container (SprayPaint) or charge counter
        if (HasPaintSolution(ent.Owner, out var paintComp))
        {
            // Drain from the same solution the PaintSystem uses so TrashOnSolutionEmpty fires normally
            if (!_solutionContainer.TryGetSolution(ent.Owner, paintComp!.Solution, out _, out var solution))
            {
                _popup.PopupEntity(Loc.GetString("graffiti-spray-can-empty"), args.Args.User, args.Args.User);
                return;
            }
            var drained = solution.RemoveReagent(paintComp.Reagent, paintComp.ConsumptionUnit);
            if (drained <= 0)
            {
                _popup.PopupEntity(Loc.GetString("graffiti-spray-can-empty"), args.Args.User, args.Args.User);
                return;
            }
        }
        else
        {
            // Standalone GraffitiSprayCan — use the charge counter
            if (ent.Comp.Charges <= 0)
            {
                _popup.PopupEntity(Loc.GetString("graffiti-spray-can-empty"), args.Args.User, args.Args.User);
                return;
            }

            ent.Comp.Charges--;
            Dirty(ent);

            if (ent.Comp.Charges <= 0)
                _popup.PopupEntity(Loc.GetString("graffiti-spray-can-ran-out"), args.Args.User, args.Args.User);
        }

        var coordinates = GetCoordinates(args.Coordinates);
        if (!coordinates.IsValid(EntityManager))
            return;

        // Snap to tile center so decals align neatly with tiles
        var snapped = new Vector2(
            MathF.Round(coordinates.X - 0.5f, MidpointRounding.AwayFromZero) + 0.5f,
            MathF.Round(coordinates.Y - 0.5f, MidpointRounding.AwayFromZero) + 0.5f);
        coordinates = coordinates.WithPosition(snapped);

        // Place the decal as cleanable so janitors can remove it
        if (!_decals.TryAddDecal(args.DecalId, coordinates, out _, cleanable: true))
        {
            _popup.PopupEntity(Loc.GetString("graffiti-spray-can-cant-place"), args.Args.User, args.Args.User);
            return;
        }

        args.Handled = true;
    }

    /// <summary>
    /// Returns true if this entity has a PaintComponent backed by a solution container (i.e., it's a SprayPaint can).
    /// </summary>
    private bool HasPaintSolution(EntityUid uid, out PaintComponent? paintComp)
    {
        if (!TryComp(uid, out paintComp))
            return false;

        return _solutionContainer.TryGetSolution(uid, paintComp.Solution, out _, out _);
    }
}
