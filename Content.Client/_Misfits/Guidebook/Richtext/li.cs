using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Content.Client._Misfits.Guidebook;
using Content.Client.Guidebook.Richtext;

// ReSharper disable InconsistentNaming
namespace Content.Client._Misfits.Guidebook.Richtext;

// #Misfits Add - HTML <li> tag handler for guidebook parser (list item with bullet)

/// <summary>
/// Guidebook document tag: <li>content</li>
/// A list item with a bullet prefix ( › ). Children are placed after the bullet.
/// Optional attribute: Bullet (string, default "  › ").
/// Usage: <li>First item</li> or <li Bullet="• ">Custom bullet</li>
/// </summary>
[UsedImplicitly]
public sealed class li : BoxContainer, IDocumentTag
{
    /// <summary>
    /// Default bullet prefix matching the existing guidebook list style.
    /// </summary>
    private const string DefaultBullet = GuidebookTheme.DefaultListBullet + " ";

    public bool TryParseTag(Dictionary<string, string> args, [NotNullWhen(true)] out Control? control)
    {
        HorizontalExpand = true;
        Orientation = LayoutOrientation.Horizontal;
        Margin = new Thickness(0, 0, 0, GuidebookTheme.ListItemBottomMargin);

        // Bullet prefix label
        var bulletText = args.TryGetValue("Bullet", out var bullet) ? bullet : DefaultBullet;
        var bulletLabel = new Label
        {
            Text = bulletText,
            FontColorOverride = GuidebookTheme.SubSectionColor,
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VAlignment.Top,
        };

        AddChild(bulletLabel);

        OnChildAdded += child =>
        {
            if (ReferenceEquals(child, bulletLabel))
                return;

            if (child is RichTextLabel richText)
                richText.Margin = new Thickness(0);

            if (child is Label label)
                label.Margin = new Thickness(0);
        };

        control = this;
        return true;
    }
}
