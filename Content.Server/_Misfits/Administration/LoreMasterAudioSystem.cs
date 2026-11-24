using Content.Server.Administration.Managers;
using Content.Shared._Misfits.Administration;
using Content.Shared.Administration;
using Robust.Server.Player;
using Robust.Shared.Player;

namespace Content.Server._Misfits.Administration;

/// <summary>Relays authorized local audio to players around the uploading admin.</summary>
public sealed partial class LoreMasterAudioSystem : EntitySystem
{
    [Dependency] private readonly IAdminManager _admins = default!;
    [Dependency] private readonly IPlayerManager _players = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<UploadLoreMasterAudioEvent>(OnUploadAudio);
        SubscribeNetworkEvent<UploadGlobalAdminAudioEvent>(OnUploadGlobalAudio);
    }

    private void OnUploadAudio(UploadLoreMasterAudioEvent upload, EntitySessionEventArgs args)
    {
        if (!_admins.HasAdminFlag(args.SenderSession, AdminFlags.Fun)
            || args.SenderSession.AttachedEntity is not { Valid: true } source
            || !ValidAudio(upload.Data, upload.Extension))
            return;

        if (!TryPrepareAudio(upload.Data, upload.Extension, out var data, out var extension, out _))
            return;

        RaiseNetworkEvent(new PlayLoreMasterAudioEvent(data, extension, GetNetEntity(source)),
            Filter.Empty().AddPlayersByPvs(source));
    }

    private void OnUploadGlobalAudio(UploadGlobalAdminAudioEvent upload, EntitySessionEventArgs args)
    {
        if (!_admins.HasAdminFlag(args.SenderSession, AdminFlags.Fun))
        {
            RaiseNetworkEvent(new GlobalAdminAudioUploadResultEvent(false, "Fun permission is required."),
                Filter.SinglePlayer(args.SenderSession));
            return;
        }

        if (!ValidAudio(upload.Data, upload.Extension))
        {
            RaiseNetworkEvent(new GlobalAdminAudioUploadResultEvent(false, "Invalid audio or file exceeds 3 MB."),
                Filter.SinglePlayer(args.SenderSession));
            return;
        }

        if (!TryPrepareAudio(upload.Data, upload.Extension, out var data, out var extension, out var error))
        {
            RaiseNetworkEvent(new GlobalAdminAudioUploadResultEvent(false, error),
                Filter.SinglePlayer(args.SenderSession));
            return;
        }

        RaiseNetworkEvent(new PlayUploadedGlobalAdminAudioEvent(data, extension, -8f),
            Filter.Empty().AddAllPlayers(_players));
        RaiseNetworkEvent(new GlobalAdminAudioUploadResultEvent(true, "Audio uploaded and playing globally."),
            Filter.SinglePlayer(args.SenderSession));
    }

    private static bool ValidAudio(byte[] data, string extension)
    {
        return AdminAudioFormat.TryGetExtension(data, out var actualExtension)
            && extension == actualExtension;
    }

    private static bool TryPrepareAudio(byte[] input, string extension, out byte[] data, out string format, out string error)
    {
        data = input;
        format = extension;
        error = string.Empty;
        if (extension != "mp3")
            return true;

        try
        {
            data = AdminMp3Converter.DecodeToWav(input);
            format = "wav";
            return true;
        }
        catch (Exception e)
        {
            error = $"MP3 could not be decoded: {e.Message}";
            return false;
        }
    }
}
