// #Misfits Add - Events and messages for the graffiti spray can BUI and do-after flow.

using Content.Shared.DoAfter;
using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._Misfits.GraffitiSprayCan;

/// <summary>
/// BUI key for the graffiti spray can picker window.
/// </summary>
[Serializable, NetSerializable]
public enum GraffitiSprayCanUiKey
{
    Key,
}

/// <summary>
/// Sent by the client when the player selects a graffiti decal from the picker.
/// </summary>
[Serializable, NetSerializable]
public sealed class GraffitiDecalSelectedMessage : BoundUserInterfaceMessage
{
    public readonly string DecalId;

    public GraffitiDecalSelectedMessage(string decalId)
    {
        DecalId = decalId;
    }
}

/// <summary>
/// Do-after event fired when the spray animation completes, carrying the decal ID and target coordinates.
/// </summary>
[Serializable, NetSerializable]
public sealed partial class GraffitiSprayCanDoAfterEvent : DoAfterEvent
{
    /// <summary>
    /// ID of the DecalPrototype to place.
    /// </summary>
    [DataField]
    public string DecalId = string.Empty;

    /// <summary>
    /// Where the decal should be placed (network coordinates).
    /// </summary>
    [DataField]
    public NetCoordinates Coordinates;

    public GraffitiSprayCanDoAfterEvent(string decalId, NetCoordinates coordinates)
    {
        DecalId = decalId;
        Coordinates = coordinates;
    }

    public override DoAfterEvent Clone() => this;
}
