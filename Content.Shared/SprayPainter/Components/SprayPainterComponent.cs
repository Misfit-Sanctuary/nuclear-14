using Content.Shared.DoAfter;
using Content.Shared.Decals;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.SprayPainter.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class SprayPainterComponent : Component
{
    public static readonly ProtoId<DecalPrototype> DefaultDecal = "StencilNumber0";

    [DataField]
    public SoundSpecifier SpraySound = new SoundPathSpecifier("/Audio/Effects/spray2.ogg");

    [DataField]
    public TimeSpan AirlockSprayTime = TimeSpan.FromSeconds(3);

    [DataField]
    public TimeSpan PipeSprayTime = TimeSpan.FromSeconds(1);

    /// <summary>
    /// DoAfterId for airlock spraying.
    /// Pipes do not track doafters so you can spray multiple at once.
    /// </summary>
    [DataField]
    public DoAfterId? AirlockDoAfter;

    /// <summary>
    /// Pipe color chosen to spray with.
    /// </summary>
    [DataField, AutoNetworkedField]
    public string? PickedColor;

    /// <summary>
    /// Pipe colors that can be selected.
    /// </summary>
    [DataField]
    public Dictionary<string, Color> ColorPalette = new();

    // Arbitrary hex color for pipes, takes priority over the palette.
    [DataField, AutoNetworkedField]
    public Color? PickedCustomColor;

    /// <summary>
    /// Airlock style index selected.
    /// After prototype reload this might not be the same style but it will never be out of bounds.
    /// </summary>
    [DataField, AutoNetworkedField]
    public int Index;

    /// <summary>
    /// Whether floor interaction adds decals, removes decals, or does nothing.
    /// </summary>
    [DataField, AutoNetworkedField]
    public DecalPaintMode DecalMode = DecalPaintMode.Off;

    [DataField, AutoNetworkedField]
    public ProtoId<DecalPrototype> SelectedDecal = DefaultDecal;

    [DataField, AutoNetworkedField]
    public Color? SelectedDecalColor;

    [DataField, AutoNetworkedField]
    public int SelectedDecalAngle;

    [DataField, AutoNetworkedField]
    public bool SnapDecals = true;

    [DataField]
    public SoundSpecifier SoundSwitchDecalMode = new SoundPathSpecifier(
        "/Audio/Machines/quickbeep.ogg",
        AudioParams.Default.WithVolume(1.5f));

    /// <summary>
    /// Whether the decal colour picker from upstream PR #41943 is active.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool ColorPickerEnabled;
}

[Serializable, NetSerializable]
public enum DecalPaintMode : byte
{
    Off,
    Add,
    Remove,
}
