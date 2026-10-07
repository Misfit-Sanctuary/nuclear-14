using Content.Server._Misfits.Radio;
using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._Misfits.Administration.Commands;

/// <summary>
/// Enables or disables the WastelandGlobal radio channel without a restart.
/// The setting is intentionally process-local and resets to enabled on restart.
/// </summary>
[AdminCommand(AdminFlags.Admin)]
public sealed class SetWastelandRadioCommand : IConsoleCommand
{
    [Dependency] private readonly IEntitySystemManager _systems = default!;

    public string Command => "setwastelandradio";
    public string Description => "Enables or disables the Wasteland radio channel for the current round.";
    public string Help => "setwastelandradio [true|false]";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length > 1)
        {
            shell.WriteError($"Usage: {Help}");
            return;
        }

        var radio = _systems.GetEntitySystem<WastelandRadioSystem>();
        var enabled = !radio.Enabled;

        if (args.Length == 1 && !bool.TryParse(args[0], out enabled))
        {
            shell.WriteError("The optional value must be true or false.");
            return;
        }

        radio.SetEnabled(enabled);
        shell.WriteLine($"Wasteland radio is now {(enabled ? "enabled" : "disabled")} for this server session.");
    }
}
