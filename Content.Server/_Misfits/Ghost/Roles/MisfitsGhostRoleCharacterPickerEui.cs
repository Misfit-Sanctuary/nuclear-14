// #Misfits Add - Server side of the ghost role character picker EUI.
using Content.Server.EUI;
using Content.Shared._Misfits.Ghost.Roles;
using Content.Shared.Eui;

namespace Content.Server._Misfits.Ghost.Roles;

public sealed class MisfitsGhostRoleCharacterPickerEui : BaseEui
{
    private readonly MisfitsGhostRoleCharacterPickerSystem _system;
    private readonly EntityUid _spawner;
    private readonly string _roleName;
    private bool _tookRole;

    public MisfitsGhostRoleCharacterPickerEui(MisfitsGhostRoleCharacterPickerSystem system, EntityUid spawner,
        string roleName)
    {
        _system = system;
        _spawner = spawner;
        _roleName = roleName;
    }

    public override void Opened()
    {
        base.Opened();

        // Nothing reaches the client until the state is dirty, so this opens an empty window.
        StateDirty();
    }

    public override EuiStateBase GetNewState()
    {
        return new MisfitsGhostRoleCharacterEuiState(_roleName, _system.GetOptions(Player, _spawner));
    }

    public override void HandleMessage(EuiMessageBase msg)
    {
        base.HandleMessage(msg);

        if (msg is not MisfitsGhostRoleCharacterChosenMessage chosen)
            return;

        _tookRole = _system.TrySpawn(Player, _spawner, chosen.Index);

        // The mind transfer may already have closed this picker, so do not close it twice.
        if (_tookRole && !IsShutDown)
            Close();
    }

    public override void Closed()
    {
        base.Closed();

        // Cancelled, disconnected or timed out: hand the role back so it is not left reserved.
        _system.OnPickerClosed(_spawner, this, _tookRole);
    }
}
