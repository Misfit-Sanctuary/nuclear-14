using Robust.Shared.Serialization;

namespace Content.Shared._Misfits.WastelandMap;

/// <summary>Sent when an administrator names a new global tactical-map pin.</summary>
[Serializable, NetSerializable]
public sealed class MapPinNameMessage : BoundUserInterfaceMessage
{
    public readonly string Name;

    public MapPinNameMessage(string name)
    {
        Name = name;
    }
}

[Serializable, NetSerializable]
public enum MapPinManageUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public readonly record struct GlobalMapPinEntry(NetEntity Pin, string Label, float X, float Y);

[Serializable, NetSerializable]
public sealed class MapPinManageState(GlobalMapPinEntry[] pins) : BoundUserInterfaceState
{
    public readonly GlobalMapPinEntry[] Pins = pins;
}

[Serializable, NetSerializable]
public sealed class MapPinRemoveMessage(NetEntity pin) : BoundUserInterfaceMessage
{
    public readonly NetEntity Pin = pin;
}
