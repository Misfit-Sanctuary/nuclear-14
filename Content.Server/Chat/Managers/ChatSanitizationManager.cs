// Misfits Add - Extended SmileyToEmote dictionary with new keyboard emote entries (O_o, >_<, etc.)
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Content.Server.Administration.Notes;
using Content.Shared.Administration.Notes;
using Content.Shared.CCVar;
using Content.Shared.Database;
using Robust.Shared.Configuration;
using Robust.Shared.Player;

namespace Content.Server.Chat.Managers;

public sealed class ChatSanitizationManager : IChatSanitizationManager
{
    [Dependency] private readonly IConfigurationManager _configurationManager = default!;
    [Dependency] private readonly IAdminNotesManager _adminNotes = default!;
    [Dependency] private readonly IChatManager _chatManager = default!;

    private const string HiFallback = "Hi.";
    private const string UnintelligibleAction = "attempted to say something unintelligible.";

    // #Misfits Tweak - Removed "gay" and "homosexual" from blocked terms; these are not slurs
    private static readonly string[] BlockedTerms =
    [
        "lesbian",
        "bisexual",
        "queer",
        "trans",
        "transgender",
        "nonbinary",
        "non-binary",
        "pansexual",
        "asexual",
        "intersex",
        "homo",
        "dyke",
        "fag",
        "faggot",
        "tranny",
        "nigger",
        "nigga",
        "kike",
        "spic",
        "chink",
        "gook",
        "wetback",
    ];

    private static readonly Regex[] BlockedTermRegexes = BuildBlockedTermRegexes();

