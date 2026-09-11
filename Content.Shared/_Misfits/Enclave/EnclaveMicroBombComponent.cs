using Content.Shared.UserInterface;
using Content.Shared.Eui;
using Robust.Shared.Audio;
using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared._Misfits.Enclave;

/// <summary>
/// Marks a microbomb as part of the Enclave's command network.
/// </summary>
[RegisterComponent]
public sealed partial class EnclaveMicroBombComponent : Component
{
    /// <summary>
    /// Played from the implanted body with every numeric countdown tick.
    /// </summary>
    [DataField]
    public SoundSpecifier BeepSound = new SoundPathSpecifier("/Audio/Effects/beep1.ogg");

    /// <summary>
    /// Runtime countdown state. This is authoritative on the server and does
    /// not need to be networked; warnings are delivered through popups.
    /// </summary>
    [ViewVariables]
    public bool CountdownActive;

    [ViewVariables]
    public TimeSpan NextAnnouncement;

    [ViewVariables]
    public TimeSpan DetonationTime;

    [ViewVariables]
    public int NextNumber = 5;

    [ViewVariables]
    public EntityUid? Initiator;
}

/// <summary>
/// Marks a handheld device as an Enclave microbomb detonator.
/// </summary>
[RegisterComponent]
public sealed partial class EnclaveDetonatorComponent : Component;

[Serializable, NetSerializable]
public enum EnclaveDetonatorUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
// Misfits Tweak - IsDead lets the detonator list corpses instead of hiding them,
// so an implant can still be blown after the wearer is killed.
public readonly record struct EnclaveMicroBombEntry(NetEntity Entity, string Name, string Role, bool IsDead);

[Serializable, NetSerializable]
public sealed class EnclaveDetonatorBoundUserInterfaceState : BoundUserInterfaceState
{
    public readonly EnclaveMicroBombEntry[] Personnel;

    public EnclaveDetonatorBoundUserInterfaceState(EnclaveMicroBombEntry[] personnel)
    {
        Personnel = personnel;
    }
}

[Serializable, NetSerializable]
public sealed class EnclaveDetonatorRefreshMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class EnclaveDetonatorActivateMessage : BoundUserInterfaceMessage
{
    public readonly NetEntity Target;

    public EnclaveDetonatorActivateMessage(NetEntity target)
    {
        Target = target;
    }
}

[Serializable, NetSerializable]
public sealed class EnclaveRecruitEuiState : EuiStateBase
{
    public readonly string CharacterName;

    public EnclaveRecruitEuiState(string characterName)
    {
        CharacterName = characterName;
    }
}

[Serializable, NetSerializable]
public sealed class EnclaveRecruitDecisionMessage : EuiMessageBase
{
    public readonly bool Accepted;

    public EnclaveRecruitDecisionMessage(bool accepted)
    {
        Accepted = accepted;
    }
}
