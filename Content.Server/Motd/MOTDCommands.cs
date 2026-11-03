// #Misfits Add - Consolidated Message of the Day commands, plus the pictures and the poll.
using System.Linq;
using Content.Server.Administration;
using Content.Server.Administration.Logs;
using Content.Server.Administration.Managers;
using Content.Server.Chat.Managers;
using Content.Shared.Administration;
using Content.Shared.CCVar;
using Content.Shared.Database;
using Robust.Shared.Configuration;
using Robust.Shared.Console;

namespace Content.Server.Motd;

/// Prints or sets the Message of the Day. Acts as an alias for <see cref="GetMotdCommand"/> or <see cref="SetMotdCommand"/> depending on the number of arguments given.
[AnyCommand]
public sealed class MOTDCommand : LocalizedCommands
{
    [Dependency] private readonly IAdminManager _adminManager = default!;

    public override string Command => "motd";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var player = shell.Player;
        if (args.Length < 1 || (player != null && !_adminManager.HasAdminFlag(player, AdminFlags.Admin)))
            shell.ConsoleHost.ExecuteCommand(shell.Player, "get-motd");
        else
            shell.ConsoleHost.ExecuteCommand(shell.Player, $"set-motd {string.Join(" ", args)}");
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        var player = shell.Player;
        if (player != null && !_adminManager.HasAdminFlag(player, AdminFlags.Admin))
            return CompletionResult.Empty;
        if (args.Length == 1)
            return CompletionResult.FromHint(Loc.GetString("cmd-set-motd-hint-head"));
        return CompletionResult.FromHint(Loc.GetString("cmd-set-motd-hint-cont"));
    }
}

/// A command that can be used by any player to print the Message of the Day and open its window.
[AnyCommand]
public sealed class GetMotdCommand : LocalizedCommands
{
    [Dependency] private readonly IEntityManager _entityManager = default!;

    public override string Command => "get-motd";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        _entityManager.EntitySysManager.GetEntitySystem<MOTDSystem>().TrySendMOTD(shell);
    }
}

/// An admin command that sets or clears the Message of the Day.
[AdminCommand(AdminFlags.Admin)]
public sealed class SetMotdCommand : LocalizedCommands
{
    [Dependency] private readonly IAdminLogManager _adminLogManager = default!;
    [Dependency] private readonly IChatManager _chatManager = default!;
    [Dependency] private readonly IConfigurationManager _configurationManager = default!;

    public override string Command => "set-motd";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        string motd = "";
        var player = shell.Player;
        if (args.Length > 0)
        {
            motd = string.Join(" ", args).Trim();
            if (player != null && _chatManager.MessageCharacterLimit(player, motd))
                return; // check function prints its own error response
        }

        _configurationManager.SetCVar(CCVars.MOTD, motd); // A hook in MOTDSystem shows changes to everyone so we don't need to do it here.
        if (string.IsNullOrEmpty(motd))
        {
            shell.WriteLine(Loc.GetString("cmd-set-motd-cleared-motd-message"));
            _adminLogManager.Add(LogType.Chat, LogImpact.Low, $"{(player == null ? "LOCALHOST" : player.Channel.UserName):Player} cleared the MOTD for the server.");
        }
        else
        {
            shell.WriteLine(Loc.GetString("cmd-set-motd-set-motd-message", ("motd", motd)));
            _adminLogManager.Add(LogType.Chat, LogImpact.Low, $"{(player == null ? "LOCALHOST" : player.Channel.UserName):Player} set the MOTD for the server to \"{motd:motd}\"");
        }
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length == 1)
            return CompletionResult.FromHint(Loc.GetString("cmd-set-motd-hint-head"));
        return CompletionResult.FromHint(Loc.GetString("cmd-set-motd-hint-cont"));
    }
}

/// An admin command that sets the pictures shown in the Message of the Day window.
[AdminCommand(AdminFlags.Admin)]
public sealed class SetMotdImageCommand : LocalizedCommands
{
    [Dependency] private readonly IAdminLogManager _adminLogManager = default!;
    [Dependency] private readonly IConfigurationManager _configurationManager = default!;

    public override string Command => "set-motd-image";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var player = shell.Player;

        if (args.Length == 0)
        {
            _configurationManager.SetCVar(CCVars.MOTDImages, "");
            shell.WriteLine(Loc.GetString("cmd-set-motd-image-cleared"));
            _adminLogManager.Add(LogType.Chat, LogImpact.Low,
                $"{(player == null ? "LOCALHOST" : player.Channel.UserName):Player} cleared the MOTD pictures.");
            return;
        }

        var paths = new List<string>();
        foreach (var arg in args)
        {
            var path = MOTDSystem.NormalizeImagePath(arg);
            if (path == null)
            {
                shell.WriteError(Loc.GetString("cmd-set-motd-image-invalid", ("path", arg)));
                return;
            }

            paths.Add(path);
        }

        if (paths.Count > MOTDSystem.MaxImages)
        {
            shell.WriteError(Loc.GetString("cmd-set-motd-image-too-many", ("max", MOTDSystem.MaxImages)));
            return;
        }

        _configurationManager.SetCVar(CCVars.MOTDImages, string.Join(';', paths));

        shell.WriteLine(Loc.GetString("cmd-set-motd-image-set", ("images", string.Join(", ", paths))));
        _adminLogManager.Add(LogType.Chat, LogImpact.Low,
            $"{(player == null ? "LOCALHOST" : player.Channel.UserName):Player} set the MOTD pictures to \"{string.Join(", ", paths)}\".");
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return CompletionResult.FromHint(Loc.GetString("cmd-set-motd-image-hint"));
    }
}

