using Content.Shared._Misfits.Special;
using Content.Shared._Misfits.Special.Components;
using Content.Server.Administration;
using Content.Server.Administration.Logs;
using Content.Shared.Administration;
using Content.Shared.Database;
using Robust.Server.Player;
using Robust.Shared.Console;

namespace Content.Server._Misfits.Special;

[AdminCommand(AdminFlags.Admin)]
public sealed class SpecialGetCommand : IConsoleCommand
{
    [Dependency] private readonly IAdminLogManager _adminLog = default!;
    [Dependency] private readonly IEntityManager _entities = default!;
    [Dependency] private readonly IPlayerManager _players = default!;

    public string Command => "specialget";
    public string Description => "Shows a player's SPECIAL values.";
    public string Help => "Usage: specialget <username>";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1)
        {
            shell.WriteError(Help);
            return;
        }

        var target = ParsePlayerEntity(shell, args[0], _players);

        if (target == null)
        {
            return;
        }

        var specialSystem = _entities.System<SharedSpecialSystem>();
        if (!specialSystem.UsesSpecialStats(target.Value))
        {
            shell.WriteError("Target cannot use SPECIAL stats.");
            return;
        }

        if (!_entities.TryGetComponent<SpecialComponent>(target.Value, out var special))
        {
            shell.WriteError("Target has no SpecialComponent.");
            return;
        }

        if (shell.Player is { } actor && _players.TryGetSessionByUsername(args[0], out var targetPlayer))
            _adminLog.Add(LogType.Action, LogImpact.Low,
                $"{actor:actor} viewed SPECIAL values for {targetPlayer:subject}");

        foreach (var stat in Content.Shared._Misfits.Special.SpecialStats.All)
        {
            shell.WriteLine($"{stat}: base {specialSystem.GetBase(target.Value, stat, special)}, modifier {specialSystem.GetModifier(target.Value, stat, special)}, effective {specialSystem.GetEffective(target.Value, stat, special)}");
        }
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length == 1)
        {
            return CompletionResult.FromHintOptions(
                CompletionHelper.SessionNames(players: _players),
                "<username>");
        }

        return CompletionResult.Empty;
    }

    internal static EntityUid? ParsePlayerEntity(IConsoleShell shell, string username, IPlayerManager players)
    {
        // SPECIAL is stored on the attached mob, not directly on the player session.
        if (!players.TryGetSessionByUsername(username, out var player))
        {
            shell.WriteError("Unable to find that player.");
            return null;
        }

        if (player.AttachedEntity is not { } target)
        {
            shell.WriteError("Player has no attached entity.");
            return null;
        }

        return target;
    }
}

[AdminCommand(AdminFlags.Admin)]
public sealed class SpecialSetCommand : IConsoleCommand
{
    [Dependency] private readonly IAdminLogManager _adminLog = default!;
    [Dependency] private readonly IEntityManager _entities = default!;
    [Dependency] private readonly IPlayerManager _players = default!;

    public string Command => "specialset";
    public string Description => "Sets a player's base SPECIAL stat.";
    public string Help => "Usage: specialset <username> <strength|perception|endurance|charisma|intelligence|agility|luck> <1-10>";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 3)
        {
            shell.WriteError(Help);
            return;
        }

        var target = SpecialGetCommand.ParsePlayerEntity(shell, args[0], _players);
        if (target == null)
            return;

        if (!TryParseStat(args[1], out var stat))
        {
            shell.WriteError("Unknown SPECIAL stat.");
            return;
        }

        if (!int.TryParse(args[2], out var value))
        {
            shell.WriteError("Value must be a number.");
            return;
        }

        var specialSystem = _entities.System<SharedSpecialSystem>();
        if (!specialSystem.UsesSpecialStats(target.Value))
        {
            shell.WriteError("Target cannot use SPECIAL stats.");
            return;
        }

        var special = _entities.EnsureComponent<SpecialComponent>(target.Value);
        var previous = specialSystem.GetBase(target.Value, stat, special);
        if (!specialSystem.TrySetBase(target.Value, stat, value, special))
        {
            shell.WriteError($"Value must be between {SpecialProfile.Minimum} and {SpecialProfile.Maximum}.");
            return;
        }

        if (shell.Player is { } actor && _players.TryGetSessionByUsername(args[0], out var targetPlayer))
            _adminLog.Add(LogType.Action, LogImpact.Medium,
                $"{actor:actor} changed {targetPlayer:subject} SPECIAL {stat} base from {previous} to {value}");

        shell.WriteLine($"{stat} set to {value}.");
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length switch
        {
            1 => CompletionResult.FromHintOptions(
                CompletionHelper.SessionNames(players: _players),
                "<username>"),
            2 => CompletionResult.FromHintOptions(StatCompletions, "<stat>"),
            3 => CompletionResult.FromHintOptions(ValueCompletions, "<1-10>"),
            _ => CompletionResult.Empty,
        };
    }

    internal static bool TryParseStat(string text, out SpecialStat stat)
    {
        // Accept short Fallout-style stat letters plus local flavor aliases used
        // by the character UI/design docs.
        switch (text.ToLowerInvariant())
        {
            case "s":
            case "str":
            case "strength":
            case "v":
            case "vig":
            case "vigor":
                stat = SpecialStat.Strength;
                return true;
            case "p":
            case "per":
            case "perception":
            case "aw":
            case "aware":
            case "awareness":
                stat = SpecialStat.Perception;
                return true;
            case "e":
            case "end":
            case "endurance":
            case "u":
            case "util":
            case "utility":
                stat = SpecialStat.Endurance;
                return true;
            case "c":
            case "cha":
            case "charisma":
                stat = SpecialStat.Charisma;
                return true;
            case "i":
            case "int":
            case "intelligence":
                stat = SpecialStat.Intelligence;
                return true;
            case "a":
            case "agi":
            case "agility":
            case "t":
            case "tmp":
            case "tempo":
                stat = SpecialStat.Agility;
                return true;
            case "l":
            case "lck":
            case "luck":
                stat = SpecialStat.Luck;
                return true;
            default:
                stat = default;
                return false;
        }
    }

    internal static readonly string[] StatCompletions =
    [
        "strength",
        "perception",
        "endurance",
        "charisma",
        "intelligence",
        "agility",
        "luck",
    ];

    private static readonly string[] ValueCompletions =
    [
        "1",
        "2",
        "3",
        "4",
        "5",
        "6",
        "7",
        "8",
        "9",
        "10",
    ];
}

