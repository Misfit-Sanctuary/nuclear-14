// SPDX-License-Identifier: AGPL-3.0-or-later
// Adapted from Goob Station / Trauma Station

using Content.Client.UserInterface.Controls;
using Content.Shared.Mobs;

namespace Content.Client._Misfits.Factory.UI;

[GenerateTypedNameReferences]
public sealed partial class MobFilterWindow : FancyWindow
{
    public event Action<MobState>? OnToggle;

    public MobFilterWindow()
    {
        RobustXamlLoader.Load(this);

        AliveButton.OnPressed += _ => OnToggle?.Invoke(MobState.Alive);
        DeadButton.OnPressed += _ => OnToggle?.Invoke(MobState.Dead);
        CriticalButton.OnPressed += _ => OnToggle?.Invoke(MobState.Critical);
        SoftCriticalButton.OnPressed += _ => OnToggle?.Invoke(MobState.SoftCritical);
    }

    public void SelectValues(HashSet<MobState> states)
    {
        AliveButton.Pressed = states.Contains(MobState.Alive);
        DeadButton.Pressed = states.Contains(MobState.Dead);
        CriticalButton.Pressed = states.Contains(MobState.Critical);
        SoftCriticalButton.Pressed = states.Contains(MobState.SoftCritical);
    }
}
