using System.Globalization;
using System.Linq;
using System.Text;
using Content.Server.Administration.Logs;
using Content.Server.Administration.Managers;
using Content.Server.Chat.Managers;
using Content.Server.GameTicking;
using Content.Server.Players.RateLimiting;
using Content.Server.Language;
using Content.Server.Speech.Components;
using Content.Server.Speech.EntitySystems;
using Content.Server.Station.Components;
using Content.Server.Station.Systems;
using Content.Shared.Administration;
using Content.Shared.ActionBlocker;
using Content.Shared.CCVar;
using Content.Shared.Chat;
using Content.Shared.Database;
using Content.Shared.Ghost;
using Content.Shared.Language;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Language.Systems;
using Content.Shared.Mobs.Systems;
using Content.Shared.Players;
using Content.Shared.Players.RateLimiting;
using Content.Shared.Radio;
using Content.Shared.Holopad;
using Content.Shared.Silicons.StationAi;
using Content.Shared.Speech;
using Content.Shared.Whitelist;
using Robust.Server.Player;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Console;
using Robust.Shared.Containers;
using Robust.Shared.Network;
using Robust.Shared.Physics;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Replays;
using Robust.Shared.Utility;
using Content.Server.Shuttles.Components;
using Content.Server._Misfits.Administration.MysteriousStranger; // #Misfits Add - stranger speech filtering
using Content.Server._Misfits.Supporter; // #Misfits Add - Supporter chat integration
using Content.Shared.Eye; // #Misfits Add - stranger speech filtering
using Content.Shared._Misfits.Special;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Dynamics.Joints;

namespace Content.Server.Chat.Systems;

// Dear contributor. When I was introducing changes to this system only god and I knew what I was doing.
// Now only god knows. Please don't touch this code ever again. If you do have to, increment this counter as a warning for others:
// TOTAL_HOURS_WASTED_HERE_EE = 19

// TODO refactor whatever active warzone this class and chatmanager have become
/// <summary>
///     ChatSystem is responsible for in-simulation chat handling, such as whispering, speaking, emoting, etc.
///     ChatSystem depends on ChatManager to actually send the messages.
/// </summary>
public sealed partial class ChatSystem : SharedChatSystem
{
    [Dependency] private readonly IReplayRecordingManager _replay = default!;
    [Dependency] private readonly IConfigurationManager _configurationManager = default!;
    [Dependency] private readonly IChatManager _chatManager = default!;
    [Dependency] private readonly IChatSanitizationManager _sanitizer = default!;
    [Dependency] private readonly IAdminManager _adminManager = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IAdminLogManager _adminLogger = default!;
    [Dependency] private readonly ActionBlockerSystem _actionBlocker = default!;
    [Dependency] private readonly StationSystem _stationSystem = default!;
    [Dependency] private readonly MobStateSystem _mobStateSystem = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedInteractionSystem _interactionSystem = default!;
    [Dependency] private readonly ReplacementAccentSystem _wordreplacement = default!;
    [Dependency] private readonly LanguageSystem _language = default!;
    [Dependency] private readonly TelepathicChatSystem _telepath = default!;
    [Dependency] private readonly EntityWhitelistSystem _whitelistSystem = default!;
    [Dependency] private readonly SharedSpecialSystem _special = default!;
    [Dependency] private readonly SharedContainerSystem _containers = default!;

    // Forge-Change Moved to shared
    // public const int VoiceRange = 10; // how far voice goes in world units
    // public const int WhisperClearRange = 2; // how far whisper goes while still being understandable, in world units
    // public const int WhisperMuffledRange = 5; // how far whisper goes at all, in world units
    public const string DefaultAnnouncementSound = "/Audio/Announcements/announce.ogg";
    public const float DefaultObfuscationFactor = 0.2f; // Percentage of symbols in a whispered message that can be seen even by "far" listeners
    public readonly Color DefaultSpeakColor = Color.White;

    private bool _loocEnabled = true;
    private bool _deadLoocEnabled;
    private bool _critLoocEnabled;
    private readonly bool _adminLoocEnabled = true;

    public override void Initialize()
    {
        base.Initialize();
        CacheEmotes();
        Subs.CVar(_configurationManager, CCVars.LoocEnabled, OnLoocEnabledChanged, true);
        Subs.CVar(_configurationManager, CCVars.DeadLoocEnabled, OnDeadLoocEnabledChanged, true);
        Subs.CVar(_configurationManager, CCVars.CritLoocEnabled, OnCritLoocEnabledChanged, true);

        SubscribeLocalEvent<GameRunLevelChangedEvent>(OnGameChange);
    }

    private void OnLoocEnabledChanged(bool val)
    {
        if (_loocEnabled == val) return;

        _loocEnabled = val;
        _chatManager.DispatchServerAnnouncement(
            Loc.GetString(val ? "chat-manager-looc-chat-enabled-message" : "chat-manager-looc-chat-disabled-message"));
    }

    private void OnDeadLoocEnabledChanged(bool val)
    {
        if (_deadLoocEnabled == val) return;

        _deadLoocEnabled = val;
        _chatManager.DispatchServerAnnouncement(
            Loc.GetString(val ? "chat-manager-dead-looc-chat-enabled-message" : "chat-manager-dead-looc-chat-disabled-message"));
    }

    private void OnCritLoocEnabledChanged(bool val)
    {
        if (_critLoocEnabled == val)
            return;

        _critLoocEnabled = val;
        _chatManager.DispatchServerAnnouncement(
            Loc.GetString(val ? "chat-manager-crit-looc-chat-enabled-message" : "chat-manager-crit-looc-chat-disabled-message"));
    }

    private void OnGameChange(GameRunLevelChangedEvent ev)
    {
        switch (ev.New)
        {
            case GameRunLevel.InRound:
                if (!_configurationManager.GetCVar(CCVars.OocEnableDuringRound))
                    _configurationManager.SetCVar(CCVars.OocEnabled, false);
                break;
            case GameRunLevel.PostRound:
                if (!_configurationManager.GetCVar(CCVars.OocEnableDuringRound))
                    _configurationManager.SetCVar(CCVars.OocEnabled, true);
                break;
        }
    }

