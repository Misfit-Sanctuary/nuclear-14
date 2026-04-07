using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Content.Client._Misfits.Guidebook;
using Content.Client.Guidebook.Richtext;

// ReSharper disable InconsistentNaming
namespace Content.Client._Misfits.Guidebook.Richtext;

// #Misfits Add - HTML <div> tag handler for guidebook parser (generic block container)

/// <summary>
/// Guidebook document tag: <div>children</div>
/// A generic vertical block container. Children are stacked vertically.
/// Optional attributes: Margin (float), Orientation ("Horizontal"/"Vertical", default Vertical).
/// Usage: <div>content here</div> or <div Margin="10">padded content</div>
/// </summary>
[UsedImplicitly]
public sealed class div : BoxContainer, IDocumentTag
{
    public bool TryParseTag(Dictionary<string, string> args, [NotNullWhen(true)] out Control? control)
    {
        HorizontalExpand = true;
        SeparationOverride = 0;

        // Default to vertical stacking (block layout)
        if (args.TryGetValue("Orientation", out var orientation))
            Orientation = Enum.Parse<LayoutOrientation>(orientation);
        else
            Orientation = LayoutOrientation.Vertical;

        if (args.TryGetValue("Margin", out var margin) && float.TryParse(margin, out var m))
            Margin = new Thickness(m);
        else
            Margin = new Thickness(0, 0, 0, GuidebookTheme.ComponentBottomMargin);

        control = this;
        return true;
    }
}
