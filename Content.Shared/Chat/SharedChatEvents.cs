using Content.Shared.Speech;
using Robust.Shared.Prototypes;
using Content.Shared.Inventory;
using Robust.Shared.Utility; // #Cythisiax Added - rank prefix coloring

namespace Content.Shared.Chat;

/// <summary>
///     This event should be sent everytime an entity talks (Radio, local chat, etc...).
///     The event is sent to both the entity itself, and all clothing (For stuff like voice masks).
/// </summary>
public sealed class TransformSpeakerNameEvent : EntityEventArgs, IInventoryRelayEvent
{
    public SlotFlags TargetSlots { get; } = SlotFlags.WITHOUT_POCKET;
    public EntityUid Sender;
    public string VoiceName;
    public ProtoId<SpeechVerbPrototype>? SpeechVerb;

    // #Cythisiax Added - faction rank prefix already in VoiceName ("1SG John Doe"), kept here so chat
    // can color it in the faction's color once the name has been escaped.
    public string? RankPrefix;
    public Color? RankColor;

    // #Cythisiax Added - wraps the rank prefix of an already-escaped name in its faction color
    public string ColorRankPrefix(string escapedName)
    {
        if (RankPrefix == null || RankColor is not { } color)
            return escapedName;

        var prefix = FormattedMessage.EscapeText(RankPrefix);
        if (!escapedName.StartsWith(prefix + " "))
            return escapedName;

        return $"[color={color.ToHex()}]{prefix}[/color]{escapedName[prefix.Length..]}";
    }

    public TransformSpeakerNameEvent(EntityUid sender, string name)
    {
        Sender = sender;
        VoiceName = name;
        SpeechVerb = null;
    }
}