    /// <summary>
    ///     Sends an in-character chat message to relevant clients.
    /// </summary>
    /// <param name="source">The entity that is speaking</param>
    /// <param name="message">The message being spoken or emoted</param>
    /// <param name="desiredType">The chat type</param>
    /// <param name="hideChat">Whether or not this message should appear in the chat window</param>
    /// <param name="hideLog">Whether or not this message should appear in the adminlog window</param>
    /// <param name="shell"></param>
    /// <param name="player">The player doing the speaking</param>
    /// <param name="nameOverride">The name to use for the speaking entity. Usually this should just be modified via <see cref="TransformSpeakerSpeechEvent"/>. If this is set, the event will not get raised.</param>
    public void TrySendInGameICMessage(
        EntityUid source,
        string message,
        InGameICChatType desiredType,
        bool hideChat, bool hideLog = false,
        IConsoleShell? shell = null,
        ICommonSession? player = null, string? nameOverride = null,
        bool checkRadioPrefix = true,
        bool ignoreActionBlocker = false,
        SpeechVerbPrototype? speechVerbOverride = null)
    {
        // #Misfits Fix - allow callers (e.g. radio relays) to preserve the original speaker's speech verb styling.
        TrySendInGameICMessage(source, message, desiredType, hideChat ? ChatTransmitRange.HideChat : ChatTransmitRange.Normal, hideLog, shell, player, nameOverride, checkRadioPrefix, ignoreActionBlocker, speechVerbOverride: speechVerbOverride);
    }

    /// <summary>
    ///     Sends an in-character chat message to relevant clients.
    /// </summary>
    /// <param name="source">The entity that is speaking</param>
    /// <param name="message">The message being spoken or emoted</param>
    /// <param name="desiredType">The chat type</param>
    /// <param name="range">Conceptual range of transmission, if it shows in the chat window, if it shows to far-away ghosts or ghosts at all...</param>
    /// <param name="shell"></param>
    /// <param name="player">The player doing the speaking</param>
    /// <param name="nameOverride">The name to use for the speaking entity. Usually this should just be modified via <see cref="TransformSpeakerSpeechEvent"/>. If this is set, the event will not get raised.</param>
    /// <param name="ignoreActionBlocker">If set to true, action blocker will not be considered for whether an entity can send this message.</param>
    public void TrySendInGameICMessage(
        EntityUid source,
        string message,
        InGameICChatType desiredType,
        ChatTransmitRange range,
        bool hideLog = false,
        IConsoleShell? shell = null,
        ICommonSession? player = null,
        string? nameOverride = null,
        bool checkRadioPrefix = true,
        bool ignoreActionBlocker = false,
        LanguagePrototype? languageOverride = null,
        SpeechVerbPrototype? speechVerbOverride = null
        )
    {
        if (HasComp<GhostComponent>(source))
        {
            // Ghosts can only send dead chat messages, so we'll forward it to InGame OOC.
            TrySendInGameOOCMessage(source, message, InGameOOCChatType.Dead, range == ChatTransmitRange.HideChat, shell, player);
            return;
        }

        if (player != null && _chatManager.HandleRateLimit(player) != RateLimitStatus.Allowed)
            return;

        // Sus
        if (player?.AttachedEntity is { Valid: true } entity && source != entity)
        {
            return;
        }

        if (!CanSendInGame(message, shell, player))
            return;

        ignoreActionBlocker = CheckIgnoreSpeechBlocker(source, ignoreActionBlocker);

        // this method is a disaster
        // every second i have to spend working with this code is fucking agony
        // scientists have to wonder how any of this was merged
        // coding any game admin feature that involves chat code is pure torture
        // changing even 10 lines of code feels like waterboarding myself
        // and i dont feel like vibe checking 50 code paths
        // so we set this here
        // todo free me from chat code
        if (player != null)
        {
            _chatManager.EnsurePlayer(player.UserId).AddEntity(GetNetEntity(source));
        }

        if (desiredType == InGameICChatType.Speak && message.StartsWith(LocalPrefix))
        {
            // prevent radios and remove prefix.
            checkRadioPrefix = false;
            message = message[1..];
        }

        var language = languageOverride ?? _language.GetLanguage(source);

        if (player != null && _sanitizer.TryGetBlockedChatResult(message, ChatSanitizationChannel.InCharacter, out var icModeration))
        {
            var contextLabel = desiredType switch
            {
                InGameICChatType.Emote => "emote",
                InGameICChatType.Telepathic => "telepathic chat",
                InGameICChatType.Whisper => "whisper",
                _ => "IC chat",
            };

            _sanitizer.ReportBlockedChat(player, message, contextLabel);
            SendEntityEmote(source, icModeration.ReplacementText, range, nameOverride, language, ignoreActionBlocker: ignoreActionBlocker, author: player.UserId);
            return;
        }

        bool shouldCapitalize = (desiredType != InGameICChatType.Emote);
        bool shouldPunctuate = _configurationManager.GetCVar(CCVars.ChatPunctuation);
        // Capitalizing the word I only happens in English, so we check language here
        bool shouldCapitalizeTheWordI = (!CultureInfo.CurrentCulture.IsNeutralCulture && CultureInfo.CurrentCulture.Parent.Name == "en")
            || (CultureInfo.CurrentCulture.IsNeutralCulture && CultureInfo.CurrentCulture.Name == "en");

        // Misfits Tweak - Keyboard emotes (smileys) are only permitted in Telepathic channel;
        // Local, Emote, Radio, and Whisper chat should carry the text as-is.
        var allowEmoteStripping = desiredType == InGameICChatType.Telepathic;
        message = SanitizeInGameICMessage(source, message, out var emoteStr, shouldCapitalize, shouldPunctuate, shouldCapitalizeTheWordI, allowEmoteStripping);

        // Misfits Tweak - Detect radio prefix BEFORE routing the emote so acronym/smiley emotes
        // on a radio channel broadcast over radio instead of firing as a local emote.
        RadioChannelPrototype? radioChannel = null;
        string? radioMessage = null;
        var isRadioMessage = checkRadioPrefix
            && TryProccessRadioMessage(source, message, out radioMessage, out radioChannel);

        // Route the emote: over radio if this was a radio message, otherwise fire locally.
        if (player != null && emoteStr != null && emoteStr != message)
        {
            if (isRadioMessage && radioChannel != null)
            {
                // Broadcast emote over the radio channel (e.g. "[Wasteland] Viktoriya laughs over radio.")
                RaiseLocalEvent(source, new EntitySpokeRadioEmoteEvent(emoteStr, radioChannel, language));
            }
            else
            {
                SendEntityEmote(source, emoteStr, range, nameOverride, language, ignoreActionBlocker);
            }
        }

        // This can happen if the entire string is sanitized out.
        if (string.IsNullOrEmpty(message))
            return;

        // This is really terrible. I hate myself for doing this.
        if (language.SpeechOverride.ChatTypeOverride is { } chatTypeOverride)
            desiredType = chatTypeOverride;

        // If a radio prefix was found, send the message body (if any) over the channel.
        if (isRadioMessage)
        {
            if (!string.IsNullOrEmpty(radioMessage))
                SendEntityWhisper(source, radioMessage, range, radioChannel, nameOverride, language, hideLog, ignoreActionBlocker);
            return;
        }

        // Otherwise, send whatever type.
        switch (desiredType)
        {
            case InGameICChatType.Speak:
                SendEntitySpeak(source, message, range, nameOverride, language, hideLog, ignoreActionBlocker, speechVerbOverride);
                break;
            case InGameICChatType.Whisper:
                SendEntityWhisper(source, message, range, null, nameOverride, language, hideLog, ignoreActionBlocker, speechVerbOverride);
                break;
            case InGameICChatType.Emote:
                SendEntityEmote(source, message, range, nameOverride, language, hideLog: hideLog, ignoreActionBlocker: ignoreActionBlocker);
                break;
            //Nyano - Summary: case adds the telepathic chat sending ability.
            case InGameICChatType.Telepathic:
                _telepath.SendTelepathicChat(source, message, range == ChatTransmitRange.HideChat);
                break;
        }
    }

