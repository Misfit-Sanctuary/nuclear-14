using System.Linq;
using Content.Server.Administration;
using Content.Server.Administration.Logs;
using Content.Server.Administration.Managers;
using Content.Server.EUI;
using Content.Shared._Misfits.Supporter;
using Content.Shared.Administration;
using Content.Shared.Database;
using Content.Shared.Eui;
using Robust.Server.Player;

namespace Content.Server._Misfits.Supporter;

public sealed class SupporterManagerEui : BaseEui
{
    [Dependency] private readonly ISupporterManager _supporters = default!;
    [Dependency] private readonly IAdminManager _admins = default!;
    [Dependency] private readonly IAdminLogManager _adminLog = default!;
    [Dependency] private readonly IPlayerLocator _locator = default!;

    private string? _pendingStatus;

    public SupporterManagerEui()
    {
        IoCManager.InjectDependencies(this);
    }

    public override async void Opened()
    {
        await _supporters.WaitLoadedAsync();
        StateDirty();
    }

    public override EuiStateBase GetNewState()
    {
        var state = new SupporterManagerState(_supporters.GetAll().ToList(), _pendingStatus);
        _pendingStatus = null;
        return state;
    }

    public override void HandleMessage(EuiMessageBase msg)
    {
        base.HandleMessage(msg);

        if (!_admins.HasAdminFlag(Player, AdminFlags.Admin))
            return;

        switch (msg)
        {
            case SupporterSetMessage setMsg:
                HandleSet(setMsg);
                break;

            case SupporterRemoveMessage removeMsg:
                HandleRemove(removeMsg);
                break;
        }
    }

    private async void HandleRemove(SupporterRemoveMessage msg)
    {
        var entry = _supporters.GetAll().FirstOrDefault(e => e.UserId == msg.UserId);
        await _supporters.RemoveSupporterAsync(msg.UserId);
        _adminLog.Add(LogType.AdminMessage, LogImpact.Medium,
            $"{Player:actor} removed supporter {entry?.Username ?? msg.UserId.ToString()}");
        _pendingStatus = $"Removed: {entry?.Username ?? msg.UserId.ToString()}";
        StateDirty();
    }

    private async void HandleSet(SupporterSetMessage msg)
    {
        Guid userId;
        string username;

        if (msg.UserId.HasValue)
        {
            userId = msg.UserId.Value;
            username = msg.Username;
        }
        else
        {
            var located = await _locator.LookupIdByNameOrIdAsync(msg.Username);
            if (located == null)
            {
                _pendingStatus = $"Player not found: {msg.Username}";
                StateDirty();
                return;
            }

            userId = located.UserId; // implicit conversion NetUserId -> Guid
            username = located.Username;
        }

        await _supporters.SetSupporterAsync(userId, username, msg.Title, msg.NameColor);
        _adminLog.Add(LogType.AdminMessage, LogImpact.Medium,
            $"{Player:actor} set supporter [{username}]: title='{msg.Title ?? "(none)"}', color='{msg.NameColor ?? "(none)"}'");
        _pendingStatus = $"Saved: {username}";
        StateDirty();
    }
}
