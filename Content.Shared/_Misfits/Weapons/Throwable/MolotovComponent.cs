using System.Numerics;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Misfits.Weapons.Throwable;

/// <summary>
/// Stores Molotov fuel, fire-spread, sound, and networked ignition state.
/// The bottle's existing sprite is retained; the client adds the wick as a separate layer.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(raiseAfterAutoHandleState: true)]
public sealed partial class MolotovComponent : Component
{
    /// <summary>The bottle's solution-container ID.</summary>
    [DataField] public string Solution = "drink";

    /// <summary>Per-bottle visual offset used to align the wick with the bottle neck.</summary>
    [DataField, AutoNetworkedField] public Vector2 WickOffset;

    /// <summary>Fire used for ordinary flammable reagents.</summary>
    [DataField] public EntProtoId FireTilePrototype = "MisfitsTileFire";

    /// <summary>Longer-lived fire selected when enough oil is present.</summary>
    [DataField] public EntProtoId OilFireTilePrototype = "MisfitsTileFireOil";

    /// <summary>Longer-lived, more damaging fire selected when enough napalm is present.</summary>
    [DataField] public EntProtoId NapalmFireTilePrototype = "MisfitsTileFireNapalm";

    /// <summary>Minimum flammable volume required to create any fire.</summary>
    [DataField] public float MinimumFuel = 5f;

    /// <summary>Fraction of total bottle capacity that must be oil to select oil fire.</summary>
    [DataField] public float OilFuelFraction = 0.25f;

    /// <summary>Fraction of total bottle capacity that must be napalm to select napalm fire.</summary>
    [DataField] public float NapalmFuelFraction = 0.5f;

    /// <summary>Filled fraction required to spread one tile from the impact point.</summary>
    [DataField] public float MediumSpreadFraction = 0.33f;

    /// <summary>Filled fraction required to use <see cref="MaximumSpreadRange"/>.</summary>
    [DataField] public float MaximumSpreadFraction = 0.67f;

    /// <summary>Largest diamond radius produced by a sufficiently full bottle.</summary>
    [DataField] public int MaximumSpreadRange = 2;

    /// <summary>Impact sound played when the bottle breaks and releases its contents.</summary>
    [DataField] public SoundSpecifier BreakSound = new SoundPathSpecifier("/Audio/_Misfits/Effects/molotov.ogg");

    /// <summary>Ordinary glass break used when the wick was never ignited.</summary>
    [DataField] public SoundSpecifier UnlitBreakSound = new SoundCollectionSpecifier("GlassBreak");

    /// <summary>Sound played when the wick is successfully ignited.</summary>
    [DataField] public SoundSpecifier IgniteSound = new SoundPathSpecifier("/Audio/_Misfits/Effects/molotov_light.ogg");

    /// <summary>Replicated so clients can switch the wick to its burning animation.</summary>
    [DataField, AutoNetworkedField] public bool Ignited;

    /// <summary>Server-side guard against both landing and hit events breaking the bottle.</summary>
    public bool Broken;
}
