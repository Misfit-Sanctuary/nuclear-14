using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._Misfits.Gallows;

[Serializable, NetSerializable]
public sealed partial class GallowsAttachDoAfterEvent : SimpleDoAfterEvent { }
