using Content.Shared.DoAfter;
using Content.Shared.Decals;
using Content.Shared.SprayPainter.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.SprayPainter;

[Serializable, NetSerializable]
public enum SprayPainterUiKey
{
    Key,
}

[Serializable, NetSerializable]
public sealed class SprayPainterSpritePickedMessage : BoundUserInterfaceMessage
{
    public readonly int Index;

    public SprayPainterSpritePickedMessage(int index)
    {
        Index = index;
    }
}

[Serializable, NetSerializable]
public sealed class SprayPainterColorPickedMessage : BoundUserInterfaceMessage
{
    public readonly string? Key;

    public SprayPainterColorPickedMessage(string? key)
    {
        Key = key;
    }
}

[Serializable, NetSerializable]
public sealed class SprayPainterSetCustomColorMessage(Color? color) : BoundUserInterfaceMessage
{
    public readonly Color? Color = color;
}

[Serializable, NetSerializable]
public sealed class SprayPainterSetDecalMessage(ProtoId<DecalPrototype> decal) : BoundUserInterfaceMessage
{
    public readonly ProtoId<DecalPrototype> Decal = decal;
}

[Serializable, NetSerializable]
public sealed class SprayPainterSetDecalColorMessage(Color? color) : BoundUserInterfaceMessage
{
    public readonly Color? Color = color;
}

[Serializable, NetSerializable]
public sealed class SprayPainterSetDecalAngleMessage(int angle) : BoundUserInterfaceMessage
{
    public readonly int Angle = angle;
}

[Serializable, NetSerializable]
public sealed class SprayPainterSetDecalSnapMessage(bool snap) : BoundUserInterfaceMessage
{
    public readonly bool Snap = snap;
}

[Serializable, NetSerializable]
public sealed class SprayPainterSetDecalColorPickerMessage(bool toggle) : BoundUserInterfaceMessage
{
    public readonly bool Toggle = toggle;
}

[Serializable, NetSerializable]
public sealed class SprayPainterSetDecalModeMessage(DecalPaintMode mode) : BoundUserInterfaceMessage
{
    public readonly DecalPaintMode Mode = mode;
}

[Serializable, NetSerializable]
public sealed partial class SprayPainterDoorDoAfterEvent : DoAfterEvent
{
    /// <summary>
    /// Base RSI path to set for the door sprite.
    /// </summary>
    [DataField]
    public string Sprite;

    /// <summary>
    /// Department id to set for the door, if the style has one.
    /// </summary>
    [DataField]
    public string? Department;

    public SprayPainterDoorDoAfterEvent(string sprite, string? department)
    {
        Sprite = sprite;
        Department = department;
    }

    public override DoAfterEvent Clone() => this;
}

[Serializable, NetSerializable]
public sealed partial class SprayPainterPipeDoAfterEvent : DoAfterEvent
{
    /// <summary>
    /// Color of the pipe to set.
    /// </summary>
    [DataField]
    public Color Color;

    public SprayPainterPipeDoAfterEvent(Color color)
    {
        Color = color;
    }

    public override DoAfterEvent Clone() => this;
}