    private static readonly Dictionary<string, string> SmileyToEmote = new()
    {
        // I could've done this with regex, but felt it wasn't the right idea.
        { ":)", "chatsan-smiles" },
        { ":]", "chatsan-smiles" },
        { "=)", "chatsan-smiles" },
        { "=]", "chatsan-smiles" },
        { "(:", "chatsan-smiles" },
        { "[:", "chatsan-smiles" },
        { "(=", "chatsan-smiles" },
        { "[=", "chatsan-smiles" },
        { "^^", "chatsan-smiles" },
        { "^-^", "chatsan-smiles" },
        { ":(", "chatsan-frowns" },
        { ":[", "chatsan-frowns" },
        { "=(", "chatsan-frowns" },
        { "=[", "chatsan-frowns" },
        { "):", "chatsan-frowns" },
        { ")=", "chatsan-frowns" },
        { "]:", "chatsan-frowns" },
        { "]=", "chatsan-frowns" },
        { ":D", "chatsan-smiles-widely" },
        { "D:", "chatsan-frowns-deeply" },
        { ":O", "chatsan-surprised" },
        { ":3", "chatsan-smiles" }, //nope
        { ":S", "chatsan-uncertain" },
        { ":>", "chatsan-grins" },
        { ":<", "chatsan-pouts" },
        { "xD", "chatsan-laughs" },
        { ":'(", "chatsan-cries" },
        { ":'[", "chatsan-cries" },
        { "='(", "chatsan-cries" },
        { "='[", "chatsan-cries" },
        { ")':", "chatsan-cries" },
        { "]':", "chatsan-cries" },
        { ")'=", "chatsan-cries" },
        { "]'=", "chatsan-cries" },
        { ";-;", "chatsan-cries" },
        { ";_;", "chatsan-cries" },
        { ":u", "chatsan-smiles-smugly" },
        { ":v", "chatsan-smiles-smugly" },
        { ">:i", "chatsan-annoyed" },
        { ":i", "chatsan-sighs" },
        { ":|", "chatsan-sighs" },
        { ":p", "chatsan-stick-out-tongue" },
        { ";p", "chatsan-stick-out-tongue" },
        { ":b", "chatsan-stick-out-tongue" },
        { "0-0", "chatsan-wide-eyed" },
        { "o-o", "chatsan-wide-eyed" },
        { "o.o", "chatsan-wide-eyed" },
        { "._.", "chatsan-surprised" },
        { ".-.", "chatsan-mumbles" },
        { "-_-", "chatsan-unimpressed" },
        { "o/", "chatsan-waves" },
        { "^^/", "chatsan-waves" },
        { ":/", "chatsan-uncertain" },
        { ":\\", "chatsan-uncertain" },
        { "o7", "chatsan-salutes" },
        { ";_;7", "chatsan-tearfully-salutes"},
        { ";)", "chatsan-winks" },
        { ";]", "chatsan-winks" },
        { "(;", "chatsan-winks" },
        { "[;", "chatsan-winks" },
        { ":')", "chatsan-tearfully-smiles" },
        { ":']", "chatsan-tearfully-smiles" },
        { "=')", "chatsan-tearfully-smiles" },
        { "=']", "chatsan-tearfully-smiles" },
        { "(':", "chatsan-tearfully-smiles" },
        { "[':", "chatsan-tearfully-smiles" },
        { "('=", "chatsan-tearfully-smiles" },
        { "['=", "chatsan-tearfully-smiles" },
        // Corvax-Localization-Start
        { "0_o", "chatsan-wide-eyed" },
        { ":0", "chatsan-surprised" },
        { "T_T", "chatsan-cries" },
        { "=_(", "chatsan-cries" },
        // Corvax-Localization-End

        // Misfits Add - Extended keyboard emote list (symbol-only emoticons)
        // Boggle / wide-eyed confusion variants
        { "O_o", "chatsan-boggles" },
        { "o_O", "chatsan-boggles" },
        { "O_O", "chatsan-boggles" },
        { "o_o", "chatsan-boggles" },
        // Frustration / grimace
        { ">_<", "chatsan-frustrated" },
        { ">.<", "chatsan-frustrated" },
        // Shifty / side-eye
        { ">_>", "chatsan-shifty" },
        { "<_<", "chatsan-shifty" },
        { ">.>", "chatsan-shifty" },
        { "<.<", "chatsan-shifty" },
        // Happy beam
        { "^_^", "chatsan-beams" },
        { "^.^", "chatsan-beams" },
        { "n_n", "chatsan-beams" },
        // Wicked / mischievous grin
        { ">:)", "chatsan-grins-wickedly" },
        { ">:D", "chatsan-grins-wickedly" },
        { ">;)", "chatsan-grins-wickedly" },
        // Scowl / angry frown
        { ">:(", "chatsan-scowls" },
        { ">:[", "chatsan-scowls" },
        // Dejected / resigned
        { "u_u", "chatsan-dejected" },
        { "v_v", "chatsan-dejected" },
        // Starstruck / amazed
        { "*_*", "chatsan-starstruck" },
        { "★_★", "chatsan-starstruck" },
        // Dizzy / disoriented
        { "@_@", "chatsan-dizzy" },
        // Dazed / knocked out
        { "x_x", "chatsan-dazed" },
        { "X_X", "chatsan-dazed" },
        { "x.x", "chatsan-dazed" },
        // Zipping lips
        { ":x", "chatsan-zips-lips" },
        { ":X", "chatsan-zips-lips" },
        { ":-x", "chatsan-zips-lips" },
        { ":-X", "chatsan-zips-lips" },
        // Blowing a kiss
        { ":*", "chatsan-blows-kiss" },
        { ":-*", "chatsan-blows-kiss" },
        { ";*", "chatsan-blows-kiss" },
        // Cheering / throwing arms up
        { "\\o/", "chatsan-cheers" },
        // Nervous sweat
        { "^^;", "chatsan-nervous" },
        { "^_^;", "chatsan-nervous" },
        { "^^;;", "chatsan-nervous" },
        // Facepalm
        { ">_<;", "chatsan-facepalms" },
        // End Misfits Add
    };