    public void TrySendInGameOOCMessage(
        EntityUid source,
        string message,
        InGameOOCChatType type,
        bool hideChat,
        IConsoleShell? shell = null,
        ICommonSession? player = null
        )
    {
        if (!CanSendInGame(message, shell, player))
            return;

        if (player != null && _chatManager.HandleRateLimit(player) != RateLimitStatus.Allowed)
            return;

        // It doesn't make any sense for a non-player to send in-game OOC messages, whereas non-players may be sending
        // in-game IC messages.
        if (player?.AttachedEntity is not { Valid: true } entity || source != entity)
            return;

        if (_sanitizer.TryGetBlockedChatResult(message, ChatSanitizationChannel.OutOfCharacter, out var oocModeration))
        {
            var contextLabel = type == InGameOOCChatType.Looc ? "LOOC" : "dead chat";
            _sanitizer.ReportBlockedChat(player, message, contextLabel);
            message = oocModeration.ReplacementText;
        }

        message = SanitizeInGameOOCMessage(message);

        var sendType = type;
        // If dead player LOOC is disabled, unless you are an aghost, send dead messages to dead chat
        if (!_adminManager.IsAdmin(player) && !_deadLoocEnabled &&
            (HasComp<GhostComponent>(source) || _mobStateSystem.IsDead(source)))
            sendType = InGameOOCChatType.Dead;

        // If crit player LOOC is disabled, don't send the message at all.
        if (!_critLoocEnabled && _mobStateSystem.IsCritical(source))
            return;

        switch (sendType)
        {
            case InGameOOCChatType.Dead:
                SendDeadChat(source, player, message, hideChat);
                break;
            case InGameOOCChatType.Looc:
                SendLOOC(source, player, message, hideChat);
                break;
        }
    }

    #region Announcements

    /// <summary>
    /// Dispatches an announcement to all.
    /// </summary>
    /// <param name="message">The contents of the message</param>
    /// <param name="sender">The sender (Communications Console in Communications Console Announcement)</param>
    /// <param name="playSound">Play the announcement sound</param>
    /// <param name="colorOverride">Optional color for the announcement message</param>
    public void DispatchGlobalAnnouncement(
        string message,
        string sender = "Central Command",
        bool playSound = true,
        SoundSpecifier? announcementSound = null,
        Color? colorOverride = null
        )
    {
        var wrappedMessage = Loc.GetString("chat-manager-sender-announcement-wrap-message", ("sender", sender), ("message", FormattedMessage.EscapeText(message)));
        _chatManager.ChatMessageToAll(ChatChannel.Radio, message, wrappedMessage, default, false, true, colorOverride);
        if (playSound)
        {
            _audio.PlayGlobal(announcementSound ?? new SoundPathSpecifier(DefaultAnnouncementSound), Filter.Broadcast(), true, AudioParams.Default.WithVolume(-2f));
        }
        _adminLogger.Add(LogType.Chat, LogImpact.Low, $"Global station announcement from {sender}: {message}");
    }

    /// <summary>
    /// Dispatches an announcement on a specific station
    /// </summary>
    /// <param name="source">The entity making the announcement (used to determine the station)</param>
    /// <param name="message">The contents of the message</param>
    /// <param name="sender">The sender (Communications Console in Communications Console Announcement)</param>
    /// <param name="playDefaultSound">Play the announcement sound</param>
    /// <param name="colorOverride">Optional color for the announcement message</param>
    public void DispatchStationAnnouncement(
        EntityUid source,
        string message,
        string sender = "Central Command",
        bool playDefaultSound = true,
        SoundSpecifier? announcementSound = null,
        Color? colorOverride = null)
    {
        var wrappedMessage = Loc.GetString("chat-manager-sender-announcement-wrap-message", ("sender", sender), ("message", FormattedMessage.EscapeText(message)));
        var station = _stationSystem.GetOwningStation(source);

        if (station == null)
        {
            // you can't make a station announcement without a station
            return;
        }

        if (!EntityManager.TryGetComponent<StationDataComponent>(station, out var stationDataComp)) return;

        var filter = _stationSystem.GetInStation(stationDataComp);

        _chatManager.ChatMessageToManyFiltered(filter, ChatChannel.Radio, message, wrappedMessage, source, false, true, colorOverride);

        if (playDefaultSound)
        {
            _audio.PlayGlobal(announcementSound ?? new SoundPathSpecifier(DefaultAnnouncementSound), filter, true, AudioParams.Default.WithVolume(-2f));
        }

        _adminLogger.Add(LogType.Chat, LogImpact.Low, $"Station Announcement on {station} from {sender}: {message}");
    }

