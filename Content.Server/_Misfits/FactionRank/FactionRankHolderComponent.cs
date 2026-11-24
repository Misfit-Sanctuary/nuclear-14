// #Cythisiax Added - Faction rank holder. Put on a pin-faction character at spawn so the roster and
// the rank system know which job and saved row they belong to without hitting the database.

using Content.Shared.Roles;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Server._Misfits.FactionRank;

[RegisterComponent]
public sealed partial class FactionRankHolderComponent : Component
{
    [ViewVariables]
    public ProtoId<JobPrototype> Job;

    [ViewVariables]
    public NetUserId UserId;

    [ViewVariables]
    public string CharacterName = string.Empty;

    [ViewVariables]
    public string? HeldRank;

    [ViewVariables]
    public string? GrantedRank;

    [ViewVariables]
    public bool GrantCapped;
}
