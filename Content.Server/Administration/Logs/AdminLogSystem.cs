using Content.Server.GameTicking;
using Content.Server.GameTicking.Events;
using Content.Server.Administration.Managers;
using Content.Shared.Database;
using Robust.Server.Player;
using Robust.Shared.Console;
using System.Security.Cryptography;
using System.Text;

namespace Content.Server.Administration.Logs;

/// <summary>
///     For system events that the manager needs to know about.
///     <see cref="IAdminLogManager"/> for admin log usage.
/// </summary>
public sealed class AdminLogSystem : EntitySystem
{
    [Dependency] private readonly IAdminLogManager _adminLogs = default!;
    [Dependency] private readonly IAdminManager _admins = default!;
    [Dependency] private readonly IConsoleHost _console = default!;
    [Dependency] private readonly IPlayerManager _players = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RoundStartingEvent>(ev => _adminLogs.RoundStarting(ev.Id));
        SubscribeLocalEvent<GameRunLevelChangedEvent>(ev => _adminLogs.RunLevelChanged(ev.New));
        _console.AnyCommandExecuted += OnCommandExecuted;
    }

    private void OnCommandExecuted(IConsoleShell shell, string commandName, string commandLine, string[] args)
    {
        if (shell.Player is not { } actor || !_admins.IsAdmin(actor))
            return;

        // Record the command even when its implementation does not log itself. A digest
        // correlates repeated invocations without persisting private notes or script bodies.
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(commandLine)))[..12];
        if (args.Length > 0 && _players.TryGetSessionByUsername(args[0], out var target))
            _adminLogs.Add(LogType.Action, LogImpact.Medium,
                $"{actor:actor} ran admin command {commandName} targeting {target:subject} (arguments: {args.Length}, digest: {digest})");
        else
            _adminLogs.Add(LogType.Action, LogImpact.Medium,
                $"{actor:actor} ran admin command {commandName} (arguments: {args.Length}, digest: {digest})");
    }


    public override void Update(float frameTime)
    {
        _adminLogs.Update();
    }

    public override void Shutdown()
    {
        _console.AnyCommandExecuted -= OnCommandExecuted;
        base.Shutdown();
        _adminLogs.Shutdown();
    }
}
