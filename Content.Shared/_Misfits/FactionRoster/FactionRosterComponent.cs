// #Cythisiax Added - Faction roster: grants the "open roster" action to a faction lead.

using Content.Shared.Actions;
using Robust.Shared.Prototypes;

namespace Content.Shared._Misfits.FactionRoster;

/// <summary>
/// Gives its holder an action that opens the faction roster GUI. Authority is still
/// checked server-side against <see cref="FactionRosterPrototype.AuthorizedJobs"/>.
/// </summary>
[RegisterComponent]
public sealed partial class FactionRosterComponent : Component
{
    [DataField(required: true)]
    public ProtoId<FactionRosterPrototype> Roster;

    [DataField]
    public EntProtoId Action = "ActionOpenFactionRoster";

    [DataField]
    public EntityUid? ActionEntity;

    /// <summary>Admin-granted access: skip the roster's authorized-job check.</summary>
    [DataField]
    public bool IgnoreJobRequirement;
}

public sealed partial class OpenFactionRosterActionEvent : InstantActionEvent;
