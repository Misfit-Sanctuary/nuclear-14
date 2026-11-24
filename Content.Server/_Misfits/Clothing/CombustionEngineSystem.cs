using Content.Server.Chemistry.EntitySystems;
using Content.Server.Damage;
using Content.Server.Explosion.EntitySystems;
using Content.Server.Fluids.EntitySystems;
using Content.Server.Verbs;
using Content.Shared._Misfits.Clothing;
using Content.Shared._Misfits.PowerArmor;
using Content.Shared.Audio;
using Content.Shared.Clothing;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.Database;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Inventory;
using Content.Shared.Physics;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Server.Containers;
using Robust.Server.Player;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics;
using Robust.Shared.Player;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._Misfits.Clothing;

public sealed class CombustionEngineSystem : EntitySystem
{
    [Dependency] private readonly SharedSolutionContainerSystem _solution = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedAmbientSoundSystem _ambient = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly ContainerSystem _containers = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly ClothingSpeedModifierSystem _clothingSpeedModifier = default!;
    [Dependency] private readonly ExplosionSystem _explosion = default!;
    [Dependency] private readonly SmokeSystem _smoke = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CombustionEngineComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<CombustionEngineComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<CombustionEngineComponent, EntRemovedFromContainerMessage>(OnRemoved);
        SubscribeLocalEvent<CombustionEngineComponent, EntInsertedIntoContainerMessage>(OnInserted);
        SubscribeLocalEvent<CombustionEngineComponent, GetVerbsEvent<Verb>>(OnGetVerbs);
        SubscribeLocalEvent<CombustionEngineComponent, GetVerbsEvent<AlternativeVerb>>(OnGetAltVerbs);

