using Robust.Shared.Serialization;

namespace Content.Shared._Misfits.Administration;

[Serializable, NetSerializable]
public sealed class UploadLoreMasterAudioEvent : EntityEventArgs
{
    public byte[] Data { get; }
    public string Extension { get; }

    public UploadLoreMasterAudioEvent(byte[] data, string extension)
    {
        Data = data;
        Extension = extension;
    }
}

[Serializable, NetSerializable]
public sealed class PlayLoreMasterAudioEvent : EntityEventArgs
{
    public byte[] Data { get; }
    public string Extension { get; }
    public NetEntity Source { get; }

    public PlayLoreMasterAudioEvent(byte[] data, string extension, NetEntity source)
    {
        Data = data;
        Extension = extension;
        Source = source;
    }
}

[Serializable, NetSerializable]
public sealed class PromptGlobalAdminAudioUploadEvent : EntityEventArgs
{
}

[Serializable, NetSerializable]
public sealed class UploadGlobalAdminAudioEvent : EntityEventArgs
{
    public byte[] Data { get; }
    public string Extension { get; }

    public UploadGlobalAdminAudioEvent(byte[] data, string extension)
    {
        Data = data;
        Extension = extension;
    }
}

[Serializable, NetSerializable]
public sealed class PlayUploadedGlobalAdminAudioEvent : EntityEventArgs
{
    public byte[] Data { get; }
    public string Extension { get; }
    public float Volume { get; }

    public PlayUploadedGlobalAdminAudioEvent(byte[] data, string extension, float volume)
    {
        Data = data;
        Extension = extension;
        Volume = volume;
    }
}

[Serializable, NetSerializable]
public sealed class PlayMapAdminWavEvent : EntityEventArgs
{
    public byte[] Data { get; }
    public float Volume { get; }

    public PlayMapAdminWavEvent(byte[] data, float volume)
    {
        Data = data;
        Volume = volume;
    }
}
