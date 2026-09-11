using Content.Server.Administration.Logs;
using Content.Server.Chat.Systems;
using Content.Server.Language;
using Content.Server.Power.Components;
using Content.Server.Radio.Components;
using Content.Shared._Misfits.Expeditions;
using Content.Shared._Misfits.Special;
using Content.Shared._MultiZ.Core.Components;
using Content.Server._MultiZ.Core;
using Content.Shared.Chat;
using Content.Shared.Database;
using Content.Shared.Language;
using Content.Shared.Language.Systems;
using Content.Shared.Radio;
using Content.Shared.Radio.Components;
using Content.Shared.Speech;
using Content.Shared.Ghost; // Nuclear-14
using Robust.Shared.Network;
using Content.Shared.Mind;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Replays;
using Robust.Shared.Utility;

namespace Content.Server.Radio.EntitySystems;

/// <summary>
///     This system handles intrinsic radios and the general process of converting radio messages into chat messages.
/// </summary>
public sealed class RadioSystem : EntitySystem
{
    [Dependency] private readonly INetManager _netMan = default!;
    [Dependency] private readonly SharedMindSystem _mind = default!; // #Misfits Add - remote pilots keep hearing their radio
    [Dependency] private readonly IReplayRecordingManager _replay = default!;
    [Dependency] private readonly IAdminLogManager _adminLogger = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly LanguageSystem _language = default!;
    [Dependency] private readonly SharedSpecialSystem _special = default!;
    // Inject the concrete MZSystem: MZSharedSystem has two server subtypes, so Robust
    // refuses to resolve the supertype.
    [Dependency] private readonly MZSystem _multiZ = default!;

    // set used to prevent radio feedback loops.
    private readonly HashSet<string> _messages = new();

    private EntityQuery<TelecomExemptComponent> _exemptQuery;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<IntrinsicRadioReceiverComponent, RadioReceiveEvent>(OnIntrinsicReceive);
        SubscribeLocalEvent<IntrinsicRadioTransmitterComponent, EntitySpokeEvent>(OnIntrinsicSpeak);
        // Misfits Add - broadcast acronym/smiley emotes over radio
        SubscribeLocalEvent<IntrinsicRadioTransmitterComponent, EntitySpokeRadioEmoteEvent>(OnIntrinsicSpokeRadioEmote);

