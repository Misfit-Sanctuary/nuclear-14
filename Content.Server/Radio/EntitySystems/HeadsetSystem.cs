using System;
using System.Collections.Generic;
using System.Linq;
using Content.Server.Chat.Systems;
using Content.Server.Emp;
using Content.Server.Language;
using Content.Server.Radio.Components;
using Content.Server.Speech;
using Content.Shared.Chat;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;
using Content.Shared.Mind;
using Content.Shared.Radio;
using Content.Shared.Radio.Components;
using Content.Shared.Radio.EntitySystems;
using Content.Shared.Verbs;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server.Radio.EntitySystems;

public sealed class HeadsetSystem : SharedHeadsetSystem
{
    [Dependency] private readonly INetManager _netMan = default!;
    [Dependency] private readonly RadioSystem _radio = default!;
    [Dependency] private readonly LanguageSystem _language = default!;
    [Dependency] private readonly EncryptionKeySystem _encryptionKeys = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly SharedMindSystem _mind = default!; // #Misfits Add - remote pilots keep hearing their radio

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<HeadsetComponent, RadioReceiveEvent>(OnHeadsetReceive);
        SubscribeLocalEvent<HeadsetComponent, EncryptionChannelsChangedEvent>(OnKeysChanged);
        SubscribeLocalEvent<HeadsetComponent, InventoryRelayedEvent<GetVerbsEvent<EquipmentVerb>>>(OnGetEquipmentVerbs);

        SubscribeLocalEvent<WearingHeadsetComponent, EntitySpokeEvent>(OnSpeak);
        // Misfits Add - broadcast acronym/smiley emotes over headset radio
        SubscribeLocalEvent<WearingHeadsetComponent, EntitySpokeRadioEmoteEvent>(OnSpokeRadioEmote);

