using Content.Server._Misfits.Weapons.Ranged.Flamer;
using Content.Server.Chemistry.TileReactions;
using Content.Server.Fluids.EntitySystems;
using Content.Server.Destructible;
using Content.Server.Damage.Components;
using Content.Shared._Misfits.Weapons.Throwable;
using Content.Shared.Atmos;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Examine;
using Content.Shared.Fluids.Components;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Throwing;
using Content.Shared.Temperature;
using Content.Shared.Stacks;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Prototypes;

namespace Content.Server._Misfits.Weapons.Throwable;

/// <summary>
/// Converts bottles into Molotovs, handles ignition and impact, and chooses fire spread from
/// the amount and type of fuel inside the original bottle.
/// </summary>
public sealed class MolotovSystem : EntitySystem
{
    private static readonly ProtoId<ReagentPrototype> WeldingFuel = "WeldingFuel";
    private static readonly ProtoId<ReagentPrototype> Ethanol = "Ethanol";
    private static readonly ProtoId<ReagentPrototype> Napalm = "Napalm";
    private static readonly ProtoId<ReagentPrototype> Oil = "Oil";
    private static readonly ProtoId<ReagentPrototype> BaseAlcohol = "BaseAlcohol";

    [Dependency] private readonly FlamerLineSystem _flamer = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly PuddleSystem _puddle = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solution = default!;
    [Dependency] private readonly SharedStackSystem _stack = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MolotovComponent, LandEvent>(OnLand);
        SubscribeLocalEvent<MolotovComponent, ThrowDoHitEvent>(OnHit);
        SubscribeLocalEvent<MolotovComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<MolotovComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<MolotovConvertibleComponent, AfterInteractUsingEvent>(OnAddWick);
    }

    private void OnAddWick(Entity<MolotovConvertibleComponent> ent, ref AfterInteractUsingEvent args)
    {
        if (args.Handled || !args.CanReach || HasComp<MolotovComponent>(ent) ||
            !TryComp<StackComponent>(args.Used, out var stack) || stack.StackTypeId != "Cloth")
            return;

        args.Handled = true;
        var molotov = AddComp<MolotovComponent>(ent);
        molotov.Solution = ent.Comp.Solution;
        molotov.WickOffset = ent.Comp.WickOffset;
        // MolotovSystem owns impact spilling once the wick has been fitted.
        RemComp<DamageOnLandComponent>(ent);
        RemComp<DestructibleComponent>(ent);
        RemComp<SpillableComponent>(ent);
        _stack.SetCount(args.Used, stack.Count - 1, stack);
        _popup.PopupEntity(Loc.GetString("molotov-wick-success"), ent, args.User);
    }

    private void OnInteractUsing(Entity<MolotovComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        var isHot = new IsHotEvent();
        RaiseLocalEvent(args.Used, isHot);
        if (!isHot.IsHot)
            return;

        args.Handled = true;
        if (ent.Comp.Ignited)
            return;

        ent.Comp.Ignited = true;
        Dirty(ent);
        _audio.PlayPvs(ent.Comp.IgniteSound, ent.Owner);
        _popup.PopupEntity(Loc.GetString("molotov-light-success"), ent.Owner, args.User, PopupType.SmallCaution);
    }

    private void OnExamined(Entity<MolotovComponent> ent, ref ExaminedEvent args)
    {
        if (ent.Comp.Ignited)
            args.PushMarkup(Loc.GetString("molotov-examine-lit"));
    }

    private void OnLand(Entity<MolotovComponent> ent, ref LandEvent args)
    {
        Break(ent, args.User);
    }

    private void OnHit(Entity<MolotovComponent> ent, ref ThrowDoHitEvent args)
    {
        Break(ent, args.Component.Thrower);
    }

    private void Break(Entity<MolotovComponent> ent, EntityUid? user)
    {
        if (ent.Comp.Broken)
            return;

        ent.Comp.Broken = true;
        var coordinates = Transform(ent).Coordinates;
        var flammableVolume = 0f;
        var napalmVolume = 0f;
        var oilVolume = 0f;
        var capacity = 0f;

        if (_solution.TryGetSolution(ent.Owner, ent.Comp.Solution, out var solutionEntity, out var solution))
        {
            capacity = solution.MaxVolume.Float();
            foreach (var (reagentId, quantity) in solution.Contents)
            {
                if (IsFlammable(reagentId.Prototype))
                    flammableVolume += quantity.Float();

                if (reagentId.Prototype == Napalm)
                    napalmVolume += quantity.Float();
                else if (reagentId.Prototype == Oil)
                    oilVolume += quantity.Float();
            }

            var spilled = _solution.SplitSolution(solutionEntity.Value, solution.Volume);
            _puddle.TrySplashSpillAt(ent.Owner, coordinates, spilled, out _, user: user);
        }

        // Unlit bottles only make an ordinary glass-breaking noise. The custom Molotov impact
        // sound is reserved for an ignited wick so throwing an unlit bottle cannot sound fiery.
        // Play at the coordinates because deleting the bottle would stop entity-bound audio.
        var impactSound = ent.Comp.Ignited ? ent.Comp.BreakSound : ent.Comp.UnlitBreakSound;
        _audio.PlayPvs(impactSound, coordinates);

        if (ent.Comp.Ignited && flammableVolume >= ent.Comp.MinimumFuel)
        {
            var range = GetSpreadRange(ent.Comp, flammableVolume, capacity);
            var firePrototype = GetFirePrototype(ent.Comp, napalmVolume, oilVolume, capacity);
            _flamer.SpawnDiamond(firePrototype, coordinates, range);
        }

        QueueDel(ent.Owner);
    }

    private bool IsFlammable(ProtoId<ReagentPrototype> reagentId)
    {
        var reagent = _prototype.Index(reagentId);
        if (reagent.TileReactions.Exists(reaction => reaction is FlammableTileReaction))
            return true;

        if (reagentId == WeldingFuel || reagentId == Ethanol || reagentId == Napalm)
            return true;

        if (reagent.Parents == null)
            return false;

        foreach (var parent in reagent.Parents)
        {
            if (parent == BaseAlcohol || IsFlammable(parent))
                return true;
        }

        return false;
    }

    private static int GetSpreadRange(MolotovComponent component, float fuelVolume, float capacity)
    {
        var fillFraction = capacity > 0f ? fuelVolume / capacity : 0f;
        if (fillFraction >= component.MaximumSpreadFraction)
            return component.MaximumSpreadRange;

        return fillFraction >= component.MediumSpreadFraction ? 1 : 0;
    }

    private static EntProtoId GetFirePrototype(
        MolotovComponent component,
        float napalmVolume,
        float oilVolume,
        float capacity)
    {
        if (napalmVolume >= capacity * component.NapalmFuelFraction)
            return component.NapalmFireTilePrototype;

        return oilVolume >= capacity * component.OilFuelFraction
            ? component.OilFireTilePrototype
            : component.FireTilePrototype;
    }
}