    // Misfits Add - Text acronyms that fire emote actions on ALL spoken channels (not just Telepathic).
    // Kept separate from SmileyToEmote so they can be applied unconditionally via TrySanitizeAcronyms.
    // Ordered longest→shortest within each group to prevent prefix swallowing (e.g. lmfao before lmao).
    private static readonly Dictionary<string, string> AcronymToEmote = new()
    {
        // Laughter
        { "lmfao", "chatsan-laughs-heartily" }, // laughing my f***ing ass off
        { "rofl",  "chatsan-rolls-laughing" },   // rolling on the floor laughing
        { "lmao",  "chatsan-laughs-heartily" },  // laughing my ass off
        { "heehee", "chatsan-chuckles" },        // quiet giggle
        { "hehe",  "chatsan-chuckles" },         // quiet laugh
        { "heh",   "chatsan-chuckles" },         // understated chuckle
        { "kek",   "chatsan-laughs" },           // kek (lol variant)
        { "lel",   "chatsan-laughs" },           // lel (lol variant)
        { "lol",   "chatsan-laughs" },           // laugh out loud
        // Expressive / reaction
        { "owo", "chatsan-looks-flustered" },    // flustered/cute reaction
        { "uwu", "chatsan-looks-flustered" },    // flustered/cute reaction
        // Confusion / uncertainty
        { "omfg", "chatsan-surprised" },         // oh my f***ing god
        { "omg",  "chatsan-surprised" },         // oh my god
        { "wtf",  "chatsan-mumbles" },          // what the f***
        { "tbh",  "chatsan-shrugs" },            // to be honest
        { "idk",  "chatsan-shrugs" },            // I don't know
        { "?",    "chatsan-mumbles" },           // Misfits Add - lone question mark; sounds more natural over radio than "looks confused"
        // Physical gestures
        { "smh",  "chatsan-shakes-head" },       // shaking my head
        { "fml",  "chatsan-facepalms" },         // f*** my life
        { "ngl",  "chatsan-shifty" },            // not gonna lie
        // Social
        { "tysm", "chatsan-beams" },             // thank you so much
        { "thx",  "chatsan-nods" },              // thanks
        { "brb",  "chatsan-waves" },             // be right back
        { "afk",  "chatsan-waves" },             // away from keyboard
        { "np",   "chatsan-nods" },              // no problem
        // Gestures / salutes
        { "o7",   "chatsan-salutes" },           // Misfits Add - military salute; fires on all channels
        { ";_;7", "chatsan-tearfully-salutes" }, // Misfits Add - tearful salute; fires on all channels
    };
    // End Misfits Add (AcronymToEmote)

    private bool _doSanitize;