        SubscribeLocalEvent<DelayedExplosionOnBreakComponent, EntityTerminatingEvent>(OnDelayedExplosionTerminating);
        SubscribeLocalEvent<DelayedExplosionOnBreakComponent, DamageChangedEvent>(OnDelayedExplosionDamageChanged);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<CombustionEngineComponent>();
        while (query.MoveNext(out var uid, out var engine))
        {
            if (!engine.EngineRunning)
                continue;

            var wearer = GetWearer(uid);
            if (wearer == null || !IsMoving(wearer.Value) || !HasUsableFuel(uid, engine))
            {
                if (wearer != null && !HasUsableFuel(uid, engine))
                    StopEngine(uid, engine, wearer.Value, true);
                continue;
            }

            var fuelToUse = engine.FuelUsePerSecond.Float() * frameTime + engine.FuelAccumulator;
            var wholeFuel = FixedPoint2.New(MathF.Floor(fuelToUse));
            engine.FuelAccumulator = fuelToUse - wholeFuel.Float();

            if (wholeFuel > FixedPoint2.Zero && !TryConsumeFuel(uid, engine, wholeFuel))
                StopEngine(uid, engine, wearer.Value, true);
        }
    }

    private void OnInit(Entity<CombustionEngineComponent> ent, ref ComponentInit args)
    {
        SetEngineState(ent, ent.Comp.EngineRunning); // init engine state
    }

    private void OnExamined(Entity<CombustionEngineComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange ||
            !_solution.TryGetSolution(ent.Owner, ent.Comp.FuelSolution, out _, out var fuelSolution))
            return;

        var current = fuelSolution.GetTotalPrototypeQuantity(ent.Comp.FuelReagent);
        args.PushMarkup(Loc.GetString("combustion-engine-examine-fuel", ("current", current), ("max", fuelSolution.MaxVolume))); // show fuel amount
    }

    private void OnInserted(Entity<CombustionEngineComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        if (args.Container.ID == "back")
            SetEngineState(ent, false); // engine off when equipped
    }

    private void OnRemoved(Entity<CombustionEngineComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        if (args.Container.ID == "back")
            SetEngineState(ent, false); // engine off when unequipped
    }

    private void SetEngineState(Entity<CombustionEngineComponent> ent, bool running)
    {
        ent.Comp.EngineRunning = running;
        _ambient.SetAmbience(ent, running);
        _appearance.SetData(ent, CombustionEngineVisuals.Running, running);
        UpdateSpeedModifier(ent);
        Dirty(ent);
    }

    private EntityUid? GetWearer(EntityUid uid)
    {
        if (!_inventory.TryGetContainingSlot(uid, out _) ||
            !_containers.TryGetContainingContainer(uid, out var container) ||
            !HasComp<InventoryComponent>(container.Owner))
            return null;

        return container.Owner; // find wearer via inventory
    }

    private bool IsMoving(EntityUid wearer)
    {
        return TryComp<PhysicsComponent>(wearer, out var physics) && physics.LinearVelocity.LengthSquared() > 0.01f; // check velocity
    }

    private bool HasUsableFuel(EntityUid uid, CombustionEngineComponent engine)
    {
        return _solution.TryGetSolution(uid, engine.FuelSolution, out _, out var fuelSolution) &&
            fuelSolution.GetTotalPrototypeQuantity(engine.FuelReagent) > FixedPoint2.Zero; // check fuel > 0
    }

    private bool TryConsumeFuel(EntityUid uid, CombustionEngineComponent engine, FixedPoint2 amount)
    {
        if (!_solution.TryGetSolution(uid, engine.FuelSolution, out var fuelSoln, out var fuelSolution))
            return false;

        var available = fuelSolution.GetTotalPrototypeQuantity(engine.FuelReagent);
        if (available <= FixedPoint2.Zero)
            return false;

        var consumed = FixedPoint2.Min(amount, available);
        if (!_solution.RemoveReagent(fuelSoln.Value, engine.FuelReagent, consumed))
            return false;

        return available > amount; // true if fuel remains
    }

    private void StopEngine(EntityUid uid, CombustionEngineComponent engine, EntityUid wearer, bool popup = false)
    {
        if (!engine.EngineRunning)
            return;

        SetEngineState(new(uid, engine), false);

        if (popup)
            _popup.PopupEntity("The combustion engine sputters and dies - out of fuel!", uid, wearer);
    }

    private void OnGetVerbs(Entity<CombustionEngineComponent> ent, ref GetVerbsEvent<Verb> args)
    {
        if (!TryComp<ActorComponent>(args.User, out var actor) ||
            GetWearer(ent.Owner) != args.User ||
            actor.PlayerSession == null)
            return;

        var user = args.User;
        var toggleVerb = new Verb
        {
            Text = ent.Comp.EngineRunning ? "Turn Off Engine" : "Turn On Engine",
            Icon = new SpriteSpecifier.Texture(new("/Textures/Interface/VerbIcons/zap.svg.192dpi.png")),
            Act = () =>
            {
                var running = !ent.Comp.EngineRunning;
                ent.Comp.EngineRunning = running;
                _ambient.SetAmbience(ent, running);
                _appearance.SetData(ent, CombustionEngineVisuals.Running, running);
                UpdateSpeedModifier(ent);
                Dirty(ent);
                _popup.PopupEntity(running
                    ? "The combustion engine roars to life."
                    : "The combustion engine winds down and falls silent.",
                    ent.Owner, user);
            },
            Impact = LogImpact.Low,
        };

        args.Verbs.Add(toggleVerb);
    }

    private void OnGetAltVerbs(Entity<CombustionEngineComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanInteract || !args.CanAccess ||
            !TryComp<ActorComponent>(args.User, out var actor) ||
            GetWearer(ent.Owner) != args.User ||
            actor.PlayerSession == null)
            return;

        var user = args.User;
        var toggleVerb = new AlternativeVerb
        {
            Text = ent.Comp.EngineRunning ? "Turn Off Engine" : "Turn On Engine",
            Icon = new SpriteSpecifier.Texture(new("/Textures/Interface/VerbIcons/zap.svg.192dpi.png")),
            Act = () =>
            {
                var running = !ent.Comp.EngineRunning;
                ent.Comp.EngineRunning = running;
                _ambient.SetAmbience(ent, running);
                _appearance.SetData(ent, CombustionEngineVisuals.Running, running);
                UpdateSpeedModifier(ent);
                Dirty(ent);
                _popup.PopupEntity(running
                    ? "The combustion engine roars to life."
                    : "The combustion engine winds down and falls silent.",
                    ent.Owner, user);
            },
        };

        args.Verbs.Add(toggleVerb);
    }

    private void UpdateSpeedModifier(EntityUid uid)
    {
        if (!TryComp<CombustionEngineComponent>(uid, out var engine))
            return;

        var modifier = engine.EngineRunning ? 4.5f : 3.25f;
        _clothingSpeedModifier.SetSpeedModifier(uid, modifier, modifier); // 90% on, 65% off
    }

    private void OnDelayedExplosionTerminating(Entity<DelayedExplosionOnBreakComponent> ent, ref EntityTerminatingEvent args)
    {
        if (ent.Comp.Triggered)
            return;

        ent.Comp.Triggered = true;

        var position = Transform(ent).Coordinates;
        Timer.Spawn(TimeSpan.FromSeconds(ent.Comp.Delay), () =>
        {
            _explosion.TriggerExplosive(ent.Owner); // explode on break
        });
    }

    private void OnDelayedExplosionDamageChanged(Entity<DelayedExplosionOnBreakComponent> ent, ref DamageChangedEvent args)
    {
        if (ent.Comp.Triggered)
            return;

        if (!TryComp<PowerArmorIntegrityComponent>(ent.Owner, out var paComp))
            return;

        if (!TryComp<DamageableComponent>(ent.Owner, out var dmgComp))
            return;

        var integrity = FixedPoint2.Max(paComp.MaxIntegrity - dmgComp.TotalDamage, 0);

        if (!paComp.SteamWarned && integrity > 0 && integrity <= paComp.MaxIntegrity * 0.25f)
        {
            paComp.SteamWarned = true;
            Dirty(ent.Owner, paComp);

            var position = Transform(ent).Coordinates;
            var steam = Spawn("Smoke", position);
            _smoke.StartSmoke(steam, new Solution(), 5f, 15);

            if (_containers.TryGetContainingContainer(ent.Owner, out var container) &&
                HasComp<InventoryComponent>(container.Owner))
            {
                _popup.PopupEntity("WARNING: Steam venting detected! Armor integrity at 25%!", container.Owner, container.Owner, PopupType.LargeCaution);
            }
        }

        if (integrity <= 0)
        {
            ent.Comp.Triggered = true;

            if (_containers.TryGetContainingContainer(ent.Owner, out var container) &&
                HasComp<InventoryComponent>(container.Owner))
            {
                _popup.PopupEntity("WARNING: Armor breach detected! Combustion engine going runaway - detonation in 3 seconds!", container.Owner, container.Owner, PopupType.LargeCaution);
            }

            var position = Transform(ent).Coordinates;
            Timer.Spawn(TimeSpan.FromSeconds(ent.Comp.Delay), () =>
            {
                _explosion.TriggerExplosive(ent.Owner);
            });
        }
    }
}