using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Content.Client._Misfits.Guidebook;
using Content.Client.Guidebook.Richtext;

// ReSharper disable InconsistentNaming
namespace Content.Client._Misfits.Guidebook.Richtext;

// #Misfits Add - HTML <h3> tag handler for guidebook parser (sub-section header)

/// <summary>
/// Guidebook document tag: <h3 Text="title"/>
/// Renders a third-level heading using the LabelSubText style class.
/// Usage: <h3 Text="My Subsection"/>
/// </summary>
[UsedImplicitly]
public sealed class h3 : Label, IDocumentTag
{
    public bool TryParseTag(Dictionary<string, string> args, [NotNullWhen(true)] out Control? control)
    {
        // Requires a Text attribute for the heading content
        if (!args.TryGetValue("Text", out var text))
        {
            Logger.Error("Guidebook tag \"h3\" requires a \"Text\" attribute.");
            control = null;
            return false;
        }

        Text = text;
        GuidebookTheme.ApplySubSectionTitle(this);

        control = this;
        return true;
    }
}
