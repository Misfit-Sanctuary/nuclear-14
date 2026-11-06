using Content.Server.Radio.Components;
using Robust.Shared.Prototypes;
using Content.Shared.Radio;
using Content.Shared.Radio.Components;

namespace Content.Server.Radio;

public sealed class IntrinsicRadioKeySystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<IntrinsicRadioTransmitterComponent, EncryptionChannelsChangedEvent>(OnTransmitterChannelsChanged);
        SubscribeLocalEvent<ActiveRadioComponent, EncryptionChannelsChangedEvent>(OnReceiverChannelsChanged);
    }

    private void OnTransmitterChannelsChanged(EntityUid uid, IntrinsicRadioTransmitterComponent component, EncryptionChannelsChangedEvent args)
    {
        UpdateChannels(uid, args.Component, ref component.Channels);
    }

    private void OnReceiverChannelsChanged(EntityUid uid, ActiveRadioComponent component, EncryptionChannelsChangedEvent args)
    {
        UpdateChannels(uid, args.Component, ref component.Channels);
    }

    private void UpdateChannels(EntityUid _, EncryptionKeyHolderComponent component, ref HashSet<ProtoId<RadioChannelPrototype>> channels)
    {
        channels.Clear();
        channels.UnionWith(component.Channels.Select(c => new ProtoId<RadioChannelPrototype>(c)));
    }
}
