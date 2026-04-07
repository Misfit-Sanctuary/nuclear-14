// #Misfits Add - Parses admin log messages into typed segments so the UI can
// color-code player names and entity references separately from action text,
// while keeping the full raw data available for tooltips/detail expansion.
//
// Server-side log messages follow the pattern:
//   "DisplayName (userId/netId, PrototypeId, endpoint@Username) did something to TargetName (id/net, Proto, ep@User)"
// This parser splits those into PlayerName, EntityRef (the parenthesized block), and plain Text segments.

using System.Text.RegularExpressions;

namespace Content.Client._Misfits.Administration.UI.Logs;

/// <summary>
/// What kind of segment a piece of parsed log message represents.
/// </summary>
public enum SegmentKind
{
    /// <summary>Plain action/verb text.</summary>
    Text,
    /// <summary>A player/entity display name (the part before the parenthesized ID block).</summary>
    PlayerName,
    /// <summary>A parenthesized entity reference block like "(310653/n310653, MobHuman, localhost@Cythisia)".</summary>
    EntityRef,
}

/// <summary>
/// A single segment of a parsed log message.
/// </summary>
public readonly record struct MessageSegment(string Text, SegmentKind Kind);

public static class LogMessageParser
{
    // Matches: "DisplayName (stuff/stuff, stuff, stuff@stuff)"
    // Group 1 = display name (non-greedy, at least 1 char)
    // Group 2 = full parenthesized block including parens
    // The parenthesized block contains: userId/netId, PrototypeId, endpoint@Username
    // Some entities have nested parens or simpler formats, so we match balanced content.
    private static readonly Regex EntityPattern = new(
        @"(\S+(?:\s+\S+)*?)\s*(\([^()]*(?:/[^()]*)?(?:,\s*[^()]*)*(?:@[^()]*?)?\))",
        RegexOptions.Compiled);

    /// <summary>
    /// Parses a log message into a list of typed segments for color-coded rendering.
    /// Player/entity names get <see cref="SegmentKind.PlayerName"/>,
    /// parenthesized ID blocks get <see cref="SegmentKind.EntityRef"/>,
    /// and everything else is <see cref="SegmentKind.Text"/>.
    /// </summary>
    public static List<MessageSegment> Parse(string message)
    {
        var segments = new List<MessageSegment>();
        var lastIndex = 0;

        foreach (Match match in EntityPattern.Matches(message))
        {
            // Text before this match
            if (match.Index > lastIndex)
            {
                var before = message[lastIndex..match.Index];
                if (!string.IsNullOrEmpty(before))
                    segments.Add(new MessageSegment(before, SegmentKind.Text));
            }

            // Display name portion
            var displayName = match.Groups[1].Value;
            segments.Add(new MessageSegment(displayName, SegmentKind.PlayerName));

            // Parenthesized entity reference
            var entityRef = match.Groups[2].Value;
            segments.Add(new MessageSegment(entityRef, SegmentKind.EntityRef));

            lastIndex = match.Index + match.Length;
        }

        // Trailing text after last match
        if (lastIndex < message.Length)
        {
            var trailing = message[lastIndex..];
            if (!string.IsNullOrEmpty(trailing))
                segments.Add(new MessageSegment(trailing, SegmentKind.Text));
        }

        // If nothing matched, return the whole message as plain text
        if (segments.Count == 0)
            segments.Add(new MessageSegment(message, SegmentKind.Text));

        return segments;
    }

    /// <summary>
    /// Returns a display-friendly version of the message with entity reference blocks stripped.
    /// Used for the compact default view — full data is preserved on the row for tooltips.
    /// </summary>
    public static string ToCompactDisplay(List<MessageSegment> segments)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var seg in segments)
        {
            // Skip entity ref blocks in compact mode — just show names and action text
            if (seg.Kind == SegmentKind.EntityRef)
                continue;

            sb.Append(seg.Text);
        }

        return sb.ToString().Trim();
    }
}