    #endregion

    #region Private API

    private void SendEntitySpeak(
        EntityUid source,
        string originalMessage,
        ChatTransmitRange range,
        string? nameOverride,
        LanguagePrototype language,
        bool hideLog = false,
        bool ignoreActionBlocker = false,
        SpeechVerbPrototype? speechVerbOverride = null
        )
    {
        if (!_actionBlocker.CanSpeak(source) && !ignoreActionBlocker)
            return;

        // The original message
        var message = TransformSpeech(source, FormattedMessage.RemoveMarkup(originalMessage), language);

        if (message.Length == 0)
            return;

        var speech = speechVerbOverride ?? GetSpeechVerb(source, message);

        // get the entity's apparent name (if no override provided).
        string name;
        if (nameOverride != null)
        {
            name = nameOverride;
        }
        else
        {
            var nameEv = new TransformSpeakerNameEvent(source, Name(source));
            RaiseLocalEvent(source, nameEv);
            name = nameEv.VoiceName ?? Name(source);
            // Check for a speech verb override
            if (nameEv.SpeechVerb != null && _prototypeManager.TryIndex(nameEv.SpeechVerb, out var proto))
                speech = proto;
        }

        name = FormattedMessage.EscapeText(name);

        // The chat message wrapped in a "x says y" string
        var wrappedMessage = WrapPublicMessage(source, name, message, language: language, speechOverride: speech);
        // The chat message obfuscated via language obfuscation
        var obfuscated = SanitizeInGameICMessage(source, _language.ObfuscateSpeech(message, language), out var emoteStr, true, _configurationManager.GetCVar(CCVars.ChatPunctuation), (!CultureInfo.CurrentCulture.IsNeutralCulture && CultureInfo.CurrentCulture.Parent.Name == "en") || (CultureInfo.CurrentCulture.IsNeutralCulture && CultureInfo.CurrentCulture.Name == "en"));
        // The language-obfuscated message wrapped in a "x says y" string
        var wrappedObfuscated = WrapPublicMessage(source, name, obfuscated, language: language, speechOverride: speech);

        SendInVoiceRange(ChatChannel.Local, name, message, wrappedMessage, obfuscated, wrappedObfuscated, source, range, languageOverride: language);

        var ev = new EntitySpokeEvent(source, message, null, false, language, null);
        RaiseLocalEvent(source, ev, true);

        // To avoid logging any messages sent by entities that are not players, like vendors, cloning, etc.
        // Also doesn't log if hideLog is true.
        if (!HasComp<ActorComponent>(source) || hideLog == true)
            return;

        if (originalMessage == message)
        {
            if (name != Name(source))
                _adminLogger.Add(LogType.Chat, LogImpact.Low, $"Say from {ToPrettyString(source):user} as {name}: {originalMessage}.");
            else
                _adminLogger.Add(LogType.Chat, LogImpact.Low, $"Say from {ToPrettyString(source):user}: {originalMessage}.");
        }
        else
        {
            if (name != Name(source))
                _adminLogger.Add(LogType.Chat, LogImpact.Low,
                    $"Say from {ToPrettyString(source):user} as {name}, original: {originalMessage}, transformed: {message}.");
            else
                _adminLogger.Add(LogType.Chat, LogImpact.Low,
                    $"Say from {ToPrettyString(source):user}, original: {originalMessage}, transformed: {message}.");
        }
    }

    private void SendEntityWhisper(
        EntityUid source,
        string originalMessage,
        ChatTransmitRange range,
        RadioChannelPrototype? channel,
        string? nameOverride,
        LanguagePrototype language,
        bool hideLog = false,
        bool ignoreActionBlocker = false,
        SpeechVerbPrototype? speechVerbOverride = null
        )
    {
        if (!_actionBlocker.CanSpeak(source) && !ignoreActionBlocker)
            return;

        var message = TransformSpeech(source, FormattedMessage.RemoveMarkup(originalMessage), language);
        if (message.Length == 0)
            return;

        // #Misfits Fix - preserve caller-provided speech style (e.g. megaphone over radio relay).
        var speech = speechVerbOverride ?? GetSpeechVerb(source, message);

        // get the entity's name by visual identity (if no override provided).
        string nameIdentity = FormattedMessage.EscapeText(nameOverride ?? Identity.Name(source, EntityManager));
        // get the entity's name by voice (if no override provided).
        string name;
        if (nameOverride != null)
        {
            name = nameOverride;
        }
        else
        {
            var nameEv = new TransformSpeakerNameEvent(source, Name(source));
            RaiseLocalEvent(source, nameEv);
            name = nameEv.VoiceName;

            // #Misfits Fix - if name-transform supplies a speech verb, use it for wrapping.
            if (nameEv.SpeechVerb != null && _prototypeManager.TryIndex(nameEv.SpeechVerb, out var proto))
                speech = proto;
        }
        name = FormattedMessage.EscapeText(name);

        var languageObfuscatedMessage = SanitizeInGameICMessage(source, _language.ObfuscateSpeech(message, language), out var emoteStr, true, _configurationManager.GetCVar(CCVars.ChatPunctuation), (!CultureInfo.CurrentCulture.IsNeutralCulture && CultureInfo.CurrentCulture.Parent.Name == "en") || (CultureInfo.CurrentCulture.IsNeutralCulture && CultureInfo.CurrentCulture.Name == "en"));

        foreach (var (session, data) in GetRecipients(source, Transform(source).GridUid == null ? 0.3f : WhisperMuffledRange))
        {
            if (session.AttachedEntity is not { Valid: true } listener)
                continue;

            if (Transform(session.AttachedEntity.Value).GridUid != Transform(source).GridUid
                && !CheckAttachedGrids(source, session.AttachedEntity.Value))
                continue;

            if (MessageRangeCheck(session, data, range) != MessageRangeCheckResult.Full)
                continue; // Won't get logged to chat, and ghosts are too far away to see the pop-up, so we just won't send it to them.

            var canUnderstandLanguage = _language.CanUnderstand(listener, language.ID);
            // How the entity perceives the message depends on whether it can understand its language
            var perceivedMessage = FormattedMessage.EscapeText(canUnderstandLanguage ? message : languageObfuscatedMessage);

            // Result is the intermediate message derived from the perceived one via obfuscation
            // Wrapped message is the result wrapped in an "x says y" string
            string result, wrappedMessage;
            if (data.Range <= WhisperClearRange)
            {
                // Scenario 1: the listener can clearly understand the message
                result = perceivedMessage;
                wrappedMessage = WrapWhisperMessage(source, "chat-manager-entity-whisper-wrap-message", name, result, language, speechOverride: speech);
            }
            else if (_interactionSystem.InRangeUnobstructed(source, listener, WhisperMuffledRange, Shared.Physics.CollisionGroup.Opaque))
            {
                // Scenario 2: if the listener is too far, they only hear fragments of the message
                result = ObfuscateMessageReadability(perceivedMessage);
                wrappedMessage = WrapWhisperMessage(source, "chat-manager-entity-whisper-wrap-message", nameIdentity, result, language, speechOverride: speech);
            }
            else
            {
                // Scenario 3: If listener is too far and has no line of sight, they can't identify the whisperer's identity
                result = ObfuscateMessageReadability(perceivedMessage);
                wrappedMessage = WrapWhisperMessage(source, "chat-manager-entity-whisper-unknown-wrap-message", string.Empty, result, language, speechOverride: speech);
            }

            _chatManager.ChatMessageToOne(ChatChannel.Whisper, result, wrappedMessage, source, false, session.Channel);
        }

        var replayWrap = WrapWhisperMessage(source, "chat-manager-entity-whisper-wrap-message", name, FormattedMessage.EscapeText(message), language, speechOverride: speech);
        _replay.RecordServerMessage(new ChatMessage(ChatChannel.Whisper, message, replayWrap, GetNetEntity(source), null, MessageRangeHideChatForReplay(range)));

        var ev = new EntitySpokeEvent(source, message, channel, true, language, languageObfuscatedMessage);
        RaiseLocalEvent(source, ev, true);
        if (!hideLog)
            if (originalMessage == message)
            {
                if (name != Name(source))
                    _adminLogger.Add(LogType.Chat, LogImpact.Low, $"Whisper from {ToPrettyString(source):user} as {name}: {originalMessage}.");
                else
                    _adminLogger.Add(LogType.Chat, LogImpact.Low, $"Whisper from {ToPrettyString(source):user}: {originalMessage}.");
            }
            else
            {
                if (name != Name(source))
                    _adminLogger.Add(LogType.Chat, LogImpact.Low,
                    $"Whisper from {ToPrettyString(source):user} as {name}, original: {originalMessage}, transformed: {message}.");
                else
                    _adminLogger.Add(LogType.Chat, LogImpact.Low,
                    $"Whisper from {ToPrettyString(source):user}, original: {originalMessage}, transformed: {message}.");
            }
    }

