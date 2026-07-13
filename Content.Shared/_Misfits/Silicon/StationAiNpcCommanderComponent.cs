using Robust.Shared.GameStates;
using Robust.Shared.Map;

namespace Content.Shared._Misfits.Silicon;

/// <summary>
/// [Changed by MisfitsCrew/Operator] Per-AI selection state for issuing camera-supervised orders to NPCs.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class StationAiNpcCommanderComponent : Component
{
    [DataField, AutoNetworkedField]
    public HashSet<EntityUid> SelectedNpcs = new();

    [DataField, AutoNetworkedField]
    public List<NetCoordinates> PendingMoveTargets = new();

    [DataField, AutoNetworkedField]
    public List<NetCoordinates> MoveTargetPreviews = new();

    [DataField]
    // [Changed by MisfitsCrew/Operator] Limit both core and shunted commanders to two simultaneously controlled NPCs.
    public int MaxSelected = 2;
}
