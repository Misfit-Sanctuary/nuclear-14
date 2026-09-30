// #Misfits Add - Shared pieces of the bunker hatch tunnels: sprite key, door menu and pry do-after.
using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._Misfits.Warps;

/// <summary>
/// Appearance key the hatch uses to swap between its closed and open sprite.
/// </summary>
[Serializable, NetSerializable]
public enum BunkerHatchVisuals : byte
{
    Locked,
}

/// <summary>
/// The menu the tunnel door opens for Enclave members.
/// </summary>
[Serializable, NetSerializable]
public enum BunkerTunnelDoorUiKey : byte
{
    Key,
}

/// <summary>
/// One surface hatch, as shown in the tunnel door menu and on the Enclave tac map.
/// X and Y are world coordinates on the hatch's own map; the tac map uses them to place its icon.
/// </summary>
[Serializable, NetSerializable]
public readonly record struct BunkerHatchEntry(NetEntity Hatch, string Label, bool Locked, float X, float Y);

[Serializable, NetSerializable]
public sealed class BunkerTunnelDoorUiState : BoundUserInterfaceState
{
    public readonly BunkerHatchEntry[] Hatches;

    public BunkerTunnelDoorUiState(BunkerHatchEntry[] hatches)
    {
        Hatches = hatches;
    }
}

/// <summary>
/// Look through this hatch. Null stops looking.
/// </summary>
[Serializable, NetSerializable]
public sealed class BunkerTunnelDoorPreviewMessage : BoundUserInterfaceMessage
{
    public readonly NetEntity? Hatch;

    public BunkerTunnelDoorPreviewMessage(NetEntity? hatch)
    {
        Hatch = hatch;
    }
}

/// <summary>
/// Go up and come out of this hatch.
/// </summary>
[Serializable, NetSerializable]
public sealed class BunkerTunnelDoorGoMessage : BoundUserInterfaceMessage
{
    public readonly NetEntity Hatch;

    public BunkerTunnelDoorGoMessage(NetEntity hatch)
    {
        Hatch = hatch;
    }
}

/// <summary>
/// Raised when someone finishes prying a locked hatch open with a crowbar.
/// </summary>
[Serializable, NetSerializable]
public sealed partial class BunkerHatchPryDoAfterEvent : SimpleDoAfterEvent;