    private void SendEntityEmote(
        EntityUid source,
        string action,
        ChatTransmitRange range,
        string? nameOverride,
        LanguagePrototype language,
        bool hideLog = false,
        bool checkEmote = true,
        bool ignoreActionBlocker = false,
        NetUserId? author = null
        )
    {
        if (!_actionBlocker.CanEmote(source) && !ignoreActionBlocker)
            return;

        if (TryResolveStationAiEmoteSource(source, out var stationAi, out var relaySource))
        {
            if (HasComp<HolopadUserComponent>(stationAi))
            {
                var ev = new StationAiHolopadEmoteRelayEvent(action, range);
                RaiseLocalEvent(stationAi, ref ev);
                return;
            }

            SendEntityNamelessEmote(relaySource, action, range, hideLog, true, author, VoiceRange);
            return;
        }

        // get the entity's apparent name (if no override provided).
        var ent = Identity.Entity(source, EntityManager);
        string name = FormattedMessage.EscapeText(nameOverride ?? Name(ent));

        // #Misfits Change
        var wrappedMessage = BuildEmoteWrappedMessage(source, name, action);

        if (checkEmote)
            TryEmoteChatInput(source, action);
        SendInVoiceRange(ChatChannel.Emotes, name, action, wrappedMessage, obfuscated: "", obfuscatedWrappedMessage: "", source, range, author);
        if (!hideLog)
            if (name != Name(source))
                _adminLogger.Add(LogType.Chat, LogImpact.Low, $"Emote from {ToPrettyString(source):user} as {name}: {action}");
            else
                _adminLogger.Add(LogType.Chat, LogImpact.Low, $"Emote from {ToPrettyString(source):user}: {action}");
    }

    private bool TryResolveStationAiEmoteSource(EntityUid source, out EntityUid stationAi, out EntityUid relaySource)
    {
        stationAi = source;
        relaySource = source;

        if (HasComp<StationAiHeldComponent>(source))
        {
            relaySource = TryGetStationAiCoreForHeld(source) ?? source;
            return true;
        }

        var query = EntityQueryEnumerator<StationAiCoreComponent>();
        while (query.MoveNext(out var coreUid, out var core))
        {
            if (core.RemoteEntity != source)
                continue;

            if (!_containers.TryGetContainer(coreUid, StationAiCoreComponent.Container, out var container))
                return false;

            foreach (var contained in container.ContainedEntities)
            {
                if (!HasComp<StationAiHeldComponent>(contained))
                    continue;

                stationAi = contained;
                relaySource = coreUid;
                return true;
            }

            return false;
        }

        return false;
    }

    private EntityUid? TryGetStationAiCoreForHeld(EntityUid stationAi)
    {
        var query = EntityQueryEnumerator<StationAiCoreComponent>();
        while (query.MoveNext(out var coreUid, out _))
        {
            if (!_containers.TryGetContainer(coreUid, StationAiCoreComponent.Container, out var container))
                continue;

            if (container.ContainedEntities.Contains(stationAi))
                return coreUid;
        }

        return null;
    }

