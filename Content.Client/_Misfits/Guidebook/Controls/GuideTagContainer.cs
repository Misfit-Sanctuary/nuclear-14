using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Content.Client.Guidebook.Richtext;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._Misfits.Guidebook.Controls;

public class GuideTagContainer : BoxContainer, IDocumentTag
{
    private readonly HashSet<Control> _ownedChildren = new();
    private Control? _contentHost;

    protected void ConfigureContentHost(Control contentHost)
    {
        _contentHost = contentHost;

        foreach (var child in Children)
        {
            _ownedChildren.Add(child);
        }

        OnChildAdded += RedirectParsedChild;
    }

    private void RedirectParsedChild(Control child)
    {
        if (_contentHost == null || _ownedChildren.Contains(child))
            return;

        child.Orphan();
        _contentHost.AddChild(child);
    }

    public virtual bool TryParseTag(Dictionary<string, string> args, [NotNullWhen(true)] out Control? control)
    {
        control = this;
        return true;
    }
}