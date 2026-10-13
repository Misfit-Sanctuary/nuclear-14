using System.Linq;
using Content.Server.Administration.Logs;
using Content.Server.Administration.Managers;
using Content.Server.Administration.Notes;
using Content.Server.Administration.Systems;
using Content.Server.Database;
using Content.Server.EUI;
using Content.Server.GameTicking; // #Misfits Add — GameTicker.Respawn for online players
using Content.Shared.Administration;
using Content.Shared.Database;
using Content.Shared.Eui;
using Content.Shared.Follower; // #Misfits Change
using Content.Shared.Ghost; // #Misfits Change
using Content.Shared.Mind; // #Misfits Add — MindComponent for offline entity lookup
using Content.Shared.Players; // #Misfits Add — ContentData() extension for offline mind lookup
using Robust.Server.Console; // #Misfits Change
using Robust.Server.Player;
using Robust.Shared.Player;

namespace Content.Server.Administration;

public sealed class PlayerPanelEui : BaseEui
{
    [Dependency] private readonly IAdminManager _admins = default!;
    [Dependency] private readonly IServerDbManager _db = default!;
    [Dependency] private readonly IAdminNotesManager _notesMan = default!;
    [Dependency] private readonly IEntityManager _entity = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly EuiManager _eui = default!;
    [Dependency] private readonly IAdminLogManager _adminLog = default!;
    [Dependency] private readonly IServerConsoleHost _consoleHost = default!; // #Misfits Change

    private readonly LocatedPlayerData _targetPlayer;
    private int? _notes;
    private int? _bans;
    private int? _roleBans;
    private int _sharedConnections;
    private bool? _whitelisted;
    private TimeSpan _playtime;
    private bool _frozen;
    private bool _canFreeze;
    private bool _canAhelp;

    public PlayerPanelEui(LocatedPlayerData player)
    {
        IoCManager.InjectDependencies(this);
        _targetPlayer = player;
    }

