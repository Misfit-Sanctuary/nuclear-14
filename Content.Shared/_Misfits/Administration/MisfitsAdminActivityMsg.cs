// #Misfits Add - Net message for opening the admin activity log from the Staff Panel
using Robust.Shared.Serialization;

namespace Content.Shared._Misfits.Administration;

/// <summary>
/// Client → Server: request to open AdminLogsEui pre-filtered to significant admin actions for the current round.
/// </summary>
[Serializable, NetSerializable]
public sealed class MisfitsOpenActivityLogMsg : EntityEventArgs
{
    // No fields — server determines current round and applies the hardcoded admin-action type/impact filter.
}
