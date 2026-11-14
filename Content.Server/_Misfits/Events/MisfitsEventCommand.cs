using System.Linq;
using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._Misfits.Events;

// Console entry point for the event pool; the admin Events tab does the same.
[AdminCommand(AdminFlags.Debug)]
public sealed class MisfitsEventCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _entities = default!;

    private MisfitsEventSystem Events => _entities.System<MisfitsEventSystem>();
    private MisfitsEventSchedulerSystem Scheduler => _entities.System<MisfitsEventSchedulerSystem>();

    public string Command => "misfitevent";
    public string Description => "Manage the fork random event pool.";
    public string Help => "misfitevent <list|pool|status|roll|run <id>|interval <min> <max>|next <seconds>|enable|disable>";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length == 0)
        {
            shell.WriteLine(Help);
            return;
        }

        switch (args[0])
        {
            case "list":
                foreach (var line in Events.AllEvents()
                             .OrderByDescending(pair => pair.Value.Weight)
                             .Select(pair => $"{pair.Key.ID} (weight {pair.Value.Weight})"))
                {
                    shell.WriteLine(line);
                }

                break;

            case "pool":
                var pool = Events.AvailableEvents();
                var totalWeight = pool.Values.Sum(component => Math.Max(0f, component.Weight));
                foreach (var (proto, component) in pool.OrderByDescending(pair => pair.Value.Weight))
                {
                    var chance = totalWeight <= 0f ? 0f : Math.Max(0f, component.Weight) / totalWeight * 100f;
                    shell.WriteLine($"{proto.ID} (weight {component.Weight}, {chance:0.0}%)");
                }

                break;

            case "status":
                PrintStatus(shell);
                break;

            case "roll":
                PrintRun(shell, Events.RunRandomEvent(), "No event was eligible to roll. See 'misfitevent pool'.");
                break;

            case "run":
                if (args.Length < 2)
                {
                    shell.WriteError("Usage: misfitevent run <event id>");
                    return;
                }

                PrintRun(shell, Events.RunEvent(args[1]), $"Could not start event '{args[1]}'. See 'misfitevent list'.");
                break;

            case "interval":
                if (args.Length < 3 || !float.TryParse(args[1], out var min) || !float.TryParse(args[2], out var max))
                {
                    shell.WriteError("Usage: misfitevent interval <min seconds> <max seconds>");
                    return;
                }

                if (!Scheduler.SetInterval(min, max))
                {
                    shell.WriteError("Could not start the event scheduler.");
                    return;
                }

                shell.WriteLine($"Automatic events now roll every {Math.Min(min, max)}-{Math.Max(min, max)} seconds.");
                break;

            case "next":
                if (args.Length < 2 || !float.TryParse(args[1], out var seconds))
                {
                    shell.WriteError("Usage: misfitevent next <seconds>");
                    return;
                }

                if (!Scheduler.SetNextRoll(seconds))
                {
                    shell.WriteError("Could not start the event scheduler.");
                    return;
                }

                shell.WriteLine($"Next automatic event roll in {Math.Max(0f, seconds):0} seconds.");
                break;

            case "enable":
            case "disable":
                var enable = args[0] == "enable";
                if (!Scheduler.SetEnabled(enable))
                {
                    shell.WriteError("Could not start the event scheduler.");
                    return;
                }

                shell.WriteLine(enable
                    ? "Automatic events enabled for this round."
                    : "Automatic events disabled for this round.");

                if (enable && !Scheduler.EventsEnabled)
                {
                    shell.WriteLine(
                        "Note: the misfits.events_enabled cvar is off, so automatic rolls stay paused until it is " +
                        "enabled (it is archived, so that survives restarts).");
                }

                break;

            default:
                shell.WriteError($"Unknown subcommand '{args[0]}'.");
                shell.WriteLine(Help);
                break;
        }
    }

    private void PrintRun(IConsoleShell shell, EntityUid? rule, string failure)
    {
        if (rule is not { } ruleUid)
        {
            shell.WriteError(failure);
            return;
        }

        var id = _entities.GetComponent<MetaDataComponent>(ruleUid).EntityPrototype?.ID ?? "unknown";

        // Report what actually happened, so a run that spawned nothing is not mistaken for success.
        if (!_entities.TryGetComponent<MisfitsEventComponent>(ruleUid, out var component) ||
            component.SpawnedAt is not { } at)
        {
            shell.WriteError(component?.LastFailure is { } why
                ? $"Started event '{id}', but nothing spawned: {why}."
                : $"Started event '{id}', waiting for it to spawn.");
            return;
        }

        var location = component.LastSpawnLocation is { } named ? $" near '{named}'" : string.Empty;
        shell.WriteLine($"Started event '{id}': spawned {component.LastSpawnedCount} entities{location} at {at.Position}.");
    }

    private void PrintStatus(IConsoleShell shell)
    {
        shell.WriteLine($"Automatic events cvar (misfits.events_enabled): {(Scheduler.EventsEnabled ? "on" : "off")}.");

        if (Scheduler.GetScheduler() is not { } running)
        {
            shell.WriteLine("Event scheduler: not running.");
        }
        else
        {
            var state = running.Comp.Enabled ? "enabled" : "disabled";
            var paused = Scheduler.EventsEnabled ? string.Empty : " (paused by cvar)";
            shell.WriteLine($"Event scheduler: {state}{paused}, every {running.Comp.MinInterval}-{running.Comp.MaxInterval}s, " +
                            $"next roll in {Math.Max(0f, running.Comp.TimeUntilNextEvent):0}s.");
        }

        shell.WriteLine($"Eligible events right now: {Events.AvailableEvents().Count} of {Events.AllEvents().Count}.");
    }
}
