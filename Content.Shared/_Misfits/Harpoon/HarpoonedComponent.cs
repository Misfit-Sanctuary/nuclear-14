using Robust.Shared.GameStates;

namespace Content.Shared._Misfits.Harpoon;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(HarpoonSystem), typeof(HarpoonReelController))]
public sealed partial class HarpoonedComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid? Harpoon;

    [DataField, AutoNetworkedField]
    public EntityUid? Thrower;

    [DataField, AutoNetworkedField]
    public bool Reeling;

    [DataField, AutoNetworkedField]
    public float ReelSpeed = 3f;

    [DataField, AutoNetworkedField]
    public float StruggleModifier = 0.4f;

    [DataField, AutoNetworkedField]
    public float MinDistance = 1f;

    [DataField, AutoNetworkedField]
    public float MaxRopeLength = 7f;

    [DataField]
    public EntityUid? ReelStream;
}