    // ReSharper disable once InconsistentNaming
    private void SendLOOC(EntityUid source, ICommonSession player, string message, bool hideChat)
    {
        var name = FormattedMessage.EscapeText(Identity.Name(source, EntityManager));

        if (_adminManager.IsAdmin(player))
        {
            if (!_adminLoocEnabled) return;
        }
        else if (!_loocEnabled) return;

        // If crit player LOOC is disabled, don't send the message at all.
        if (!_critLoocEnabled && _mobStateSystem.IsCritical(source))
            return;

        var wrappedMessage = Loc.GetString("chat-manager-entity-looc-wrap-message",
            ("entityName", name),
            ("message", FormattedMessage.EscapeText(message)));

        SendInVoiceRange(ChatChannel.LOOC, name, message, wrappedMessage,
            obfuscated: string.Empty,
            obfuscatedWrappedMessage: string.Empty, // will be skipped anyway
            source,
            hideChat ? ChatTransmitRange.HideChat : ChatTransmitRange.Normal,
            player.UserId,
            languageOverride: LanguageSystem.Universal);
        _adminLogger.Add(LogType.Chat, LogImpact.Low, $"LOOC from {player:Player}: {message}");
    }

    private void SendDeadChat(EntityUid source, ICommonSession player, string message, bool hideChat)
    {
        var clients = GetDeadChatClients();
        var playerName = Name(source);
        string wrappedMessage;
        if (_adminManager.IsAdmin(player))
        {
            wrappedMessage = Loc.GetString("chat-manager-send-admin-dead-chat-wrap-message",
                ("adminChannelName", Loc.GetString("chat-manager-admin-channel-name")),
                ("userName", player.Channel.UserName),
                ("message", FormattedMessage.EscapeText(message)));
            _adminLogger.Add(LogType.Chat, LogImpact.Low, $"Admin dead chat from {player:Player}: {message}");
        }
        else
        {
            wrappedMessage = Loc.GetString("chat-manager-send-dead-chat-wrap-message",
                ("deadChannelName", Loc.GetString("chat-manager-dead-channel-name")),
                ("playerName", (playerName)),
                ("message", FormattedMessage.EscapeText(message)));
            _adminLogger.Add(LogType.Chat, LogImpact.Low, $"Dead chat from {player:Player}: {message}");
        }

        _chatManager.ChatMessageToMany(ChatChannel.Dead, message, wrappedMessage, source, hideChat, true, clients.ToList(), author: player.UserId);
    }
    #endregion

    #region Utility

    private enum MessageRangeCheckResult
    {
        Disallowed,
        HideChat,
        Full
    }

    /// <summary>
    ///     If hideChat should be set as far as replays are concerned.
    /// </summary>
    private bool MessageRangeHideChatForReplay(ChatTransmitRange range)
    {
        return range == ChatTransmitRange.HideChat;
    }

    /// <summary>
    ///     Checks if a target as returned from GetRecipients should receive the message.
    ///     Keep in mind data.Range is -1 for out of range observers.
    /// </summary>
    private MessageRangeCheckResult MessageRangeCheck(ICommonSession session, ICChatRecipientData data, ChatTransmitRange range)
    {
        var initialResult = MessageRangeCheckResult.Full;
        switch (range)
        {
            case ChatTransmitRange.Normal:
                initialResult = MessageRangeCheckResult.Full;
                break;
            case ChatTransmitRange.GhostRangeLimit:
                // N14: Always hide this from ghosts to avoid multiple radios spamming the ghost chat.
                initialResult = data.Observer ? MessageRangeCheckResult.HideChat : MessageRangeCheckResult.Full;
                break;
            case ChatTransmitRange.HideChat:
                initialResult = MessageRangeCheckResult.HideChat;
                break;
            case ChatTransmitRange.NoGhosts:
                initialResult = (data.Observer && !_adminManager.IsAdmin(session)) ? MessageRangeCheckResult.Disallowed : MessageRangeCheckResult.Full;
                break;
        }
        var insistHideChat = data.HideChatOverride ?? false;
        var insistNoHideChat = !(data.HideChatOverride ?? true);
        if (insistHideChat && initialResult == MessageRangeCheckResult.Full)
            return MessageRangeCheckResult.HideChat;
        if (insistNoHideChat && initialResult == MessageRangeCheckResult.HideChat)
            return MessageRangeCheckResult.Full;
        return initialResult;
    }

    /// <summary>
    ///     Sends a chat message to the given players in range of the source entity.
    /// </summary>
    private void SendInVoiceRange(ChatChannel channel, string name, string message, string wrappedMessage, string obfuscated, string obfuscatedWrappedMessage, EntityUid source, ChatTransmitRange range, NetUserId? author = null, LanguagePrototype? languageOverride = null)
    {
        var language = languageOverride ?? _language.GetLanguage(source);
        foreach (var (session, data) in GetRecipients(source, Transform(source).GridUid == null ? 0.3f : VoiceRange))
        {
            if (session.AttachedEntity != null
                && Transform(session.AttachedEntity.Value).GridUid != Transform(source).GridUid
                && !CheckAttachedGrids(source, session.AttachedEntity.Value))
                continue;

            var entRange = MessageRangeCheck(session, data, range);
            if (entRange == MessageRangeCheckResult.Disallowed)
                continue;
            var entHideChat = entRange == MessageRangeCheckResult.HideChat;
            if (session.AttachedEntity is not { Valid: true } playerEntity)
                continue;
            EntityUid listener = session.AttachedEntity.Value;


            // If the channel does not support languages, or the entity can understand the message, send the original message, otherwise send the obfuscated version
            if (channel == ChatChannel.LOOC || channel == ChatChannel.Emotes || _language.CanUnderstand(listener, language.ID))
            {
                _chatManager.ChatMessageToOne(channel, message, wrappedMessage, source, entHideChat, session.Channel, author: author);
            }
            else
            {
                _chatManager.ChatMessageToOne(channel, obfuscated, obfuscatedWrappedMessage, source, entHideChat, session.Channel, author: author);
            }
        }

        _replay.RecordServerMessage(new ChatMessage(channel, message, wrappedMessage, GetNetEntity(source), null, MessageRangeHideChatForReplay(range)));
    }

    /// <summary>
    ///     Returns true if the given player is 'allowed' to send the given message, false otherwise.
    /// </summary>
    private bool CanSendInGame(string message, IConsoleShell? shell = null, ICommonSession? player = null)
    {
        // Non-players don't have to worry about these restrictions.
        if (player == null)
            return true;

        var mindContainerComponent = player.ContentData()?.Mind;

        if (mindContainerComponent == null)
        {
            shell?.WriteError("You don't have a mind!");
            return false;
        }

        if (player.AttachedEntity is not { Valid: true } _)
        {
            shell?.WriteError("You don't have an entity!");
            return false;
        }

        return !_chatManager.MessageCharacterLimit(player, message);
    }