/// An admin command that attaches a poll to the Message of the Day.
[AdminCommand(AdminFlags.Admin)]
public sealed class SetMotdPollCommand : LocalizedCommands
{
    [Dependency] private readonly IAdminLogManager _adminLogManager = default!;
    [Dependency] private readonly IChatManager _chatManager = default!;
    [Dependency] private readonly IEntityManager _entityManager = default!;

    public override string Command => "set-motd-poll";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var raw = string.Join(" ", args).Trim();
        if (raw.Length == 0)
        {
            shell.WriteError(Loc.GetString("cmd-set-motd-poll-usage"));
            return;
        }

        if (shell.Player != null && _chatManager.MessageCharacterLimit(shell.Player, raw))
            return; // check function prints its own error response

        // The question comes first, then every option is separated by a pipe: "question | option | option"
        var parts = raw.Split('|').Select(part => part.Trim()).ToArray();
        if (parts.Length < 3 || parts[0].Length == 0)
        {
            shell.WriteError(Loc.GetString("cmd-set-motd-poll-usage"));
            return;
        }

        var question = parts[0];
        var options = parts.Skip(1).ToArray();
        if (options.Any(string.IsNullOrEmpty))
        {
            shell.WriteError(Loc.GetString("cmd-set-motd-poll-empty"));
            return;
        }

        if (options.Length > MotdPoll.MaxOptions)
        {
            shell.WriteError(Loc.GetString("cmd-set-motd-poll-too-many-options", ("max", MotdPoll.MaxOptions)));
            return;
        }

        _entityManager.EntitySysManager.GetEntitySystem<MOTDSystem>().SetPoll(question, options);

        shell.WriteLine(Loc.GetString("cmd-set-motd-poll-set", ("question", question), ("count", options.Length)));
        _adminLogManager.Add(LogType.Chat, LogImpact.Low,
            $"{(shell.Player == null ? "LOCALHOST" : shell.Player.Channel.UserName):Player} set the MOTD poll to \"{question}\" with {options.Length} option(s): {string.Join(", ", options)}");
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return CompletionResult.FromHint(Loc.GetString("cmd-set-motd-poll-hint"));
    }
}

/// An admin command that removes the poll attached to the Message of the Day.
[AdminCommand(AdminFlags.Admin)]
public sealed class ClearMotdPollCommand : LocalizedCommands
{
    [Dependency] private readonly IAdminLogManager _adminLogManager = default!;
    [Dependency] private readonly IEntityManager _entityManager = default!;

    public override string Command => "clear-motd-poll";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var motdSystem = _entityManager.EntitySysManager.GetEntitySystem<MOTDSystem>();
        if (motdSystem.Poll == null)
        {
            shell.WriteLine(Loc.GetString("cmd-clear-motd-poll-no-poll"));
            return;
        }

        motdSystem.ClearPoll();

        shell.WriteLine(Loc.GetString("cmd-clear-motd-poll-cleared"));
        _adminLogManager.Add(LogType.Chat, LogImpact.Low,
            $"{(shell.Player == null ? "LOCALHOST" : shell.Player.Channel.UserName):Player} cleared the MOTD poll.");
    }
}

/// A command usable by any player which votes in the Message of the Day poll, or prints its results.
[AnyCommand]
public sealed class VoteMotdPollCommand : LocalizedCommands
{
    [Dependency] private readonly IEntityManager _entityManager = default!;

    public override string Command => "motd-vote";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var motdSystem = _entityManager.EntitySysManager.GetEntitySystem<MOTDSystem>();

        if (args.Length == 0)
        {
            motdSystem.TrySendPoll(shell);
            return;
        }

        if (shell.Player is not { } player)
        {
            shell.WriteError(Loc.GetString("shell-only-players-can-run-this-command"));
            return;
        }

        if (!int.TryParse(args[0], out var option))
        {
            shell.WriteError(Loc.GetString("cmd-motd-vote-not-a-number", ("option", args[0])));
            return;
        }

        switch (motdSystem.Vote(player, option))
        {
            case MotdVoteResult.NoPoll:
                shell.WriteError(Loc.GetString("cmd-motd-vote-no-poll"));
                return;
            case MotdVoteResult.InvalidOption:
                shell.WriteError(Loc.GetString("cmd-motd-vote-invalid-option",
                    ("option", option),
                    ("max", motdSystem.Poll?.Options.Count ?? 0)));
                return;
            case MotdVoteResult.Voted:
                shell.WriteLine(Loc.GetString("cmd-motd-vote-cast",
                    ("number", option),
                    ("option", GetOption(motdSystem, option))));
                break;
            case MotdVoteResult.Changed:
                shell.WriteLine(Loc.GetString("cmd-motd-vote-changed",
                    ("number", option),
                    ("option", GetOption(motdSystem, option))));
                break;
        }

        motdSystem.BroadcastMotd(open: false);
        motdSystem.TrySendPoll(shell, sendWindow: false);
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length != 1)
            return CompletionResult.Empty;

        var motdSystem = _entityManager.EntitySysManager.GetEntitySystem<MOTDSystem>();
        if (motdSystem.Poll is not { } poll)
            return CompletionResult.Empty;

        var options = new List<string>();
        for (var i = 1; i <= poll.Options.Count; i++)
            options.Add(i.ToString());

        return CompletionResult.FromOptions(options);
    }

    /// Gets the text of a 1-based option, falling back to the number if the poll changed underneath us.
    private static string GetOption(MOTDSystem motdSystem, int option)
    {
        return motdSystem.Poll is { } poll && option >= 1 && option <= poll.Options.Count
            ? poll.Options[option - 1]
            : option.ToString();
    }
}
