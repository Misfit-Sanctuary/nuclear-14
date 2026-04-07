using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;
using Content.Client._Misfits.Guidebook;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.Guidebook.Richtext;

[UsedImplicitly]
public sealed class ColorBox : PanelContainer, IDocumentTag
{
    public bool TryParseTag(Dictionary<string, string> args, [NotNullWhen(true)] out Control? control)
    {
        HorizontalExpand = true;
        // #Misfits Tweak - Prevent callout boxes from stretching vertically and apply shared block spacing.
        VerticalExpand = false;
        control = this;

        if (args.TryGetValue("Margin", out var margin))
            Margin = new Thickness(float.Parse(margin));
        else
            Margin = new Thickness(0, 0, 0, GuidebookTheme.ComponentBottomMargin);

        if (args.TryGetValue("HorizontalAlignment", out var halign))
            HorizontalAlignment = Enum.Parse<HAlignment>(halign);
        else
            HorizontalAlignment = HAlignment.Stretch;

        if (args.TryGetValue("VerticalAlignment", out var valign))
            VerticalAlignment = Enum.Parse<VAlignment>(valign);
        else
            VerticalAlignment = VAlignment.Stretch;

        var styleBox =  new StyleBoxFlat();
        if (args.TryGetValue("Color", out var color))
            styleBox.BackgroundColor = Color.FromHex(color);
        else
            styleBox.BackgroundColor = GuidebookTheme.ExampleBackground;

        if (args.TryGetValue("OutlineThickness", out var outlineThickness))
            styleBox.BorderThickness = new Thickness(float.Parse(outlineThickness));
        else
            styleBox.BorderThickness = new Thickness(1);

        if (args.TryGetValue("OutlineColor", out var outlineColor))
            styleBox.BorderColor = Color.FromHex(outlineColor);
        else
            styleBox.BorderColor = GuidebookTheme.ExampleBorder;

        PanelOverride = styleBox;

        return true;
    }
}
