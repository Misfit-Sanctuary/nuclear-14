using System.Diagnostics.CodeAnalysis;
using Content.Client._Misfits.Guidebook;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.Guidebook.Richtext;

public sealed class Box : BoxContainer, IDocumentTag
{
    public bool TryParseTag(Dictionary<string, string> args, [NotNullWhen(true)] out Control? control)
    {
        // #Misfits Tweak - Default guidebook boxes to full-width layout so page structure is consistent.
        HorizontalExpand = true;
        control = this;

        if (args.TryGetValue("Margin", out var margin))
            Margin = new Thickness(float.Parse(margin));

        if (args.TryGetValue("Spacing", out var spacing) && int.TryParse(spacing, out var spacingValue))
            SeparationOverride = spacingValue;
        else
            SeparationOverride = 0;

        if (args.TryGetValue("Orientation", out var orientation))
            Orientation = Enum.Parse<LayoutOrientation>(orientation);
        else
            Orientation = LayoutOrientation.Horizontal;

        if (args.TryGetValue("HorizontalAlignment", out var halign))
            HorizontalAlignment = Enum.Parse<HAlignment>(halign);
        else
            HorizontalAlignment = HAlignment.Stretch;

        if (args.TryGetValue("VerticalAlignment", out var valign))
            VerticalAlignment = Enum.Parse<VAlignment>(valign);

        return true;
    }
}
