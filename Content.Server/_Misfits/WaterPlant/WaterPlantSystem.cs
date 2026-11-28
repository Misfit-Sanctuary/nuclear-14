// #Cythisiax Added - Clean water is meant to be hard to come by in the wastes.
// Each working treatment machine slowly fills its own tank, but only while it has power.
// Cut the power or wreck the machine and the water stops. Fixing a broken machine is set
// up in the yml. No water plant shows on the map until an admin turns one on with the
// waterplantmarker command, and it goes away again when the round ends. The marker stays
// on the spot, so it keeps working when the machine there breaks or gets fixed.

using Content.Server.Administration;
using Content.Server.Chemistry.Containers.EntitySystems;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared.Administration;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.GameTicking;
using Robust.Shared.Console;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Misfits.WaterPlant;

[RegisterComponent]
public sealed partial class WaterPurifierComponent : Component
{
    [DataField] public string Solution = "well";
    [DataField] public ProtoId<ReagentPrototype> Reagent = "Water";
    [DataField] public FixedPoint2 Amount = 1;
    [DataField] public TimeSpan Interval = TimeSpan.FromSeconds(1);

    public TimeSpan NextUpdate;
    public bool Running;
    public Entity<SolutionComponent>? SolutionRef;
}

public sealed class WaterPurifierSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly PowerReceiverSystem _power = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SolutionContainerSystem _solutions = default!;

    public static readonly HashSet<string> TreatmentMachines = new()
    {
        "N14MachineWaterTreatment",
        "N14MachineWaterTreatmentBroken",
    };

    public MapCoordinates? Marker { get; private set; }

    private readonly HashSet<Entity<WaterPurifierComponent>> _nearby = new();

    public override void Initialize()
    {
        SubscribeLocalEvent<WaterPurifierComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => Marker = null);
    }

    public override void Update(float frameTime)
    {
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WaterPurifierComponent, ApcPowerReceiverComponent, SolutionContainerManagerComponent>();
        while (query.MoveNext(out var uid, out var purifier, out var receiver, out var manager))
        {
            if (now < purifier.NextUpdate)
                continue;

            purifier.NextUpdate = now + purifier.Interval;
            purifier.Running = _power.IsPowered(uid, receiver);
            if (!purifier.Running ||
                !_solutions.ResolveSolution((uid, manager), purifier.Solution, ref purifier.SolutionRef, out var solution))
            {
                continue;
            }

            var amount = FixedPoint2.Min(solution.AvailableVolume, purifier.Amount);
            if (amount > FixedPoint2.Zero)
                _solutions.TryAddReagent(purifier.SolutionRef.Value, purifier.Reagent, amount);
        }
    }

    public bool IsTreatmentMachine(EntityUid uid)
    {
        return MetaData(uid).EntityPrototype?.ID is { } id && TreatmentMachines.Contains(id);
    }

    public void SetMarker(EntityUid machine)
    {
        Marker = _transform.GetMapCoordinates(machine);
    }

    public void ClearMarker()
    {
        Marker = null;
    }

    public bool IsMarkerRunning()
    {
        if (Marker is not { } marker)
            return false;

        _nearby.Clear();
        _lookup.GetEntitiesInRange(marker, 0.5f, _nearby);
        foreach (var purifier in _nearby)
        {
            if (purifier.Comp.Running)
                return true;
        }

        return false;
    }

    public EntityUid? FindNearestMachine(EntityUid near, float range)
    {
        var coords = _transform.GetMapCoordinates(near);
        EntityUid? best = null;
        var bestDistance = float.MaxValue;
        foreach (var uid in _lookup.GetEntitiesInRange(coords, range))
        {
            if (!IsTreatmentMachine(uid))
                continue;

            var distance = (_transform.GetMapCoordinates(uid).Position - coords.Position).Length();
            if (distance >= bestDistance)
                continue;

            best = uid;
            bestDistance = distance;
        }

        return best;
    }

    private void OnExamined(Entity<WaterPurifierComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        args.PushMarkup(Loc.GetString(_power.IsPowered(ent.Owner)
            ? "n14-water-purifier-examine-running"
            : "n14-water-purifier-examine-unpowered"));
    }
}

[AdminCommand(AdminFlags.Admin)]
public sealed class WaterPlantMarkerCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _entities = default!;

    private const float SearchRange = 10f;

    public string Command => "waterplantmarker";
    public string Description => "Shows a water treatment machine on everyone's map as the water plant, or hides it. Off by default each round.";
    public string Help => "waterplantmarker [machine entity id | off]   With no id it picks the treatment machine closest to you.";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var waterPlant = _entities.System<WaterPurifierSystem>();

        if (args.Length == 1 && args[0].Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            waterPlant.ClearMarker();
            shell.WriteLine("Water plant marker hidden.");
            return;
        }

        EntityUid? machine = null;
        if (args.Length == 1)
        {
            if (!NetEntity.TryParse(args[0], out var net) || !_entities.TryGetEntity(net, out machine))
            {
                shell.WriteError($"No entity with id {args[0]}.");
                return;
            }
        }
        else if (args.Length == 0)
        {
            if (shell.Player?.AttachedEntity is not { Valid: true } admin)
            {
                shell.WriteError("You need to be in game to pick the closest machine. Give a machine entity id instead.");
                return;
            }

            machine = waterPlant.FindNearestMachine(admin, SearchRange);
            if (machine == null)
            {
                shell.WriteError($"No water treatment machine within {SearchRange} tiles of you.");
                return;
            }
        }
        else
        {
            shell.WriteError(Help);
            return;
        }

        if (!waterPlant.IsTreatmentMachine(machine.Value))
        {
            shell.WriteError($"{_entities.ToPrettyString(machine.Value)} is not a water treatment machine.");
            return;
        }

        waterPlant.SetMarker(machine.Value);
        shell.WriteLine($"Water plant marker now shows at {_entities.ToPrettyString(machine.Value)}.");
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length == 1
            ? CompletionResult.FromHint("[machine entity id | off]")
            : CompletionResult.Empty;
    }
}
