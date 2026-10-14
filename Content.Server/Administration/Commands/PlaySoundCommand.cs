using System.IO;
using Content.Server._Misfits.Administration;
using Content.Server.Audio;
using Content.Shared._Misfits.Administration;
using Content.Shared.Administration;
using Robust.Server.Player;
using Robust.Shared.Audio;
using Robust.Shared.Console;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server.Administration.Commands;

/// <summary>Plays an unpositioned admin sound to players on the caller's current map.</summary>
[AdminCommand(AdminFlags.Fun)]
public sealed class PlaySoundCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _entities = default!;
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly IResourceManager _resources = default!;

    public string Command => "playsound";
    public string Description => "Plays a sound to everyone on your current map.";
    public string Help => "playsound <path> [volume]";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length is < 1 or > 2)
        {
            shell.WriteError(Help);
            return;
        }

        if (shell.Player?.AttachedEntity is not { Valid: true } source
            || !_entities.TryGetComponent(source, out TransformComponent? transform)
            || transform.MapID == MapId.Nullspace)
        {
            shell.WriteError("You must be attached to an entity on a map to play a map sound.");
            return;
        }

        var path = new ResPath(args[0]);
        if (path.Extension is not ("ogg" or "wav" or "mp3") || !_resources.ContentFileExists(path))
        {
            shell.WriteError("Sound must be an existing OGG, PCM WAV, or MP3 resource path.");
            return;
        }

        var audio = AudioParams.Default.WithVolume(-8);
        if (args.Length == 2)
        {
            if (!float.TryParse(args[1], out var volume) || !float.IsFinite(volume))
            {
                shell.WriteError("Volume must be a finite number of decibels.");
                return;
            }

            audio = audio.WithVolume(volume - 8);
        }

        var map = transform.MapID;
        var filter = Filter.Empty().AddWhere(session => session.AttachedEntity is { Valid: true } player
            && _entities.TryGetComponent(player, out TransformComponent? playerTransform)
            && playerTransform.MapID == map, _players);

        if (path.Extension == "mp3")
        {
            try
            {
                using var file = _resources.ContentFileRead(path);
                if (file.Length > AdminAudioFormat.MaxBytes)
                    throw new InvalidDataException("MP3 exceeds the 3 MB input limit.");

                using var copy = new MemoryStream();
                file.CopyTo(copy);
                var bytes = copy.ToArray();
                if (!AdminAudioFormat.TryGetExtension(bytes, out var extension) || extension != "mp3")
                    throw new InvalidDataException("Resource is not a valid MP3 file.");

                var wav = AdminMp3Converter.DecodeToWav(bytes);
                _entities.System<ServerGlobalSoundSystem>().PlayAdminMapWav(filter, wav, audio.Volume);
            }
            catch (Exception e)
            {
                shell.WriteError($"Could not play MP3: {e.Message}");
                return;
            }
        }
        else
        {
            _entities.System<ServerGlobalSoundSystem>().PlayAdminGlobal(filter, path.ToString(), audio);
        }
        shell.WriteLine($"Playing {path} for {filter.Count} player(s) on this map.");
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length == 1)
            return CompletionResult.FromHintOptions(
                CompletionHelper.AudioFilePath(args[0], _prototypes, _resources), "<path>");

        return args.Length == 2 ? CompletionResult.FromHint("[volume in dB]") : CompletionResult.Empty;
    }
}
