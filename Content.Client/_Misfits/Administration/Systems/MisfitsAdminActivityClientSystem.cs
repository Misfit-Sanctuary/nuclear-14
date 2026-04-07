// #Misfits Add - Client-side system for the Staff Panel "Activity Log" button.
// Provides a typed wrapper around RaiseNetworkEvent so the StaffTab Control can trigger it.
using Content.Shared._Misfits.Administration;

namespace Content.Client._Misfits.Administration.Systems;

public sealed class MisfitsAdminActivityClientSystem : EntitySystem
{
    /// <summary>
    /// Sends the server a request to open the AdminLogsEui pre-filtered to admin-action types.
    /// </summary>
    public void RequestActivityLog()
    {
        RaiseNetworkEvent(new MisfitsOpenActivityLogMsg());
    }
}
