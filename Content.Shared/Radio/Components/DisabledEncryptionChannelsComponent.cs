using Content.Shared.Radio;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype.Set;

namespace Content.Shared.Radio.Components;

/// <summary>
/// Removes selected channels from an encryption key or key holder without deleting the item.
/// </summary>
[RegisterComponent]
public sealed partial class DisabledEncryptionChannelsComponent : Component
{
    [DataField("channels", customTypeSerializer: typeof(PrototypeIdHashSetSerializer<RadioChannelPrototype>))]
    public HashSet<string> Channels = new();
}
