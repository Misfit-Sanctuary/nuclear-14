// #Misfits Add - Static name sanitizer to block slurs/offensive terms in character names
// Mirrors the blocked term + leetspeak detection logic from ChatSanitizationManager (server-side)
// so it can be used in Content.Shared for profile validation.
using System.Text;
using System.Text.RegularExpressions;

namespace Content.Shared._Misfits.Chat;

/// <summary>
/// Provides static helpers to detect blocked/offensive terms in character names.
/// Uses the same leetspeak-aware regex approach as the server-side ChatSanitizationManager.
/// </summary>
public static class NameSanitizer
{
    // Removed "gay" and "homosexual" from blocked terms; these are not slurs
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

    /// <summary>
    /// Returns true if the given name contains any blocked/offensive term.
    /// </summary>
    public static bool ContainsBlockedTerm(string name)
    {
        foreach (var regex in BlockedTermRegexes)
        {
            if (regex.IsMatch(name))
                return true;
        }

        return false;
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
