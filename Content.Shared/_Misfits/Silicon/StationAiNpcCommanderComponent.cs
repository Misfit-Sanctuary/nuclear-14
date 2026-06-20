using Robust.Shared.GameStates;

namespace Content.Shared._Misfits.Silicon;

/// <summary>
/// [Changed by MisfitsCrew/Operator] Per-AI selection state for issuing camera-supervised orders to NPCs.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class StationAiNpcCommanderComponent : Component
{
    [DataField, AutoNetworkedField]
    public HashSet<EntityUid> SelectedNpcs = new();

    [DataField]
    public int MaxSelected = 12;
}
