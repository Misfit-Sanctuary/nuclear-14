using Robust.Shared.Serialization;

namespace Content.Shared._RMC14.Mortar;

[Serializable, NetSerializable]
public enum MortarUiKey
{
    Key,
}

[Serializable, NetSerializable]
public sealed class MortarTargetBuiMsg(Vector2i target) : BoundUserInterfaceMessage
{
    public Vector2i Target = target;
}

[Serializable, NetSerializable]
public sealed class MortarDialBuiMsg(Vector2i target) : BoundUserInterfaceMessage
{
    public Vector2i Target = target;
}

/// Toggles the user's view between their body and the impact camera this mortar last put down.

[Serializable, NetSerializable]
public sealed class MortarCameraBuiMsg : BoundUserInterfaceMessage;
