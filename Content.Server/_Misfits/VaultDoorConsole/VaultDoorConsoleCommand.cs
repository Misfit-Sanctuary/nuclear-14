using Content.Server.Administration;
using Content.Shared._Misfits.VaultDoorConsole;
using Content.Shared.Administration;
using Robust.Shared.Toolshed;

namespace Content.Server._Misfits.VaultDoorConsole;

[ToolshedCommand, AdminCommand(AdminFlags.Debug)]
public sealed class VaultDoorConsoleCommand : ToolshedCommand
{
    [CommandImplementation("bypassraid")]
    public void BypassRaid(IInvocationContext ctx, [CommandArgument] bool enabled)
    {
        var count = 0;
        var query = EntityManager.EntityQueryEnumerator<VaultDoorConsoleGateComponent>();
        while (query.MoveNext(out var uid, out var gate))
        {
            gate.BypassRaidRequirement = enabled;
            EntityManager.Dirty(uid, gate);
            count++;
        }

        ctx.WriteLine($"Set BypassRaidRequirement={enabled} on {count} vault door console(s).");
    }
}
