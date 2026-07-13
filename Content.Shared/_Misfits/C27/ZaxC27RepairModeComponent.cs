using Content.Shared.Actions;
using Content.Shared.Damage;
using Content.Shared.DoAfter;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Misfits.C27;

/// <summary>
///     Grants a Z.A.X C-27 chassis a stationary repair mode that swaps passive
///     regeneration between normal and braced rates.
/// </summary>
[RegisterComponent]
public sealed partial class ZaxC27RepairModeComponent : Component
{
    [DataField]
    public TimeSpan ToggleDelay = TimeSpan.FromSeconds(10);

    [DataField(required: true)]
    public DamageSpecifier NormalRepair = new();

    [DataField(required: true)]
    public DamageSpecifier ActiveRepair = new();

    public EntityUid? ActionEntity;
}

/// <summary>
///     Marker present while the chassis is locked down in enhanced repair mode.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class ZaxC27RepairModeActiveComponent : Component
{
}

/// <summary>
///     Marker present during the 10 second enter/exit transition.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class ZaxC27RepairModeTransitionComponent : Component
{
}

public sealed partial class ToggleZaxC27RepairModeEvent : InstantActionEvent
{
}

[Serializable, NetSerializable]
public sealed partial class ZaxC27RepairModeDoAfterEvent : SimpleDoAfterEvent
{
    public bool Activate;

    public ZaxC27RepairModeDoAfterEvent()
    {
    }

    public ZaxC27RepairModeDoAfterEvent(bool activate)
    {
        Activate = activate;
    }
}
