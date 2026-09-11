using Robust.Client.GameObjects;
using Robust.Shared.Console;

namespace Content.Client._Misfits.Areas;

internal sealed class ShowAreaMarkersCommand : LocalizedCommands
{
    [Dependency] private readonly IEntitySystemManager _entitySystemManager = default!;

    public override string Command => "showareas";

    public override string Help => LocalizationManager.GetString($"cmd-{Command}-help", ("command", Command));

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        _entitySystemManager.GetEntitySystem<AreaMarkerSystem>().AreaMarkersVisible ^= true;
    }
}