[AdminCommand(AdminFlags.Admin)]
public sealed class SpecialModCommand : IConsoleCommand
{
    [Dependency] private readonly IAdminLogManager _adminLog = default!;
    [Dependency] private readonly IEntityManager _entities = default!;
    [Dependency] private readonly IPlayerManager _players = default!;

    public string Command => "specialmod";
    public string Description => "Adds a temporary SPECIAL modifier to a player.";
    public string Help => "Usage: specialmod <username> <strength|perception|endurance|charisma|intelligence|agility|luck> <modifier> [durationSeconds] [source]";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length is < 3 or > 5)
        {
            shell.WriteError(Help);
            return;
        }

        var target = SpecialGetCommand.ParsePlayerEntity(shell, args[0], _players);
        if (target == null)
            return;

        if (!SpecialSetCommand.TryParseStat(args[1], out var stat))
        {
            shell.WriteError("Unknown SPECIAL stat.");
            return;
        }

        if (!int.TryParse(args[2], out var modifier))
        {
            shell.WriteError("Modifier must be a number.");
            return;
        }

        TimeSpan? duration = null;
        if (args.Length >= 4)
        {
            if (!float.TryParse(args[3], out var seconds) || seconds <= 0f)
            {
                shell.WriteError("Duration must be a positive number of seconds.");
                return;
            }

            duration = TimeSpan.FromSeconds(seconds);
        }

        var source = args.Length >= 5 ? args[4] : "admin";
        var specialSystem = _entities.System<SharedSpecialSystem>();
        if (!specialSystem.UsesSpecialStats(target.Value))
        {
            shell.WriteError("Target cannot use SPECIAL stats.");
            return;
        }

        var special = _entities.EnsureComponent<SpecialComponent>(target.Value);
        var previous = specialSystem.GetEffective(target.Value, stat, special);

        if (!specialSystem.TryModifyTemporary(target.Value, stat, modifier, duration, source, special))
        {
            shell.WriteError("Could not apply modifier.");
            return;
        }

        if (shell.Player is { } actor && _players.TryGetSessionByUsername(args[0], out var targetPlayer))
            _adminLog.Add(LogType.Action, LogImpact.Medium,
                $"{actor:actor} applied SPECIAL {stat} modifier {modifier} to {targetPlayer:subject}, effective {previous} -> {specialSystem.GetEffective(target.Value, stat, special)}, duration {duration?.TotalSeconds} seconds, source {source}");

        shell.WriteLine($"{stat} temporary modifier {modifier:+#;-#;0} applied.");
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length switch
        {
            1 => CompletionResult.FromHintOptions(
                CompletionHelper.SessionNames(players: _players),
                "<username>"),
            2 => CompletionResult.FromHintOptions(SpecialSetCommand.StatCompletions, "<stat>"),
            3 => CompletionResult.FromHint("<modifier>"),
            4 => CompletionResult.FromHint("<durationSeconds>"),
            5 => CompletionResult.FromHint("<source>"),
            _ => CompletionResult.Empty,
        };
    }
}
