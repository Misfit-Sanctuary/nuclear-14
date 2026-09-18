using Content.Server.Abilities.Psionics;
using Content.Shared.Administration;
using Robust.Shared.Console;


namespace Content.Server.Administration.Commands;


[AdminCommand(AdminFlags.Admin)]
public sealed partial class SwapMindsCommand : IConsoleCommand
{
    [Dependency] private IEntityManager _entManager = default!;

    public string Command => "swapMinds";
    public string Description => Loc.GetString("swap-minds-description");
    public string Help => Loc.GetString("swap-minds-help-text", ("command", Command));

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {

        if (args.Length < 2)
        {
            shell.WriteError(Loc.GetString("shell-wrong-arguments-number"));
            return;
        }

        if (!int.TryParse(args[0], out var entInt1) || !int.TryParse(args[1], out var entInt2))
        {
            shell.WriteLine(Loc.GetString("shell-entity-uid-must-be-number"));
            return;
        }

        var netEntity1 = new NetEntity(entInt1);
        var netEntity2 = new NetEntity(entInt2);
        if (!_entManager.TryGetEntity(netEntity1, out var tar1) || !_entManager.TryGetEntity(netEntity1, out var tar2))
        {
            shell.WriteLine(Loc.GetString("shell-invalid-entity-id"));
            return;
        }

        var ent1 = _entManager.GetEntity(netEntity1);
        var ent2 = _entManager.GetEntity(netEntity2);
        var sysMan = IoCManager.Resolve<IEntitySystemManager>();
        var mindSwap = sysMan.GetEntitySystem<MindSwapPowerSystem>();

        if (!mindSwap.SwapUntracked(ent1, ent2))
        {
            shell.WriteLine(Loc.GetString("swap-minds-no-mind"));
            return;
        }


        shell.WriteLine(Loc.GetString("shell-command-success"));
    }
}
