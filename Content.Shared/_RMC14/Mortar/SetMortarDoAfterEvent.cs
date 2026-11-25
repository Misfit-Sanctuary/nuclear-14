using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._RMC14.Mortar;

[Serializable, NetSerializable]
public sealed partial class SetMortarDoAfterEvent : SimpleDoAfterEvent
{
    public readonly Vector2i Target;
    public readonly Vector2i Dial;

    public SetMortarDoAfterEvent(Vector2i target, Vector2i dial)
    {
        Target = target;
        Dial = dial;
    }
}
