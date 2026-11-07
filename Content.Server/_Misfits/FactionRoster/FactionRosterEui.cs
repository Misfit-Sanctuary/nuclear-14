// #Cythisiax Added - Faction roster: server side of the faction lead's roster window.

using Content.Server.EUI;
using Content.Shared.Eui;
using Content.Shared._Misfits.FactionRoster;
using Robust.Shared.Prototypes;

namespace Content.Server._Misfits.FactionRoster;

public sealed class FactionRosterEui : BaseEui
{
    [Dependency] private readonly IEntityManager _entMan = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;

    private readonly FactionRosterSystem _system;
    public readonly string RosterId;
    private string? _status;

    public FactionRosterEui(FactionRosterSystem system, string rosterId)
    {
        IoCManager.InjectDependencies(this);
        _system = system;
        RosterId = rosterId;
    }

    public override void Opened()
    {
        base.Opened();
        _system.RegisterUi(this);

        if (_proto.TryIndex<FactionRosterPrototype>(RosterId, out var roster))
            _system.LoadRoster(roster);

        StateDirty();
    }

    public override void Closed()
    {
        base.Closed();
        _system.UnregisterUi(this);
    }

    public override EuiStateBase GetNewState()
    {
        if (!_proto.TryIndex<FactionRosterPrototype>(RosterId, out var roster))
            return new FactionRosterEuiState(RosterId, new(), new(), new(), new(), false, null, string.Empty, false, false);

        return _system.BuildState(roster, Player, _status);
    }

    public override void HandleMessage(EuiMessageBase msg)
    {
        base.HandleMessage(msg);

        if (IsShutDown)
            return;

        if (!_proto.TryIndex<FactionRosterPrototype>(RosterId, out var roster))
            return;

        // Re-check authority on every message: the lead may have died, been
        // demoted, or swapped bodies since the window opened.
        if (Player.AttachedEntity is not { } user || !_system.IsAuthorized(user, roster))
        {
            Close();
            return;
        }

        switch (msg)
        {
            case FactionRosterRefreshMessage:
                _status = null;
                _system.LoadRoster(roster);
                break;
            case FactionRosterPromoteMessage promote:
                _status = _system.Promote(roster, Player, promote.UserId, promote.Track);
                break;
            case FactionRosterDemoteMessage demote:
                _status = _system.Demote(roster, Player, demote.UserId, demote.Track);
                break;
            case FactionRosterDischargeMessage discharge:
                _status = _system.Discharge(roster, Player, discharge.UserId);
                break;
            case FactionRosterToggleSpecialtyMessage toggle:
                _status = _system.ToggleSpecialty(roster, Player, toggle.UserId, toggle.Role);
                break;
            case FactionRosterRecruitMessage recruit:
                if (_entMan.TryGetEntity(recruit.Target, out var target))
                    _status = _system.OfferRecruitment(roster, user, target.Value);
                break;
            case FactionRosterSetOathMessage oath:
                _status = _system.SetOath(roster, Player, oath.Oath);
                break;
            default:
                return;
        }

        StateDirty();
    }
}
