using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Toolshed;

namespace Content.Server._Misfits.Requisitions;

[ToolshedCommand, AdminCommand(AdminFlags.VarEdit)]
public sealed class RequisitionsCommand : ToolshedCommand
{
    [CommandImplementation("addbudget")]
    public void AddBudget([CommandArgument] string group, [CommandArgument] int money)
    {
        var system = Sys<RequisitionsSystem>();
        if (group == "all")
            system.ChangeBudget(money);
        else
            system.ChangeBudget(group, money);
    }

    [CommandImplementation("removebudget")]
    public void RemoveBudget([CommandArgument] string group, [CommandArgument] int money)
    {
        var system = Sys<RequisitionsSystem>();
        if (group == "all")
            system.ChangeBudget(-money);
        else
            system.ChangeBudget(group, -money);
    }
}