        _exemptQuery = GetEntityQuery<TelecomExemptComponent>();
    }

    private void OnIntrinsicSpeak(EntityUid uid, IntrinsicRadioTransmitterComponent component, EntitySpokeEvent args)
    {
        if (args.Channel != null && component.Channels.Contains(args.Channel.ID))
        {
            SendRadioMessage(uid, args.Message, args.Channel, uid, args.Language);
            args.Channel = null; // prevent duplicate messages from other listeners.
        }
    }

    // Misfits Add — handler for acronym/smiley emotes on intrinsic radio transmitters
    private void OnIntrinsicSpokeRadioEmote(EntityUid uid, IntrinsicRadioTransmitterComponent component, EntitySpokeRadioEmoteEvent args)
    {
        if (args.Channel != null && component.Channels.Contains(args.Channel.ID))
        {
            SendRadioEmote(uid, args.EmoteText, args.Channel, uid, args.Language);
            args.Channel = null; // prevent duplicate broadcasts
        }
    }
    // End Misfits Add

    //Nuclear-14
    /// <summary>
    /// Gets the message frequency, if there is no such frequency, returns the standard channel frequency.
    /// </summary>
    public int GetFrequency(EntityUid source, RadioChannelPrototype channel)
    {
        if (TryComp<RadioMicrophoneComponent>(source, out var radioMicrophone))
            return radioMicrophone.Frequency;

        return channel.Frequency;
    }

    private void OnIntrinsicReceive(EntityUid uid, IntrinsicRadioReceiverComponent component, ref RadioReceiveEvent args)
    {
        // #Misfits Fix - same as the headset case: someone off running a camera remotely has their
        // session attached to it, so the body carrying the radio has no actor of its own to send to.
        ICommonSession? session = TryComp(uid, out ActorComponent? actor)
            ? actor.PlayerSession
            : _mind.TryGetPilotingSession(uid, out var piloting) ? piloting : null;

        if (session == null)
            return;

        // Einstein-Engines - languages mechanic
        var listener = component.Owner;
        var msg = args.OriginalChatMsg;

        if (listener != null && !_language.CanUnderstand(listener, args.Language.ID))
            msg = args.LanguageObfuscatedChatMsg;

        _netMan.ServerSendMessage(new MsgChatMessage { Message = msg}, session.Channel);
    }

    /// <summary>
    /// Send radio message to all active radio listeners
    /// </summary>
    public void SendRadioMessage(
        EntityUid messageSource,
        string message,
        ProtoId<RadioChannelPrototype> channel,
        EntityUid radioSource,
        int? frequency = null,
        LanguagePrototype? language = null,
        bool escapeMarkup = true
        ) =>
        SendRadioMessage(messageSource, message, _prototype.Index(channel), radioSource, escapeMarkup: escapeMarkup, frequency: frequency, language: language);

    /// <summary>
    /// Send radio message to all active radio listeners
    /// </summary>
    public void SendRadioMessage(
        EntityUid messageSource,
        string message,
        RadioChannelPrototype channel,
        EntityUid radioSource,
        LanguagePrototype? language = null,
        int? frequency = null,
        bool escapeMarkup = true)
    {
        if (language == null)
            language = _language.GetLanguage(messageSource);

        if (!language.SpeechOverride.AllowRadio)
            return;

        // TODO if radios ever garble / modify messages, feedback-prevention needs to be handled better than this.
        if (!_messages.Add(message))
            return;

        var evt = new TransformSpeakerNameEvent(messageSource, Name(messageSource));
        RaiseLocalEvent(messageSource, evt);
        var name = evt.VoiceName;

        name = FormattedMessage.EscapeText(name);

        // most radios are relayed to chat, so lets parse the chat message beforehand
        var content = escapeMarkup
            ? FormattedMessage.EscapeText(message)
            : message;

        var wrappedMessage = WrapRadioMessage(messageSource, channel, name, content, language, frequency);
        var msg = new ChatMessage(ChatChannel.Radio, content, wrappedMessage, NetEntity.Invalid, null);

        // ... you guess it
        var obfuscated = _language.ObfuscateSpeech(content, language);
        var obfuscatedWrapped = WrapRadioMessage(messageSource, channel, name, obfuscated, language, frequency);
        var notUdsMsg = new ChatMessage(ChatChannel.Radio, obfuscated, obfuscatedWrapped, NetEntity.Invalid, null);

        var ev = new RadioReceiveEvent(messageSource, channel, msg, notUdsMsg, language, radioSource);

        var sendAttemptEv = new RadioSendAttemptEvent(channel, radioSource);
        RaiseLocalEvent(ref sendAttemptEv);
        RaiseLocalEvent(radioSource, ref sendAttemptEv);
        var canSend = !sendAttemptEv.Cancelled;

        var sourceMap = Transform(radioSource).MapUid;
        var sourceZNetwork = GetZNetwork(sourceMap);
        var hasActiveServer = HasActiveServer(sourceMap, sourceZNetwork, channel.ID);
        var hasMicro = HasComp<RadioMicrophoneComponent>(radioSource);

        var speakerQuery = GetEntityQuery<RadioSpeakerComponent>();
        var radioQuery = EntityQueryEnumerator<ActiveRadioComponent, TransformComponent>();

        if (frequency == null) // Nuclear-14
            frequency = GetFrequency(messageSource, channel); // Nuclear-14

        while (canSend && radioQuery.MoveNext(out var receiver, out var radio, out var transform))
        {
            if (!radio.ReceiveAllChannels)
            {
                if (!radio.Channels.Contains(channel.ID) || (TryComp<IntercomComponent>(receiver, out var intercom) &&
                                                             !intercom.SupportedChannels.Contains(channel.ID)))
                    continue;
            }

            if (!HasComp<GhostComponent>(receiver) && GetFrequency(receiver, channel) != frequency)
                continue;

            if (!channel.LongRange && !InRadioRange(sourceMap, sourceZNetwork, transform.MapUid) && !radio.GlobalReceive)
                continue;

            // don't need telecom server for long range channels or handheld radios and intercoms
            var needServer = !channel.LongRange && (!hasMicro || !speakerQuery.HasComponent(receiver));
            if (needServer && !hasActiveServer)
                continue;

            // check if message can be sent to specific receiver
            var attemptEv = new RadioReceiveAttemptEvent(channel, radioSource, receiver);
            RaiseLocalEvent(ref attemptEv);
            RaiseLocalEvent(receiver, ref attemptEv);
            if (attemptEv.Cancelled)
                continue;

            // send the message
            RaiseLocalEvent(receiver, ref ev);
        }

        if (name != Name(messageSource))
            _adminLogger.Add(LogType.Chat, LogImpact.Low, $"Radio message from {ToPrettyString(messageSource):user} as {name} on {channel.LocalizedName}: {message}");
        else
            _adminLogger.Add(LogType.Chat, LogImpact.Low, $"Radio message from {ToPrettyString(messageSource):user} on {channel.LocalizedName}: {message}");

        _replay.RecordServerMessage(msg);
        _messages.Remove(message);
    }

    // Misfits Add — broadcasts an emote action over a radio channel using an emote-style wrap
    // (no speech verb / no quotes) so it reads e.g. "[Wasteland] Viktoriya laughs over radio."
    public void SendRadioEmote(
        EntityUid messageSource,
        string emoteText,
        RadioChannelPrototype channel,
        EntityUid radioSource,
        LanguagePrototype? language = null)
    {
        if (language == null)
            language = _language.GetLanguage(messageSource);

        if (!language.SpeechOverride.AllowRadio)
            return;

        // Use the same feedback-prevention guard as SendRadioMessage
        if (!_messages.Add(emoteText))
            return;

        var nameEv = new TransformSpeakerNameEvent(messageSource, Name(messageSource));
        RaiseLocalEvent(messageSource, nameEv);
        var name = FormattedMessage.EscapeText(nameEv.VoiceName);

        // Append "over the radio" so it reads e.g. "laughs over the radio."
        var emoteBase = emoteText.TrimEnd();
        if (emoteBase.EndsWith('.'))
            emoteBase = emoteBase[..^1];
        var radioEmoteText = $"{emoteBase} {Loc.GetString("chatsan-radio-emote-suffix")}.";
        var content = FormattedMessage.EscapeText(radioEmoteText);

        var channelText = channel.ShowFrequency
            ? $"\\[{GetFrequency(messageSource, channel)}\\]"
            : $"\\[{channel.LocalizedName}\\]";

        var wrappedMessage = Loc.GetString("chat-radio-emote-wrap",
            ("color", channel.Color),
            ("channel", channelText),
            ("name", name),
            ("emote", content));

        var msg = new ChatMessage(ChatChannel.Radio, content, wrappedMessage, NetEntity.Invalid, null);
        var ev = new RadioReceiveEvent(messageSource, channel, msg, msg, language, radioSource);

        var sendAttemptEv = new RadioSendAttemptEvent(channel, radioSource);
        RaiseLocalEvent(ref sendAttemptEv);
        RaiseLocalEvent(radioSource, ref sendAttemptEv);
        var canSend = !sendAttemptEv.Cancelled;

        var sourceMap = Transform(radioSource).MapUid;
        var sourceZNetwork = GetZNetwork(sourceMap);
        var hasActiveServer = HasActiveServer(sourceMap, sourceZNetwork, channel.ID);
        var hasMicro = HasComp<RadioMicrophoneComponent>(radioSource);
        var frequency = GetFrequency(messageSource, channel);

        var speakerQuery = GetEntityQuery<RadioSpeakerComponent>();
        var radioQuery = EntityQueryEnumerator<ActiveRadioComponent, TransformComponent>();

        while (canSend && radioQuery.MoveNext(out var receiver, out var radio, out var transform))
        {
            if (!radio.ReceiveAllChannels)
            {
                if (!radio.Channels.Contains(channel.ID) || (TryComp<IntercomComponent>(receiver, out var intercom) &&
                                                             !intercom.SupportedChannels.Contains(channel.ID)))
                    continue;
            }

            if (!HasComp<GhostComponent>(receiver) && GetFrequency(receiver, channel) != frequency)
                continue;

            if (!channel.LongRange && !InRadioRange(sourceMap, sourceZNetwork, transform.MapUid) && !radio.GlobalReceive)
                continue;

            var needServer = !channel.LongRange && (!hasMicro || !speakerQuery.HasComponent(receiver));
            if (needServer && !hasActiveServer)
                continue;

            var attemptEv = new RadioReceiveAttemptEvent(channel, radioSource, receiver);
            RaiseLocalEvent(ref attemptEv);
            RaiseLocalEvent(receiver, ref attemptEv);
            if (attemptEv.Cancelled)
                continue;

            RaiseLocalEvent(receiver, ref ev);
        }

        _adminLogger.Add(LogType.Chat, LogImpact.Low,
            $"Radio emote from {ToPrettyString(messageSource):user} on {channel.LocalizedName}: {emoteText}");
        _replay.RecordServerMessage(msg);
        _messages.Remove(emoteText);
    }
    // End Misfits Add (SendRadioEmote)

    private string WrapRadioMessage(
        EntityUid source,
        RadioChannelPrototype channel,
        string name,
        string message,
        LanguagePrototype language,
        int? frequency = null)
    {
        // TODO: code duplication with ChatSystem.WrapMessage
        var speech = _chat.GetSpeechVerb(source, message);
        var languageColor = channel.Color;
        if (language.SpeechOverride.Color is { } colorOverride)
            languageColor = Color.InterpolateBetween(languageColor, colorOverride, colorOverride.A);
        var languageDisplay = language.IsVisibleLanguage
            ? Loc.GetString("chat-manager-language-prefix", ("language", language.ChatName))
            : "";
        var messageColor = language.IsVisibleLanguage ? languageColor : channel.Color;
        var fontSize = _special.GetCharismaChatFontSize(source, language.SpeechOverride.FontSize ?? speech.FontSize);

        string channelText;
        if (channel.ShowFrequency && frequency.HasValue)
            channelText = $"\\[{frequency}\\]";
        else
            channelText = $"\\[{channel.LocalizedName}\\]";

        return Loc.GetString(speech.Bold ? "chat-radio-message-wrap-bold" : "chat-radio-message-wrap",
            ("color", channel.Color),
            ("languageColor", languageColor),
            ("messageColor", messageColor),
            ("fontType", language.SpeechOverride.FontId ?? speech.FontId),
            ("fontSize", fontSize),
            ("verb", Loc.GetString(_random.Pick(speech.SpeechVerbStrings))),
            ("channel", channelText),
            ("name", name),
            ("message", message),
            ("language", languageDisplay));
    }

    /// <summary>
    /// Gets the Z-level network the given map belongs to, if any. Resolve this once per broadcast:
    /// the lookup falls back to scanning every network when the map isn't part of one.
    /// </summary>
    private Entity<MZNetworkComponent>? GetZNetwork(EntityUid? map)
    {
        if (map == null)
            return null;

        return _multiZ.TryGetZNetwork(map.Value, out var network) ? network : null;
    }

    /// <summary>
    /// Every Z-level of a station is its own map, so a radio on another level of the same
    /// Z-network counts as being on the source map.
    /// </summary>
    private bool InRadioRange(EntityUid? sourceMap, Entity<MZNetworkComponent>? sourceZNetwork, EntityUid? map)
    {
        if (map == sourceMap)
            return true;

        // Expeditions are deliberately isolated game maps, not MultiZ levels.
        // A live expedition therefore acts as a radio relay to the main server
        // without turning every normal map pair into long-range radio.
        if (IsExpeditionMap(sourceMap) || IsExpeditionMap(map))
            return true;

        return map != null && sourceZNetwork is { } network && _multiZ.IsMapInNetwork(network, map.Value);
    }

    private bool IsExpeditionMap(EntityUid? map) =>
        map is { } mapUid && HasComp<N14ExpeditionComponent>(mapUid);

    /// <inheritdoc cref="TelecomServerComponent"/>
    private bool HasActiveServer(EntityUid? sourceMap, Entity<MZNetworkComponent>? sourceZNetwork, string channelId)
    {
        var servers = EntityQuery<TelecomServerComponent, EncryptionKeyHolderComponent, ApcPowerReceiverComponent, TransformComponent>();
        foreach (var (_, keys, power, transform) in servers)
        {
            if (InRadioRange(sourceMap, sourceZNetwork, transform.MapUid) &&
                power.Powered &&
                keys.Channels.Contains(channelId))
            {
                return true;
            }
        }
        return false;
    }
}
