// #Misfits Add - EUI messages for picking a character when taking an event ghost role.
using Content.Shared.Eui;
using Robust.Shared.Serialization;

namespace Content.Shared._Misfits.Ghost.Roles;

// One entry: a preference slot, or Index -1 for a randomly generated character.
[NetSerializable, Serializable]
public sealed class MisfitsGhostRoleCharacterOption
{
    public int Index { get; set; }
    public string Name { get; set; } = string.Empty;
}

[NetSerializable, Serializable]
public sealed class MisfitsGhostRoleCharacterEuiState : EuiStateBase
{
    // Role being taken, used as the window title.
    public string RoleName { get; }

    public MisfitsGhostRoleCharacterOption[] Options { get; }

    public MisfitsGhostRoleCharacterEuiState(string roleName, MisfitsGhostRoleCharacterOption[] options)
    {
        RoleName = roleName;
        Options = options;
    }
}

[NetSerializable, Serializable]
public sealed class MisfitsGhostRoleCharacterChosenMessage : EuiMessageBase
{
    // Preference slot of the chosen character, or -1 for a random one.
    public int Index { get; }

    public MisfitsGhostRoleCharacterChosenMessage(int index)
    {
        Index = index;
    }
}
