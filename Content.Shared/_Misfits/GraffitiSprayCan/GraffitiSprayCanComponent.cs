// #Misfits Add - Component for the graffiti spray can item.
// Tracks the selected graffiti decal and how many sprays remain.

using Content.Shared.DoAfter;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;

namespace Content.Shared._Misfits.GraffitiSprayCan;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class GraffitiSprayCanComponent : Component
{
    /// <summary>
    /// The decal ID currently selected by the player. Null until they pick one.
    /// </summary>
    [DataField, AutoNetworkedField]
    public string? SelectedDecalId;

    /// <summary>
    /// How many sprays remain before the can is exhausted.
    /// </summary>
    [DataField, AutoNetworkedField]
    public int Charges = 10;

    /// <summary>
    /// Time required to complete a spray do-after.
    /// </summary>
    [DataField]
    public TimeSpan SprayTime = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Sound played while spraying.
    /// </summary>
    [DataField]
    public SoundSpecifier SpraySound = new SoundPathSpecifier("/Audio/Effects/spray2.ogg");

    /// <summary>
    /// Tracks the active do-after to prevent stacking multiple sprays.
    /// </summary>
    [DataField]
    public DoAfterId? ActiveDoAfter;
}
