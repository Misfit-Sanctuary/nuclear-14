using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Content.Client._Misfits.Guidebook;
using Content.Client.Guidebook.Richtext;

// ReSharper disable InconsistentNaming
namespace Content.Client._Misfits.Guidebook.Richtext;

// #Misfits Add - HTML <br/> tag handler for guidebook parser (line break)

/// <summary>
/// Guidebook document tag: <br/>
/// Inserts a vertical spacer to create a line break between content.
/// Optional attribute: Height (float, default 10).
/// Usage: <br/> or <br Height="20"/>
/// </summary>
[UsedImplicitly]
public sealed class br : Control, IDocumentTag
{
    public bool TryParseTag(Dictionary<string, string> args, [NotNullWhen(true)] out Control? control)
    {
        // Default height for a single line break spacer
        float height = GuidebookTheme.BreakHeight;

        if (args.TryGetValue("Height", out var h) && float.TryParse(h, out var parsed))
            height = parsed;

        MinHeight = height;
        HorizontalExpand = true;

        control = this;
        return true;
    }
}
