// #Misfits Add - EUI messages for naming the mob a player just took over.
using Content.Shared.Eui;
using Robust.Shared.Serialization;

namespace Content.Shared._Misfits.Events;

[NetSerializable, Serializable]
public sealed class MisfitsNamePromptEuiState : EuiStateBase
{
    // Window title, so the same prompt can be reused by other roles.
    public string Title { get; }

    // The mob's current name, offered as the starting text.
    public string CurrentName { get; }

    public MisfitsNamePromptEuiState(string title, string currentName)
    {
        Title = title;
        CurrentName = currentName;
    }
}

[NetSerializable, Serializable]
public sealed class MisfitsNamePromptSubmittedMessage : EuiMessageBase
{
    public string Name { get; }

    public MisfitsNamePromptSubmittedMessage(string name)
    {
        Name = name;
    }
}
