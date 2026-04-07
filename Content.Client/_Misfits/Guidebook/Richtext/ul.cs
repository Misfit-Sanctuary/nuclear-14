using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Content.Client._Misfits.Guidebook;
using Content.Client.Guidebook.Richtext;

// ReSharper disable InconsistentNaming
namespace Content.Client._Misfits.Guidebook.Richtext;

// #Misfits Add - HTML <ul> tag handler for guidebook parser (unordered list container)

/// <summary>
/// Guidebook document tag: <ul>children</ul>
/// A vertical container for list items. Use with <li> children for bullet lists.
/// Optional attribute: Margin (float).
/// Usage: <ul><li>Item 1</li><li>Item 2</li></ul>
/// </summary>
[UsedImplicitly]
public sealed class ul : BoxContainer, IDocumentTag
{
    public bool TryParseTag(Dictionary<string, string> args, [NotNullWhen(true)] out Control? control)
    {
        HorizontalExpand = true;
        Orientation = LayoutOrientation.Vertical;
        SeparationOverride = 0;

        if (args.TryGetValue("Margin", out var margin) && float.TryParse(margin, out var m))
            Margin = new Thickness(m);
        else
            Margin = new Thickness(GuidebookTheme.ListIndent, 0, 0, GuidebookTheme.ParagraphBottomMargin);

        control = this;
        return true;
    }
}
