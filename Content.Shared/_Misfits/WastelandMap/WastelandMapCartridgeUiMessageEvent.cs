// #Misfits Change - Pip-Boy tactical map cartridge messages
using Content.Shared.CartridgeLoader;
using Robust.Shared.Serialization;

namespace Content.Shared._Misfits.WastelandMap;

[Serializable, NetSerializable]
public sealed class WastelandMapCartridgeAddAnnotationMessageEvent : CartridgeMessageEvent
{
    public readonly WastelandMapAnnotation Annotation;

    public WastelandMapCartridgeAddAnnotationMessageEvent(WastelandMapAnnotation annotation)
    {
        Annotation = annotation;
    }
}

[Serializable, NetSerializable]
public sealed class WastelandMapCartridgeRemoveAnnotationMessageEvent : CartridgeMessageEvent
{
    public readonly int Index;

    public WastelandMapCartridgeRemoveAnnotationMessageEvent(int index)
    {
        Index = index;
    }
}

[Serializable, NetSerializable]
public sealed class WastelandMapCartridgeClearAnnotationsMessageEvent : CartridgeMessageEvent;

// #Misfits Add - Pip-Boy (with an Enclave ID): look through a bunker hatch. Null stops looking.
[Serializable, NetSerializable]
public sealed class WastelandMapCartridgeHatchViewMessageEvent : CartridgeMessageEvent
{
    public readonly NetEntity? Hatch;

    public WastelandMapCartridgeHatchViewMessageEvent(NetEntity? hatch)
    {
        Hatch = hatch;
    }
}
