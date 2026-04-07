using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Content.Client._Misfits.Guidebook;
using Content.Client.Guidebook.Richtext;

// ReSharper disable InconsistentNaming
namespace Content.Client._Misfits.Guidebook.Richtext;

// #Misfits Add - HTML <p> tag handler for guidebook parser (paragraph block)

/// <summary>
/// Guidebook document tag: <p>children</p>
/// A paragraph container with bottom margin for spacing between blocks.
/// Optional attribute: Margin (float, default 15).
/// Usage: <p>Paragraph text here.</p>
/// </summary>
[UsedImplicitly]
public sealed class p : BoxContainer, IDocumentTag
{
    public bool TryParseTag(Dictionary<string, string> args, [NotNullWhen(true)] out Control? control)
    {
        HorizontalExpand = true;
        Orientation = LayoutOrientation.Vertical;
        SeparationOverride = 0;

        // Default paragraph bottom margin for spacing
        if (args.TryGetValue("Margin", out var margin) && float.TryParse(margin, out var m))
            Margin = new Thickness(m);
        else
            Margin = new Thickness(0, 0, 0, GuidebookTheme.ParagraphBottomMargin);

        control = this;
        return true;
    }
}