    // ReSharper disable once InconsistentNaming
    // Misfits Tweak - Added allowEmoteStripping param so keyboard emotes can be blocked on specific channels
    private string SanitizeInGameICMessage(EntityUid source, string message, out string? emoteStr, bool capitalize = true, bool punctuate = false, bool capitalizeTheWordI = true, bool allowEmoteStripping = true)
    {
        var newMessage = message.Trim();
        newMessage = SanitizeMessageReplaceWords(newMessage);

        // Misfits Fix - Sanitization must run BEFORE capitalization and punctuation, because
        // SanitizeMessagePeriod appends "." which would break EndsWith checks on letter-ending tokens.
        //
        // Acronyms (lol, rofl, idk, …) fire on ALL spoken channels — no restriction.
        // Symbol smileys (:), o7, …) only fire on Telepathic (allowEmoteStripping gate below).
        _sanitizer.TrySanitizeAcronyms(newMessage, source, out newMessage, out emoteStr);

        // Only strip symbol-based keyboard emotes on the Telepathic channel.
        // If an acronym already matched above, skip the symbol pass to avoid double-emoting.
        if (allowEmoteStripping && emoteStr == null)
            _sanitizer.TrySanitizeOutSmilies(newMessage, source, out newMessage, out emoteStr);

        if (capitalize)
            newMessage = SanitizeMessageCapital(newMessage);
        if (capitalizeTheWordI)
            newMessage = SanitizeMessageCapitalizeTheWordI(newMessage, "i");
        if (punctuate)
            newMessage = SanitizeMessagePeriod(newMessage);

        return newMessage;
    }

    private string SanitizeInGameOOCMessage(string message)
    {
        var newMessage = message.Trim();
        newMessage = FormattedMessage.EscapeText(newMessage);

        return newMessage;
    }

    public string TransformSpeech(EntityUid sender, string message, LanguagePrototype language)
    {
        if (!language.SpeechOverride.RequireSpeech)
            return message; // Do not apply speech accents if there's no speech involved.

        var ev = new TransformSpeechEvent(sender, message);
        RaiseLocalEvent(ev);

        return ev.Message;
    }

    public bool CheckIgnoreSpeechBlocker(EntityUid sender, bool ignoreBlocker)
    {
        if (ignoreBlocker)
            return ignoreBlocker;

        var ev = new CheckIgnoreSpeechBlockerEvent(sender, ignoreBlocker);
        RaiseLocalEvent(sender, ev, true);

        return ev.IgnoreBlocker;
    }

    private IEnumerable<INetChannel> GetDeadChatClients()
    {
        // Only ghosts and full admins (Admin flag) can see dead chat.
        // Mentors with ViewNotes should NOT see dead chat.
        var adminsOnly = _adminManager.ActiveAdmins.Where(p =>
        {
            var adminData = _adminManager.GetAdminData(p);
            return adminData?.HasFlag(AdminFlags.Admin) ?? false;
        });

        return Filter.Empty()
            .AddWhereAttachedEntity(HasComp<GhostComponent>)
            .Recipients
            .Union(adminsOnly)
            .Select(p => p.Channel);
    }

    private string SanitizeMessagePeriod(string message)
    {
        if (string.IsNullOrEmpty(message))
            return message;
        // Adds a period if the last character is a letter.
        if (char.IsLetter(message[^1]))
            message += ".";
        return message;
    }

    [ValidatePrototypeId<ReplacementAccentPrototype>]
    public const string ChatSanitize_Accent = "chatsanitize";

    public string SanitizeMessageReplaceWords(string message)
    {
        if (string.IsNullOrEmpty(message)) return message;

        var msg = message;

        msg = _wordreplacement.ApplyReplacements(msg, ChatSanitize_Accent);

        return msg;
    }

    /// <summary>
    ///     Wraps a message sent by the specified entity into an "x says y" string.
    /// </summary>
    public string WrapPublicMessage(EntityUid source, string name, string message, LanguagePrototype? language = null, SpeechVerbPrototype? speechOverride = null)
    {
        var speech = speechOverride ?? GetSpeechVerb(source, message);
        var wrapId = speech.Bold ? "chat-manager-entity-say-bold-wrap-message" : "chat-manager-entity-say-wrap-message";
        return WrapMessage(wrapId, InGameICChatType.Speak, source, name, message, language, speech);
    }

    /// <summary>
    ///     Wraps a message whispered by the specified entity into an "x whispers y" string.
    /// </summary>
    public string WrapWhisperMessage(EntityUid source, LocId defaultWrap, string entityName, string message, LanguagePrototype? language = null, SpeechVerbPrototype? speechOverride = null)
    {
        return WrapMessage(defaultWrap, InGameICChatType.Whisper, source, entityName, message, language, speechOverride);
    }

    /// <summary>
    ///     Wraps a message sent by the specified entity into the specified wrap string.
    /// </summary>
    public string WrapMessage(LocId wrapId, InGameICChatType chatType, EntityUid source, string entityName, string message, LanguagePrototype? language, SpeechVerbPrototype? speechOverride = null)
    {
        language ??= _language.GetLanguage(source);
        if (language.SpeechOverride.MessageWrapOverrides.TryGetValue(chatType, out var wrapOverride))
            wrapId = wrapOverride;

        var speech = speechOverride ?? GetSpeechVerb(source, message);
        var verbId = language.SpeechOverride.SpeechVerbOverrides is { } verbsOverride
            ? _random.Pick(verbsOverride).ToString()
            : _random.Pick(speech.SpeechVerbStrings);
        var color = DefaultSpeakColor;
        if (language.SpeechOverride.Color is { } colorOverride)
            color = Color.InterpolateBetween(color, colorOverride, colorOverride.A);
        var languageDisplay = language.IsVisibleLanguage
            ? Loc.GetString("chat-manager-language-prefix", ("language", language.ChatName))
            : "";
        var fontSize = _special.GetCharismaChatFontSize(source, language.SpeechOverride.FontSize ?? speech.FontSize);

        return Loc.GetString(wrapId,
            ("color", color),
            ("entityName", entityName),
            ("verb", Loc.GetString(verbId)),
            ("fontType", language.SpeechOverride.FontId ?? speech.FontId),
            ("fontSize", fontSize),
            ("message", message),
            ("language", languageDisplay));
    }


