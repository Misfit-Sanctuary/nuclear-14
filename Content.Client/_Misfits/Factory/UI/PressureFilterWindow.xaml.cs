// SPDX-License-Identifier: AGPL-3.0-or-later
// Adapted from Goob Station / Trauma Station

using Content.Client.UserInterface.Controls;
using Content.Shared._Misfits.Factory.Filters;

namespace Content.Client._Misfits.Factory.UI;

[GenerateTypedNameReferences]
public sealed partial class PressureFilterWindow : FancyWindow
{
    [Dependency] private readonly EntityManager _entMan = default!;

    public event Action<float>? OnSetMin;
    public event Action<float>? OnSetMax;

    private readonly FloatSpinBox _minSpin;
    private readonly FloatSpinBox _maxSpin;

    private float _min, _max;

    public PressureFilterWindow()
    {
        IoCManager.InjectDependencies(this);
        RobustXamlLoader.Load(this);

        _minSpin = new FloatSpinBox(1f, 3) { HorizontalExpand = true };
        _maxSpin = new FloatSpinBox(1f, 3) { HorizontalExpand = true };
        MinSpinContainer.AddChild(_minSpin);
        MaxSpinContainer.AddChild(_maxSpin);

        _minSpin.IsValid = min => min >= 0f && min <= _max;
        _maxSpin.IsValid = max => max >= _min;

        _minSpin.OnValueChanged += args =>
        {
            _min = args.Value;
            OnSetMin?.Invoke(_min);
        };

        _maxSpin.OnValueChanged += args =>
        {
            _max = args.Value;
            OnSetMax?.Invoke(_max);
        };
    }

    public void SetEntity(EntityUid uid)
    {
        if (!_entMan.TryGetComponent<PressureFilterComponent>(uid, out var comp))
            return;

        _min = comp.Min;
        _max = comp.Max;
        _minSpin.Value = _min;
        _maxSpin.Value = _max;
    }
}
