using Robust.Shared.Serialization;

namespace Content.Shared._Misfits.Warps;

[Serializable, NetSerializable]
public sealed class AutoWarperTravelEvent(float durationSeconds) : EntityEventArgs
{
    public readonly float DurationSeconds = durationSeconds;
}

[Serializable, NetSerializable]
public sealed class AutoWarperArrivalEvent(string placeName) : EntityEventArgs
{
    public readonly string PlaceName = placeName;
}
