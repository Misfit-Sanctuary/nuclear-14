using Robust.Shared.GameStates;

namespace Content.Shared._Misfits.Warcry;

/// <summary>
/// Marks a warcry source so clients can render its active radius.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ActiveWarcryComponent : Component
{
    /// <summary>
    /// Radius of the active warcry area.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Radius = 6f;

    /// <summary>
    /// Color used for the client overlay.
    /// </summary>
    [DataField, AutoNetworkedField]
    public Color Color = Color.Red;

    /// <summary>
    /// When the overlay should stop being shown.
    /// </summary>
    [DataField, AutoNetworkedField]
    public TimeSpan ExpiresAt; // #Misfits Fix - networked for client-side timing consistency
}