// #Misfits Add - Client EUI for the global ban list window; mirrors BanListEui but for all bans
using System.Numerics;
using Content.Client._Misfits.Administration.UI.BanListAll.Bans;
using Content.Client._Misfits.Administration.UI.BanListAll.RoleBans;
using Content.Client.Administration.UI.BanList;
using Content.Client.Eui;
using Content.Shared._Misfits.Administration.BanList;
using Content.Shared.Eui;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._Misfits.Administration.UI.BanListAll;

[UsedImplicitly]
public sealed class MisfitsBanListAllEui : BaseEui
{
    [Dependency] private readonly IUserInterfaceManager _ui = default!;

    private BanListIdsPopup? _popup;

    private readonly MisfitsBanListAllWindow _window;
    private readonly MisfitsBanListAllControl _activeBanControl;
    private readonly MisfitsBanListAllControl _allBanControl;
    private readonly MisfitsRoleBanListAllControl _activeRoleBanControl;
    private readonly MisfitsRoleBanListAllControl _allRoleBanControl;

    public MisfitsBanListAllEui()
    {
        _window = new MisfitsBanListAllWindow();
        _window.OnClose += OnClosed;

        _activeBanControl = _window.ActiveBanList;
        _activeBanControl.LineIdsClicked += OnLineIdsClicked;

        _allBanControl = _window.AllBanList;
        _allBanControl.LineIdsClicked += OnLineIdsClicked;

        _activeRoleBanControl = _window.ActiveRoleBanList;
        _activeRoleBanControl.LineIdsClicked += OnRoleLineIdsClicked;

        _allRoleBanControl = _window.AllRoleBanList;
        _allRoleBanControl.LineIdsClicked += OnRoleLineIdsClicked;
    }

    private void OnClosed()
    {
        _popup?.Close();
        _popup?.Dispose();
        _popup = null;

        SendMessage(new CloseEuiMessage());
    }

    public override void Closed()
    {
        base.Closed();
        _window.Close();
    }

    public override void HandleState(EuiStateBase state)
    {
        if (state is not MisfitsBanListAllEuiState s)
            return;

        // Sort all four lists by ban time ascending before displaying
        s.ActiveBans.Sort((a, b) => a.Ban.BanTime.CompareTo(b.Ban.BanTime));
        s.AllBans.Sort((a, b) => a.Ban.BanTime.CompareTo(b.Ban.BanTime));
        s.ActiveRoleBans.Sort((a, b) => a.Ban.BanTime.CompareTo(b.Ban.BanTime));
        s.AllRoleBans.Sort((a, b) => a.Ban.BanTime.CompareTo(b.Ban.BanTime));

        _activeBanControl.SetBans(s.ActiveBans);
        _allBanControl.SetBans(s.AllBans);
        _activeRoleBanControl.SetRoleBans(s.ActiveRoleBans);
        _allRoleBanControl.SetRoleBans(s.AllRoleBans);
    }

    public override void Opened()
    {
        _window.OpenCentered();
    }

    private void OnLineIdsClicked(MisfitsBanListAllLine line)
    {
        ShowIdsPopup(line.Ban.Id, line.Ban.Address?.address, line.Ban.HWId, line.Ban.UserId?.ToString());
    }

    private void OnRoleLineIdsClicked(MisfitsRoleBanListAllLine line)
    {
        ShowIdsPopup(line.Ban.Id, line.Ban.Address?.address, line.Ban.HWId, line.Ban.UserId?.ToString());
    }

    private void ShowIdsPopup(int? id, string? address, string? hwid, string? guid)
    {
        _popup?.Close();
        _popup = null;

        var idStr = id == null ? string.Empty : Loc.GetString("ban-list-id", ("id", id.Value));
        var ipStr = address == null ? string.Empty : Loc.GetString("ban-list-ip", ("ip", address));
        var hwidStr = hwid == null ? string.Empty : Loc.GetString("ban-list-hwid", ("hwid", hwid));
        var guidStr = guid == null ? string.Empty : Loc.GetString("ban-list-guid", ("guid", guid));

        _popup = new BanListIdsPopup(idStr, ipStr, hwidStr, guidStr);

        var box = UIBox2.FromDimensions(_ui.MousePositionScaled.Position, new Vector2(1, 1));
        _popup.Open(box);
    }
}
