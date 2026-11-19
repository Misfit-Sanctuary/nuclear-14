// #Misfits Add - server side of the "name the mob you just took over" prompt.
using Content.Server.EUI;
using Content.Shared._Misfits.Events;
using Content.Shared.Eui;

namespace Content.Server._Misfits.Events;

public sealed class MisfitsNamePromptEui : BaseEui
{
    private readonly EntityUid _target;
    private readonly MisfitsNamePromptSystem _system;
    private readonly int _maxLength;

    public MisfitsNamePromptEui(EntityUid target, MisfitsNamePromptSystem system, int maxLength)
    {
        _target = target;
        _system = system;
        _maxLength = maxLength;
    }

    public override void Opened()
    {
        base.Opened();

        // Nothing reaches the client until the state is dirty, so this opens the window.
        StateDirty();
    }

    public override EuiStateBase GetNewState()
    {
        return new MisfitsNamePromptEuiState(Loc.GetString("misfits-name-prompt-title"),
            _system.GetEntityName(_target));
    }

    public override void HandleMessage(EuiMessageBase msg)
    {
        base.HandleMessage(msg);

        if (msg is not MisfitsNamePromptSubmittedMessage submitted)
            return;

        var name = submitted.Name.Trim();
        if (name.Length == 0 || name.Length > _maxLength)
            return;

        // The mob was deleted while the window was open; nothing to rename.
        if (!_system.TryRenameEntity(_target, name))
            return;

        Close();
    }
}
