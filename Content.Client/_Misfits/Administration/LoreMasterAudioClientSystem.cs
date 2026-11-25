using Content.Shared._Misfits.Administration;
using Content.Shared.CCVar;
using System.IO;
using Robust.Client.Audio;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.Audio;
using Robust.Shared.Console;
using Robust.Shared.ContentPack;
using Robust.Shared.Configuration;
using Robust.Shared.Utility;

namespace Content.Client._Misfits.Administration;

/// <summary>Plays temporary admin audio at the admin's entity without adding a permanent asset.</summary>
public sealed class LoreMasterAudioClientSystem : EntitySystem
{
    public event Action<bool, string>? GlobalUploadResult;

    private static readonly ResPath RootPath = ResPath.Root / "LoreMasterAudioPlayback";

    [Dependency] private readonly IResourceManager _resources = default!;
    [Dependency] private readonly AudioSystem _audio = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IFileDialogManager _dialogs = default!;
    [Dependency] private readonly IConsoleHost _console = default!;

    private readonly MemoryContentRoot _audioRoot = new();
    private int _nextFile;
    private bool _globalPromptOpen;

    public override void Initialize()
    {
        base.Initialize();
        _resources.AddRoot(RootPath, _audioRoot);
        SubscribeNetworkEvent<PlayLoreMasterAudioEvent>(OnPlayAudio);
        SubscribeNetworkEvent<PromptGlobalAdminAudioUploadEvent>(OnPromptGlobalAudio);
        SubscribeNetworkEvent<PlayUploadedGlobalAdminAudioEvent>(OnPlayUploadedGlobalAudio);
        SubscribeNetworkEvent<PlayMapAdminWavEvent>(OnPlayMapAudio);
        SubscribeNetworkEvent<GlobalAdminAudioUploadResultEvent>(OnGlobalUploadResult);
    }

    private void OnPlayAudio(PlayLoreMasterAudioEvent ev)
    {
        if (!TryGetEntity(ev.Source, out _))
            return;

        var source = GetEntity(ev.Source);
        PlayBytes(ev.Data, ev.Extension, source,
            AudioParams.Default.WithVolume(-10f).WithMaxDistance(16f));
    }

    private void OnPlayUploadedGlobalAudio(PlayUploadedGlobalAdminAudioEvent ev)
    {
        if (!_cfg.GetCVar(CCVars.AdminSoundsEnabled))
            return;

        PlayBytes(ev.Data, ev.Extension, null, AudioParams.Default.WithVolume(ev.Volume));
    }

    private void OnPlayMapAudio(PlayMapAdminWavEvent ev)
    {
        if (_cfg.GetCVar(CCVars.AdminSoundsEnabled))
            PlayBytes(ev.Data, "wav", null, AudioParams.Default.WithVolume(ev.Volume));
    }

    private void OnGlobalUploadResult(GlobalAdminAudioUploadResultEvent ev)
    {
        GlobalUploadResult?.Invoke(ev.Success, ev.Message);
        if (ev.Success)
            _console.LocalShell.WriteLine(ev.Message);
        else
            _console.LocalShell.WriteError(ev.Message);
    }

    private async void OnPromptGlobalAudio(PromptGlobalAdminAudioUploadEvent ev)
    {
        if (_globalPromptOpen)
            return;

        _globalPromptOpen = true;
        try
        {
            await using var file = await _dialogs.OpenFile(
                new FileDialogFilters(new FileDialogFilters.Group("ogg", "wav", "mp3")), FileAccess.Read);
            if (file == null)
                return;

            if (file.Length > AdminAudioFormat.MaxBytes)
                throw new InvalidDataException("Audio exceeds the 3 MB global playback limit.");

            using var copy = new MemoryStream();
            await file.CopyToAsync(copy);
            var data = copy.ToArray();
            if (!AdminAudioFormat.TryGetExtension(data, out var extension))
                throw new InvalidDataException("Choose a valid OGG, PCM WAV, or MP3 file (3 MB max).");

            RaiseNetworkEvent(new UploadGlobalAdminAudioEvent(data, extension));
            _console.LocalShell.WriteLine("Uploading audio for global playback...");
        }
        catch (Exception e) when (e is InvalidDataException or IOException)
        {
            _console.LocalShell.WriteError(e.Message);
        }
        finally
        {
            _globalPromptOpen = false;
        }
    }

    private void PlayBytes(byte[] bytes, string format, EntityUid? source, AudioParams parameters)
    {
        ResPath? relative = null;
        try
        {
            if (!AdminAudioFormat.TryGetExtension(bytes, out var extension, AdminAudioFormat.MaxPlaybackBytes)
                || extension != format)
                return;

            relative = new ResPath($"{_nextFile++}.{extension}");
            _audioRoot.AddOrUpdateFile(relative.Value, bytes);
            var path = RootPath / relative.Value;
            var resource = new AudioResource();
            resource.Load(IoCManager.Instance!, path);
            var specifier = new ResolvedPathSpecifier(path);
            if (source is { } entity)
                _audio.PlayEntity(resource.AudioStream, entity, specifier, parameters);
            else
                _audio.PlayGlobal(resource.AudioStream, specifier, parameters);
        }
        catch (Exception)
        {
            // Ignore a malformed audio file rather than breaking the client's event loop.
        }
        finally
        {
            if (relative != null)
                _audioRoot.RemoveFile(relative.Value);
        }
    }
}
