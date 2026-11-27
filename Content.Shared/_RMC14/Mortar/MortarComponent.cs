using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Shared._RMC14.Mortar;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
[Access(typeof(SharedMortarSystem))]
public sealed partial class MortarComponent : Component
{
    [DataField, AutoNetworkedField]
    public string ContainerId = "rmc_mortar_container";

    [DataField, AutoNetworkedField]
    public TimeSpan DeployDelay = TimeSpan.FromSeconds(4);

    /// How long it takes to set the target and the offset that goes with it, in one go.

    [DataField, AutoNetworkedField]
    public TimeSpan TargetDelay = TimeSpan.FromSeconds(3);

    [DataField, AutoNetworkedField]
    public bool Deployed;

    /// Whether the mortar has ever been given a target. Needed because (0, 0) is a valid coordinate.

    [DataField, AutoNetworkedField]
    public bool HasTarget;

    /// Absolute map tile coordinate this mortar is aimed at.

    [DataField, AutoNetworkedField]
    public Vector2i Target;

    [DataField, AutoNetworkedField]
    public Vector2i Dial;

    /// How far a round lands from the aim. Only the server rolls it, so it does not need syncing.

    [DataField]
    public MortarScatterProfile Scatter = new();

    [DataField, AutoNetworkedField]
    public int MaxTarget = 10000;

    [DataField, AutoNetworkedField]
    public int MaxDial = 10;

    [DataField, AutoNetworkedField]
    public int MinimumRange = 8;

    [DataField, AutoNetworkedField]
    public int MaximumRange = 165;

    [DataField, AutoNetworkedField]
    public string FixtureId = "mortar";

    [DataField, AutoNetworkedField]
    public TimeSpan AnimationTime = TimeSpan.FromSeconds(0.3);

    [DataField, AutoNetworkedField]
    public string AnimationLayer = "mortar";

    [DataField, AutoNetworkedField]
    public string AnimationState = "mortar_m402_fire";

    [DataField, AutoNetworkedField]
    public string DeployedState = "mortar_m402";

    [DataField, AutoNetworkedField]
    public SoundSpecifier? DeploySound = new SoundPathSpecifier("/Audio/_RMC14/Weapons/gun_mortar_unpack.ogg");

    [DataField, AutoNetworkedField]
    public SoundSpecifier? ReloadSound = new SoundPathSpecifier("/Audio/_RMC14/Weapons/gun_mortar_reload.ogg", AudioParams.Default.WithVariation(0.4f));

    [DataField, AutoNetworkedField]
    public SoundSpecifier? FireSound = new SoundPathSpecifier("/Audio/_RMC14/Weapons/gun_mortar_fire.ogg", AudioParams.Default.AddVolume(4));

    [DataField, AutoNetworkedField]
    public EntProtoId Drop = "RMCMortarKit";

    /// Camera left by the last illumination round this mortar fired, if it still exists.

    [DataField, AutoNetworkedField]
    public EntityUid? ImpactCamera;
}

/// Where a shell lands relative to the aim: the further the shot, the wider the spread.
[DataDefinition]
public sealed partial class MortarScatterProfile
{
    /// Tiles of distance from the aim that buy one tile of possible deflection.

    [DataField]
    public float TilesPerOffset = 40f;

    /// Weight per deflection magnitude in tiles, index 0 = dead on; only reachable ones are drawn.

    [DataField]
    public List<float> OffsetWeights = new() { 5, 4, 3, 2, 1 };

    /// Furthest, in tiles, a single axis may deflect, whatever the range.

    [DataField]
    public int MaxOffset = 4;

    /// Furthest magnitude this profile can draw: the cap, limited by the weights on offer.

    private int MaxMagnitude => Math.Min(Math.Max(MaxOffset, 0), Math.Max(OffsetWeights.Count - 1, 0));

    /// How many tiles of deflection one axis's distance from the aim buys.

    public int BucketFor(float axisDistance)
    {
        var tilesPer = MathF.Max(TilesPerOffset, 1f);
        return Math.Min((int) (MathF.Abs(axisDistance) / tilesPer), MaxMagnitude);
    }

    /// Deflection magnitude for one axis, picked by weight from those this bucket reaches.

    public int MagnitudeFor(int bucket, float roll)
    {
        if (OffsetWeights.Count == 0)
            return 0;

        var last = Math.Min(bucket, MaxMagnitude);

        var sum = 0f;
        for (var i = 0; i <= last; i++)
            sum += OffsetWeights[i];

        // All weightless, or a bucket below zero, so every round lands dead on.
        if (sum <= 0f)
            return 0;

        var target = roll * sum;
        var accumulated = 0f;
        for (var i = 0; i <= last; i++)
        {
            accumulated += OffsetWeights[i];

            if (accumulated > target)
                return i;
        }

        return last;
    }
}
