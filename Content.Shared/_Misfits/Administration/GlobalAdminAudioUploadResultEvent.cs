using Robust.Shared.Serialization;

namespace Content.Shared._Misfits.Administration;

[Serializable, NetSerializable]
public sealed class GlobalAdminAudioUploadResultEvent : EntityEventArgs
{
    public bool Success { get; }
    public string Message { get; }

    public GlobalAdminAudioUploadResultEvent(bool success, string message)
    {
        Success = success;
        Message = message;
    }
}