        SubscribeLocalEvent<HeadsetComponent, EmpPulseEvent>(OnEmpPulse);
    }

    private void OnGetEquipmentVerbs(Entity<HeadsetComponent> ent, ref InventoryRelayedEvent<GetVerbsEvent<EquipmentVerb>> args)
    {
        if (!args.Args.CanAccess ||
            !args.Args.CanInteract ||
            !TryComp<EncryptionKeyHolderComponent>(ent, out var holder))
        {
            return;
        }

        var wearer = Transform(ent).ParentUid;
        if (args.Args.User != wearer)
            return;

        var channels = new HashSet<string>();
        foreach (var keyUid in holder.KeyContainer.ContainedEntities)
        {
            if (!TryComp<EncryptionKeyComponent>(keyUid, out var key))
                continue;

            channels.UnionWith(key.Channels);
        }

        if (channels.Count == 0)
            return;

        var orderedChannels = channels.ToList();
        orderedChannels.Sort(StringComparer.OrdinalIgnoreCase);

        foreach (var channelId in orderedChannels)
        {
            var holderDisabled = IsHeadsetChannelDisabled(ent, channelId);
            var keyDisabled = IsInsertedKeyChannelDisabled(holder, channelId);
            var disabled = holderDisabled || keyDisabled;
            var channelName = GetChannelName(channelId);
            var verb = new EquipmentVerb
            {
                Text = disabled ? $"Enable {channelName} radio" : $"Disable {channelName} radio",
                Category = VerbCategory.ChannelSelect,
                Priority = -5,
                Disabled = keyDisabled,
                Message = keyDisabled
                    ? "This encryption key was disabled by command and cannot be restored from the headset."
                    : null,
                Act = () => SetHeadsetChannelDisabled(ent, channelId, !holderDisabled),
            };

            args.Args.Verbs.Add(verb);
        }
    }

    private string GetChannelName(string channelId)
    {
        return _prototype.TryIndex<RadioChannelPrototype>(channelId, out var channel)
            ? channel.LocalizedName
            : channelId;
    }

    private bool IsHeadsetChannelDisabled(EntityUid uid, string channelId)
    {
        return TryComp<DisabledEncryptionChannelsComponent>(uid, out var disabled) &&
               disabled.Channels.Contains(channelId);
    }

    private bool IsInsertedKeyChannelDisabled(EncryptionKeyHolderComponent holder, string channelId)
    {
        foreach (var keyUid in holder.KeyContainer.ContainedEntities)
        {
            if (!TryComp<EncryptionKeyComponent>(keyUid, out var key) ||
                !key.Channels.Contains(channelId))
            {
                continue;
            }

            if (TryComp<DisabledEncryptionChannelsComponent>(keyUid, out var disabled) &&
                disabled.Channels.Contains(channelId))
            {
                return true;
            }
        }

        return false;
    }

    private void SetHeadsetChannelDisabled(EntityUid uid, string channelId, bool disabled)
    {
        if (!TryComp<EncryptionKeyHolderComponent>(uid, out var holder))
            return;

        if (disabled)
        {
            var disabledChannels = EnsureComp<DisabledEncryptionChannelsComponent>(uid);
            if (!disabledChannels.Channels.Add(channelId))
                return;

            Dirty(uid, disabledChannels);
        }
        else
        {
            if (!TryComp<DisabledEncryptionChannelsComponent>(uid, out var disabledChannels) ||
                !disabledChannels.Channels.Remove(channelId))
            {
                return;
            }

            if (disabledChannels.Channels.Count == 0)
                RemComp<DisabledEncryptionChannelsComponent>(uid);
            else
                Dirty(uid, disabledChannels);
        }

        _encryptionKeys.UpdateChannels(uid, holder);
    }

    private void OnKeysChanged(EntityUid uid, HeadsetComponent component, EncryptionChannelsChangedEvent args)
    {
        UpdateRadioChannels(uid, component, args.Component);
    }

    private void UpdateRadioChannels(EntityUid uid, HeadsetComponent headset, EncryptionKeyHolderComponent? keyHolder = null)
    {
        // make sure to not add ActiveRadioComponent when headset is being deleted
        if (!headset.Enabled || MetaData(uid).EntityLifeStage >= EntityLifeStage.Terminating)
            return;

        // #Misfits Change - don't early-return if no keyHolder; passive channels still need to be applied.
        Resolve(uid, ref keyHolder, logMissing: false);

        // #Misfits Add - merge key channels with passive listen-only channels (e.g. PBS broadcast).
        // PassiveChannels are always received regardless of inserted encryption keys,
        // but transmitting on them still requires the matching EncryptionKey (OnSpeak checks keyHolder.Channels).
        // Note: keyHolder may be null if the headset has no inserted keys — use empty set as base in that case.
        var keyChannels = keyHolder != null ? keyHolder.Channels : new HashSet<string>();
        var allReceiveChannels = new HashSet<string>(keyChannels);
        allReceiveChannels.UnionWith(headset.PassiveChannels);

        if (allReceiveChannels.Count == 0)
            RemComp<ActiveRadioComponent>(uid);
        else
            EnsureComp<ActiveRadioComponent>(uid).Channels = allReceiveChannels;
        // End #Misfits Add
    }

    private void OnSpeak(EntityUid uid, WearingHeadsetComponent component, EntitySpokeEvent args)
    {
        if (args.Channel != null
            && TryComp(component.Headset, out EncryptionKeyHolderComponent? keys)
            && keys.Channels.Contains(args.Channel.ID))
        {
            _radio.SendRadioMessage(uid, args.Message, args.Channel, component.Headset);
            args.Channel = null; // prevent duplicate messages from other listeners.
        }
    }

    // Misfits Add - routes acronym/smiley emotes over the headset's radio channel
    private void OnSpokeRadioEmote(EntityUid uid, WearingHeadsetComponent component, EntitySpokeRadioEmoteEvent args)
    {
        if (args.Channel != null
            && TryComp(component.Headset, out EncryptionKeyHolderComponent? keys)
            && keys.Channels.Contains(args.Channel.ID))
        {
            _radio.SendRadioEmote(uid, args.EmoteText, args.Channel, component.Headset, args.Language);
            args.Channel = null; // prevent duplicate broadcasts
        }
    }
    // End Misfits Add

    protected override void OnGotEquipped(EntityUid uid, HeadsetComponent component, GotEquippedEvent args)
    {
        base.OnGotEquipped(uid, component, args);
        if (component.IsEquipped && component.Enabled)
        {
            EnsureComp<WearingHeadsetComponent>(args.Equipee).Headset = uid;
            UpdateRadioChannels(uid, component);
        }
    }

    protected override void OnGotUnequipped(EntityUid uid, HeadsetComponent component, GotUnequippedEvent args)
    {
        base.OnGotUnequipped(uid, component, args);
        component.IsEquipped = false;
        RemComp<ActiveRadioComponent>(uid);
        RemComp<WearingHeadsetComponent>(args.Equipee);
    }

    public void SetEnabled(EntityUid uid, bool value, HeadsetComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return;

        if (component.Enabled == value)
            return;

        if (!value)
        {
            RemCompDeferred<ActiveRadioComponent>(uid);

            if (component.IsEquipped)
                RemCompDeferred<WearingHeadsetComponent>(Transform(uid).ParentUid);
        }
        else if (component.IsEquipped)
        {
            EnsureComp<WearingHeadsetComponent>(Transform(uid).ParentUid).Headset = uid;
            UpdateRadioChannels(uid, component);
        }
    }

    private void OnHeadsetReceive(EntityUid uid, HeadsetComponent component, ref RadioReceiveEvent args)
    {
        var parent = Transform(uid).ParentUid;

        // #Misfits Fix - a wearer off running a camera remotely, like a vertibird gunner, has their
        // session attached to the camera, so the headset still on their head has no actor to send to.
        ICommonSession? session = TryComp(parent, out ActorComponent? actor)
            ? actor.PlayerSession
            : _mind.TryGetPilotingSession(parent, out var piloting) ? piloting : null;

        if (session == null)
            return;

        var canUnderstand = _language.CanUnderstand(parent, args.Language.ID);
        var msg = new MsgChatMessage
        {
            Message = canUnderstand ? args.OriginalChatMsg : args.LanguageObfuscatedChatMsg
        };
        _netMan.ServerSendMessage(msg, session.Channel);
    }

    private void OnEmpPulse(EntityUid uid, HeadsetComponent component, ref EmpPulseEvent args)
    {
        if (component.Enabled)
        {
            args.Affected = true;
            args.Disabled = true;
        }
    }
}