    /// <summary>
    ///     Returns list of players and ranges for all players withing some range. Also returns observers with a range of -1.
    /// </summary>
    private Dictionary<ICommonSession, ICChatRecipientData> GetRecipients(EntityUid source, float voiceGetRange)
    {
        // TODO proper speech occlusion

        var recipients = new Dictionary<ICommonSession, ICChatRecipientData>();
        var ghostHearing = GetEntityQuery<GhostHearingComponent>();
        var xforms = GetEntityQuery<TransformComponent>();

        var transformSource = xforms.GetComponent(source);
        var sourceMapId = transformSource.MapID;
        var sourceCoords = transformSource.Coordinates;

        foreach (var player in _playerManager.Sessions)
        {
            if (player.AttachedEntity is not { Valid: true } playerEntity)
                continue;

            var transformEntity = xforms.GetComponent(playerEntity);

            if (transformEntity.MapID != sourceMapId)
                continue;

            var observer = ghostHearing.HasComponent(playerEntity);

            // even if they are a ghost hearer, in some situations we still need the range
            if (sourceCoords.TryDistance(EntityManager, transformEntity.Coordinates, out var distance) && distance < voiceGetRange)
            {
                recipients.Add(player, new ICChatRecipientData(distance, observer));
                continue;
            }

            if (observer)
                recipients.Add(player, new ICChatRecipientData(-1, true));
        }

        // #Misfits Add - mysterious strangers are only heard by sessions whose eye can actually see them
        // (their target and admin observers). Covers say, whisper and emotes, which all pass through here.
        if (HasComp<MysteriousStrangerComponent>(source))
        {
            foreach (var session in recipients.Keys.ToArray())
            {
                if (session.AttachedEntity is not { } listener
                    || !TryComp<EyeComponent>(listener, out var listenerEye)
                    || (listenerEye.VisibilityMask & (int) VisibilityFlags.MysteriousStranger) == 0)
                    recipients.Remove(session);
            }
        }

        RaiseLocalEvent(new ExpandICChatRecipientsEvent(source, voiceGetRange, recipients));
        return recipients;
    }

    public readonly record struct ICChatRecipientData(float Range, bool Observer, bool? HideChatOverride = null)
    {
    }

    public string ObfuscateMessageReadability(string message, float chance = DefaultObfuscationFactor)
    {
        var modifiedMessage = new StringBuilder(message);

        for (var i = 0; i < message.Length; i++)
        {
            if (char.IsWhiteSpace((modifiedMessage[i])))
            {
                continue;
            }

            if (_random.Prob(1 - chance))
            {
                modifiedMessage[i] = '~';
            }
        }

        return modifiedMessage.ToString();
    }

    public string BuildGibberishString(IReadOnlyList<char> charOptions, int length)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < length; i++)
        {
            sb.Append(_random.Pick(charOptions));
        }
        return sb.ToString();
    }

    private bool CheckAttachedGrids(EntityUid source, EntityUid receiver)
    {
        if (!TryComp<JointComponent>(Transform(source).GridUid, out var sourceJoints)
            || !TryComp<JointComponent>(Transform(receiver).GridUid, out var receiverJoints))
            return false;

        foreach (var (id, _) in sourceJoints.GetJoints)
            if (receiverJoints.GetJoints.ContainsKey(id))
                return true;

        return false;
    }

    #endregion
}

/// <summary>
///     This event is raised before chat messages are sent out to clients. This enables some systems to send the chat
///     messages to otherwise out-of view entities (e.g. for multiple viewports from cameras).
/// </summary>
public record ExpandICChatRecipientsEvent(EntityUid Source, float VoiceRange, Dictionary<ICommonSession, ChatSystem.ICChatRecipientData> Recipients)
{
}

/// <summary>
///     Raised broadcast in order to transform speech.transmit
/// </summary>
public sealed class TransformSpeechEvent : EntityEventArgs
{
    public EntityUid Sender;
    public string Message;

    public TransformSpeechEvent(EntityUid sender, string message)
    {
        Sender = sender;
        Message = message;
    }
}

public sealed class CheckIgnoreSpeechBlockerEvent : EntityEventArgs
{
    public EntityUid Sender;
    public bool IgnoreBlocker;

    public CheckIgnoreSpeechBlockerEvent(EntityUid sender, bool ignoreBlocker)
    {
        Sender = sender;
        IgnoreBlocker = ignoreBlocker;
    }
}

/// <summary>
/// Raised on an entity when an acronym/smiley emote fires while the player was speaking on a radio channel.
/// HeadsetSystem and IntrinsicRadioSystem subscribe to this to broadcast the emote over the channel.
/// </summary>
/// <remarks>Misfits Add</remarks>
public sealed class EntitySpokeRadioEmoteEvent : EntityEventArgs
{
    public readonly string EmoteText;
    /// <summary>Set to null by handlers once consumed, to prevent duplicate broadcasts.</summary>
    public RadioChannelPrototype? Channel;
    public readonly LanguagePrototype Language;

    public EntitySpokeRadioEmoteEvent(string emoteText, RadioChannelPrototype channel, LanguagePrototype language)
    {
        EmoteText = emoteText;
        Channel = channel;
        Language = language;
    }
}

/// <summary>
///     Raised on an entity when it speaks, either through 'say' or 'whisper'.
/// </summary>
public sealed class EntitySpokeEvent : EntityEventArgs
{
    public readonly EntityUid Source;
    public readonly string Message;
    public readonly bool IsWhisper;
    public readonly LanguagePrototype Language;
    public readonly string? ObfuscatedMessage; // not null if this was a whisper
    /// <summary>
    ///     If the entity was trying to speak into a radio, this was the channel they were trying to access. If a radio
    ///     message gets sent on this channel, this should be set to null to prevent duplicate messages.
    /// </summary>
    public RadioChannelPrototype? Channel;

    public EntitySpokeEvent(EntityUid source, string message, RadioChannelPrototype? channel, bool isWhisper, LanguagePrototype language, string? obfuscatedMessage)
    {
        Source = source;
        Message = message;
        Channel = channel;
        IsWhisper = isWhisper;
        Language = language;
        ObfuscatedMessage = obfuscatedMessage;
    }
}
