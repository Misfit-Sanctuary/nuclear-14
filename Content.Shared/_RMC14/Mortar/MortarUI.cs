using Robust.Shared.Serialization;

namespace Content.Shared._RMC14.Mortar;

[Serializable, NetSerializable]
public enum MortarUiKey
{
    Key,
}

[Serializable, NetSerializable]
public sealed class MortarSetBuiMsg(Vector2i target, Vector2i dial) : BoundUserInterfaceMessage
{
    public Vector2i Target = target;
    public Vector2i Dial = dial;
}

/// Toggles the user's view between their body and the impact camera this mortar last put down.
[Serializable, NetSerializable]
public sealed class MortarCameraBuiMsg : BoundUserInterfaceMessage;

