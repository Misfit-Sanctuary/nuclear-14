using Content.Server._Misfits.Requisitions;
using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._Misfits.Administration.Commands;

[AdminCommand(AdminFlags.Debug)]
public sealed class RerollRequisitionsRequestsCommand : IConsoleCommand
{
    [Dependency] private readonly IEntitySystemManager _sysManager = default!;

    public string Command => "reqrerollall";
    public string Description => "force rerols every randomized requisitions request board slot for every.";
    public string Help => "reqrerollall";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var requisitions = _sysManager.GetEntitySystem<RequisitionsSystem>();
        var rerolled = requisitions.DebugRerollAllRandomRequests();
        shell.WriteLine($"Rerolled {rerolled} requisitions request board slot(s).");
    }
}
