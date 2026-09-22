// SPDX-License-Identifier: AGPL-3.0-or-later
// Adapted from Goob Station / Trauma Station

using Content.Client.UserInterface.Controls;
using Content.Shared._Misfits.Factory.Filters;

namespace Content.Client._Misfits.Factory.UI;

[GenerateTypedNameReferences]
public sealed partial class StackFilterWindow : FancyWindow
{
    [Dependency] private readonly EntityManager _entMan = default!;

    public event Action<int>? OnSetMin;
    public event Action<int>? OnSetSize;

    public StackFilterWindow()
    {
        IoCManager.InjectDependencies(this);
        RobustXamlLoader.Load(this);

        MinSpin.IsValid = min => min >= 1;
        MinSpin.ValueChanged += args => OnSetMin?.Invoke(args.Value);

        SizeSpin.IsValid = size => size >= 0;
        SizeSpin.ValueChanged += args => OnSetSize?.Invoke(args.Value);
    }

    public void SetEntity(EntityUid uid)
    {
        if (!_entMan.TryGetComponent<StackFilterComponent>(uid, out var comp))
            return;

        MinSpin.OverrideValue(comp.Min);
        SizeSpin.OverrideValue(comp.Size);
    }
}
