using System.Diagnostics.CodeAnalysis;
using Content.Client.UserInterface.Controls;
using Content.Client._Misfits.Guidebook;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client.Guidebook.Richtext;

[UsedImplicitly]
public sealed class Table : TableContainer, IDocumentTag
{
    public bool TryParseTag(Dictionary<string, string> args, [NotNullWhen(true)] out Control? control)
    {
        // #Misfits Tweak - Give tables consistent block spacing with the rest of the guidebook system.
        HorizontalExpand = true;
        Margin = new Thickness(0, 0, 0, GuidebookTheme.ComponentBottomMargin);
        control = this;

        if (!args.TryGetValue("Columns", out var columns) || !int.TryParse(columns, out var columnsCount))
        {
            Logger.Error("Guidebook tag \"Table\" does not specify required property \"Columns.\"");
            control = null;
            return false;
        }

        Columns = columnsCount;

        return true;
    }
}
