// SPDX-License-Identifier: AGPL-3.0-or-later
// Adapted from Goob Station / Trauma Station

using Content.Client.UserInterface.Controls;
using Content.Shared._Misfits.Factory.Filters;

namespace Content.Client._Misfits.Factory.UI;

[GenerateTypedNameReferences]
public sealed partial class LabelFilterWindow : FancyWindow
{
    [Dependency] private readonly EntityManager _entMan = default!;

    public event Action<string>? OnSetLabel;

    public LabelFilterWindow()
    {
        IoCManager.InjectDependencies(this);
        RobustXamlLoader.Load(this);

        LabelEdit.OnTextChanged += _ => OnSetLabel?.Invoke(LabelEdit.Text);
    }

    public void SetEntity(EntityUid uid)
    {
        if (!_entMan.TryGetComponent<LabelFilterComponent>(uid, out var comp))
            return;

        var max = comp.MaxLength;
        LabelEdit.IsValid = label => label.Length < max;
        LabelEdit.Text = comp.Label;
    }
}