    public override void Opened()
    {
        base.Opened();
        _admins.OnPermsChanged += OnPermsChanged;
        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"{Player:actor} viewed player panel for {_targetPlayer.Username} ({_targetPlayer.UserId.UserId:targetPlayerId})");
    }

    public override void Closed()
    {
        base.Closed();
        _admins.OnPermsChanged -= OnPermsChanged;
    }

    public override EuiStateBase GetNewState()
    {
        _player.TryGetSessionById(_targetPlayer.UserId, out var session);
        // #Misfits Add — pass whether the player is currently online so the client can reflect it.
        var connected = session != null;

        return new PlayerPanelEuiState(_targetPlayer.UserId,
            _targetPlayer.Username,
            _entity.GetNetEntity(session?.AttachedEntity),
            _playtime,
            _notes,
            _bans,
            _roleBans,
            _sharedConnections,
            _whitelisted,
            _canFreeze,
            _frozen,
            _canAhelp,
            connected); // #Misfits Add
    }

    private void OnPermsChanged(AdminPermsChangedEventArgs args)
    {
        if (args.Player != Player)
            return;

        SetPlayerState();
    }

    public override void HandleMessage(EuiMessageBase msg)
    {
        base.HandleMessage(msg);

        ICommonSession? session;

        switch (msg)
        {
            case PlayerPanelFreezeMessage freezeMsg:
                if (!_admins.IsAdmin(Player) ||
                    !_entity.TrySystem<AdminFrozenSystem>(out var frozenSystem) ||
                    !_player.TryGetSessionById(_targetPlayer.UserId, out session) ||
                    session.AttachedEntity == null)
                    return;

                if (_entity.HasComponent<AdminFrozenComponent>(session.AttachedEntity))
                {
                    _adminLog.Add(LogType.Action,$"{Player:actor} unfroze {_entity.ToPrettyString(session.AttachedEntity):subject}");
                    _entity.RemoveComponent<AdminFrozenComponent>(session.AttachedEntity.Value);
                    SetPlayerState();
                    return;
                }

                if (freezeMsg.Mute)
                {
                    _adminLog.Add(LogType.Action,$"{Player:actor} froze and muted {_entity.ToPrettyString(session.AttachedEntity):subject}");
                    frozenSystem.FreezeAndMute(session.AttachedEntity.Value);
                }
                else
                {
                    _adminLog.Add(LogType.Action,$"{Player:actor} froze {_entity.ToPrettyString(session.AttachedEntity):subject}");
                    _entity.EnsureComponent<AdminFrozenComponent>(session.AttachedEntity.Value);
                }
                SetPlayerState();
                break;

            case PlayerPanelLogsMessage:
                if (!_admins.HasAdminFlag(Player, AdminFlags.Logs))
                    return;

                _adminLog.Add(LogType.Action, $"{Player:actor} opened logs on {_targetPlayer.Username:subject}");
                var ui = new AdminLogsEui();
                _eui.OpenEui(ui, Player);
                ui.SetLogFilter(search: _targetPlayer.Username);
                break;
            // #Misfits Change — Ghost Follow: ensure admin is in aghost mode, then orbit the target player's entity.
            case PlayerPanelGhostFollowMessage:
                if (!_admins.HasAdminFlag(Player, AdminFlags.Admin))
                    return;

                if (!_player.TryGetSessionById(_targetPlayer.UserId, out session) ||
                    session.AttachedEntity == null)
                    return;

                // If the admin is not already an admin ghost, enter aghost mode first.
                var alreadyAGhost = Player.AttachedEntity.HasValue &&
                    _entity.TryGetComponent<GhostComponent>(Player.AttachedEntity.Value, out var ghostComp) &&
                    ghostComp.CanGhostInteract;
                if (!alreadyAGhost)
                    _consoleHost.ExecuteCommand(Player, "aghost");

                // After entering aghost, make the ghost entity orbit the target.
                if (Player.AttachedEntity != null &&
                    _entity.TrySystem<FollowerSystem>(out var followerSystem))
                {
                    _adminLog.Add(LogType.Action,
                        $"{Player:actor} ghost-followed {_entity.ToPrettyString(session.AttachedEntity.Value):subject}");
                    followerSystem.StartFollowingEntity(Player.AttachedEntity.Value, session.AttachedEntity.Value);
                }
                break;

            case PlayerPanelDeleteMessage:
            case PlayerPanelRejuvenationMessage:
                if (!_admins.HasAdminFlag(Player, AdminFlags.Debug) ||
                    !_player.TryGetSessionById(_targetPlayer.UserId, out session) ||
                    session.AttachedEntity == null)
                    return;

                if (msg is PlayerPanelRejuvenationMessage)
                {
                    _adminLog.Add(LogType.Action,$"{Player:actor} rejuvenated {_entity.ToPrettyString(session.AttachedEntity):subject}");
                    if (!_entity.TrySystem<RejuvenateSystem>(out var rejuvenate))
                        return;

                    rejuvenate.PerformRejuvenate(session.AttachedEntity.Value);
                }
                else
                {
                    _adminLog.Add(LogType.Action,$"{Player:actor} deleted {_entity.ToPrettyString(session.AttachedEntity):subject}");
                    _entity.DeleteEntity(session.AttachedEntity);
                }
                break;

            // #Misfits Add — Respawn/Despawn: deletes the lingering entity and frees the spawn slot.
            case PlayerPanelRespawnMessage:
                if (!_admins.HasAdminFlag(Player, AdminFlags.Admin))
                    return;

                if (!_entity.TrySystem<GameTicker>(out var gameTicker) ||
                    !_entity.TrySystem<SharedMindSystem>(out var mindSystem))
                    return;

                if (_player.TryGetSessionById(_targetPlayer.UserId, out session))
                {
                    // Online player: standard respawn kicks them to lobby and cleans up their entity.
                    _adminLog.Add(LogType.Action,
                        $"{Player:actor} respawned online player {_targetPlayer.Username} via Player Panel");
                    gameTicker.Respawn(session);
                }
                else
                {
                    // Offline player: delete their lingering entity (if any) and wipe their mind so
                    // the spawn slot is freed when they reconnect.
                    if (!_player.TryGetPlayerData(_targetPlayer.UserId, out var pData))
                        return;

                    var mindEnt = pData.ContentData()?.Mind;
                    if (mindEnt != null &&
                        _entity.TryGetComponent<MindComponent>(mindEnt.Value, out var mindComp) &&
                        mindComp.CurrentEntity is { } bodyEnt)
                    {
                        _adminLog.Add(LogType.Action,
                            $"{Player:actor} despawned entity of offline player {_targetPlayer.Username}");
                        _entity.DeleteEntity(bodyEnt);
                    }

                    mindSystem.WipeMind(mindEnt);
                    _adminLog.Add(LogType.Action,
                        $"{Player:actor} wiped mind of offline player {_targetPlayer.Username} via Player Panel");
                }
                break;
        }
    }

    public async void SetPlayerState()
    {
        if (!_admins.IsAdmin(Player))
        {
            Close();
            return;
        }

        _playtime = (await _db.GetPlayTimes(_targetPlayer.UserId))
            .Where(p => p.Tracker == "Overall")
            .Select(p => p.TimeSpent)
            .FirstOrDefault();

        if (_notesMan.CanView(Player))
        {
            _notes = (await _notesMan.GetAllAdminRemarks(_targetPlayer.UserId)).Count;
        }
        else
        {
            _notes = null;
        }

        _sharedConnections = _player.Sessions.Count(s => s.Channel.RemoteEndPoint.Address.Equals(_targetPlayer.LastAddress) && s.UserId != _targetPlayer.UserId);

    // Apparently the Bans flag is also used for whitelists
    if (_admins.HasAdminFlag(Player, AdminFlags.Ban))
        {
            _whitelisted = await _db.GetWhitelistStatusAsync(_targetPlayer.UserId);
            // This won't get associated ip or hwid bans but they were not placed on this account anyways
            _bans = (await _db.GetServerBansAsync(null, _targetPlayer.UserId, null, null)).Count;
            // Unfortunately role bans for departments and stuff are issued individually. This means that a single role ban can have many individual role bans internally
            // The only way to distinguish whether a role ban is the same is to compare the ban time.
            // This is horrible and I would love to just erase the database and start from scratch instead but that's what I can do for now.
            _roleBans = (await _db.GetServerRoleBansAsync(null, _targetPlayer.UserId, null, null)).DistinctBy(rb => rb.BanTime).Count();
        }
        else
        {
            _whitelisted = null;
            _bans = null;
            _roleBans = null;
        }

        if (_player.TryGetSessionById(_targetPlayer.UserId, out var session))
        {
            _canFreeze = session.AttachedEntity != null;
            _frozen = _entity.HasComponent<AdminFrozenComponent>(session.AttachedEntity);
        }
        else
        {
            _canFreeze = false;
        }

        if (_admins.HasAdminFlag(Player, AdminFlags.Adminhelp))
        {
            _canAhelp = true;
        }
        else
        {
            _canAhelp = false;
        }

        StateDirty();
    }
}
