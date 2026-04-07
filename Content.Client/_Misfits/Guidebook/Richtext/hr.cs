using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Content.Client._Misfits.Guidebook;
using Content.Client.Guidebook.Richtext;

// ReSharper disable InconsistentNaming
namespace Content.Client._Misfits.Guidebook.Richtext;

// #Misfits Add - HTML <hr/> tag handler for guidebook parser (horizontal rule)

/// <summary>
/// Guidebook document tag: <hr/>
/// Renders a thin horizontal separator line.
/// Optional attributes: Color (hex, default #444444), Thickness (float, default 1).
/// Usage: <hr/> or <hr Color="#888888" Thickness="2"/>
/// </summary>
[UsedImplicitly]
public sealed class hr : PanelContainer, IDocumentTag
{
    public bool TryParseTag(Dictionary<string, string> args, [NotNullWhen(true)] out Control? control)
    {
        HorizontalExpand = true;
        MinHeight = GuidebookTheme.DividerThickness;
        Margin = new Thickness(0, GuidebookTheme.DividerTopMargin, 0, GuidebookTheme.DividerBottomMargin);

        var styleBox = new StyleBoxFlat();

        // Optional color attribute, default to a muted gray
        if (args.TryGetValue("Color", out var color))
            styleBox.BackgroundColor = Color.FromHex(color);
        else
            styleBox.BackgroundColor = GuidebookTheme.DividerColor;

        // Optional thickness attribute
        if (args.TryGetValue("Thickness", out var thickness) && float.TryParse(thickness, out var t))
            MinHeight = t;

        PanelOverride = styleBox;

        control = this;
        return true;
    }
}
