// #Cythisiax Added - Faction rank. This is the rank itself, the name, the short prefix that goes in
// front of their name in chat, the paygrade and the color. It sits on every rank pin, so whoever
// wears the pin gets that rank. Factions without pins get it stamped on their ID tag at spawn,
// copied from the rank block in their job yaml.
// The job yaml rank block lives here too. Pin jobs list the pins they work up through and how many
// hours each one takes. Jobs without pins just write the rank out right there.

using Content.Shared.Roles;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Misfits.FactionRank;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class FactionRankComponent : Component
{
    [DataField, AutoNetworkedField]
    public string Name = string.Empty;

    [DataField, AutoNetworkedField]
    public string Prefix = string.Empty;

    [DataField, AutoNetworkedField]
    public string? Paygrade;

    [DataField, AutoNetworkedField]
    public Color? Color;
}

[DataDefinition]
public sealed partial class JobRank
{
    [DataField]
    public string? Name;

    [DataField]
    public string? Prefix;

    [DataField]
    public string? Paygrade;

    [DataField]
    public Color? Color;

    // counts time across the whole department instead of just this job
    [DataField]
    public ProtoId<DepartmentPrototype>? Department;

    [DataField]
    public List<JobRankPin> Pins = new();
}

[DataDefinition]
public sealed partial class JobRankPin
{
    [DataField(required: true)]
    public EntProtoId Pin;

    [DataField]
    public int Hours;
}