    // Anti-Goida
    private static readonly Regex GoydaRegex = new(@"[Gg][Oo]+[Yy][Dd][Aa]", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public void Initialize()
    {
        _configurationManager.OnValueChanged(CCVars.ChatSanitizerEnabled, x => _doSanitize = x, true);
    }

    public bool TryGetBlockedChatResult(string input, ChatSanitizationChannel channel, [NotNullWhen(true)] out BlockedChatMessageResult? result)
    {
        result = null;

        if (!_doSanitize || string.IsNullOrWhiteSpace(input))
            return false;

        foreach (var regex in BlockedTermRegexes)
        {
            if (!regex.IsMatch(input))
                continue;

            result = channel == ChatSanitizationChannel.OutOfCharacter
                ? new BlockedChatMessageResult(HiFallback, false)
                : new BlockedChatMessageResult(UnintelligibleAction, true);
            return true;
        }

        return false;
    }

    public void ReportBlockedChat(ICommonSession player, string rawMessage, string contextLabel)
    {
        var trimmed = rawMessage.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return;

        _chatManager.SendAdminAlert($"Auto-moderation blocked {player.Name} ({player.UserId}) in {contextLabel}: \"{trimmed}\"");
        CreateModerationNote(player, trimmed, contextLabel);
    }

    public bool TrySanitizeOutSmilies(string input, EntityUid speaker, out string sanitized, [NotNullWhen(true)] out string? emote)
    {
        if (!_doSanitize)
        {
            sanitized = input;
            emote = null;
            return false;
        }

        input = input.TrimEnd();

        // Apply Anti-Goida filter
        input = GoydaRegex.Replace(input, "I am an idiot");

        foreach (var (smiley, replacement) in SmileyToEmote)
        {
            if (input.EndsWith(smiley, true, CultureInfo.InvariantCulture))
            {
                sanitized = input.Remove(input.Length - smiley.Length).TrimEnd();
                emote = Loc.GetString(replacement, ("ent", speaker));
                return true;
            }
        }

        sanitized = input;
        emote = null;
        return false;
    }

    // Misfits Add - Separate acronym check so it fires on ALL spoken channels, not just Telepathic.
    // Called unconditionally from SanitizeInGameICMessage before capitalize/punctuate passes.
    public bool TrySanitizeAcronyms(string input, EntityUid speaker, out string sanitized, [NotNullWhen(true)] out string? emote)
    {
        if (!_doSanitize)
        {
            sanitized = input;
            emote = null;
            return false;
        }

        input = input.TrimEnd();

        foreach (var (acronym, replacement) in AcronymToEmote)
        {
            if (!input.EndsWith(acronym, true, CultureInfo.InvariantCulture))
                continue;

            // Misfits Fix - ensure the acronym is a standalone token, not the tail of a real sentence.
            // Trim trailing whitespace from the portion before the match so that "What ?" and "What?"
            // are both rejected (the real last character before the token would be 't', a letter).
            // A bare "?" or "idk" alone has an empty before-string and always matches.
            var matchStart = input.Length - acronym.Length;
            if (matchStart > 0)
            {
                var beforeTrimmed = input[..matchStart].TrimEnd();
                if (beforeTrimmed.Length > 0 && char.IsLetterOrDigit(beforeTrimmed[^1]))
                    continue;
            }

            sanitized = input.Remove(matchStart).TrimEnd();
            emote = Loc.GetString(replacement, ("ent", speaker));
            return true;
        }

        sanitized = input;
        emote = null;
        return false;
    }
    // End Misfits Add (TrySanitizeAcronyms)

    private async void CreateModerationNote(ICommonSession player, string rawMessage, string contextLabel)
    {
        var noteMessage = $"Automatic chat moderation blocked a message in {contextLabel}. Raw attempted message: \"{rawMessage}\"";
        await _adminNotes.AddSystemRemark(player.UserId, NoteType.Note, noteMessage, NoteSeverity.Medium, true, null);
    }

    private static Regex[] BuildBlockedTermRegexes()
    {
        var regexes = new Regex[BlockedTerms.Length];

        for (var i = 0; i < BlockedTerms.Length; i++)
        {
            regexes[i] = BuildBlockedTermRegex(BlockedTerms[i]);
        }

        return regexes;
    }

    private static Regex BuildBlockedTermRegex(string term)
    {
        var pattern = new StringBuilder(@"(?<![\p{L}\p{N}])");
        var appendSeparator = false;

        foreach (var character in term)
        {
            if (character is ' ' or '-' or '_')
            {
                pattern.Append(@"[\W_]*");
                appendSeparator = false;
                continue;
            }

            if (appendSeparator)
                pattern.Append(@"[\W_]*");

            pattern.Append(GetProtectedCharacterPattern(character));
            appendSeparator = true;
        }

        pattern.Append(@"(?![\p{L}\p{N}])");
        return new Regex(pattern.ToString(), RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string GetProtectedCharacterPattern(char character)
    {
        return char.ToLowerInvariant(character) switch
        {
            'a' => "[a4@]",
            'b' => "[b8]",
            'e' => "[e3]",
            'g' => "[g69]",
            'i' => "[i1!|]",
            'l' => "[l1|]",
            'o' => "[o0]",
            's' => "[s5$]",
            't' => "[t7+]",
            'z' => "[z2]",
            _ => Regex.Escape(character.ToString()),
        };
    }
}
