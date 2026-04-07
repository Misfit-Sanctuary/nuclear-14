// #Misfits Add — DoAfter event fired when a tribal finishes the pray animation at the Ug-Qualtoth idol.
using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._Misfits.UgQualtoth;

[Serializable, NetSerializable]
public sealed partial class UgQualtothPrayDoAfterEvent : SimpleDoAfterEvent
{
}
