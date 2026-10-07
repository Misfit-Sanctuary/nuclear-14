// #Misfits Add - Doomarm: a super mutant replacement arm, attached by surgery in place of a real arm.
// Punches like a power fist and grants Kinetic Discharge, Groundbreaker and Orbital Descent.

using Content.Shared.Damage;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Speech;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Misfits.Doomarm;

/// <summary>
/// On the Doomarm arm part. While it's attached to a body (and not crippled) the body gets the
/// abilities and a locked <see cref="DoomarmFistComponent"/> item in the gloves slot.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class DoomarmComponent : Component
{
    /// <summary>
    /// Species it can be attached to (the art is drawn for the super mutant body).
    /// </summary>
    [DataField]
    public List<ProtoId<SpeciesPrototype>> AllowedSpecies = new() { "SuperMutant", "Nightkin" };

    /// <summary>
    /// The punching "glove" put in the gloves slot while attached.
    /// </summary>
    [DataField]
    public EntProtoId Fist = "MisfitsDoomarmFist";

    /// <summary>
    /// The fist currently in the wearer's gloves slot. Server only.
    /// </summary>
    public EntityUid? FistEntity;

    // ── Actions ──────────────────────────────────────────────────────────

    [DataField]
    public EntProtoId LungeAction = "ActionDoomarmLunge";

    [DataField, AutoNetworkedField]
    public EntityUid? LungeActionEntity;

    [DataField]
    public EntProtoId SlamAction = "ActionDoomarmSlam";

    [DataField, AutoNetworkedField]
    public EntityUid? SlamActionEntity;

    [DataField]
    public EntProtoId DescentAction = "ActionDoomarmDescent";

    [DataField, AutoNetworkedField]
    public EntityUid? DescentActionEntity;

    // ── Overheat ─────────────────────────────────────────────────────────

    /// <summary>
    /// Heat at <see cref="HeatUpdatedAt"/>. Current heat is this minus cooling since then.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Heat;

    [DataField, AutoNetworkedField]
    public TimeSpan HeatUpdatedAt;

    [DataField]
    public float CoolingPerSecond = 5f;

    [DataField]
    public float OverheatThreshold = 100f;

    [DataField]
    public DamageSpecifier OverheatDamage = new() { DamageDict = { ["Heat"] = 15 } };

    [DataField]
    public SoundSpecifier OverheatSound = new SoundPathSpecifier("/Audio/Effects/lightburn.ogg");

    // ── Kinetic Discharge (lunge) ────────────────────────────────────────

    [DataField]
    public float LungeChargeTime = 1f;

    [DataField]
    public float LungeRange = 6f;

    [DataField]
    public float LungeSpeed = 15f;

    [DataField]
    public float LungeHeat = 35f;

    [DataField]
    public DamageSpecifier LungeDamage = new() { DamageDict = { ["Blunt"] = 25 } };

    /// <summary>
    /// How far the person you hit gets thrown, in tiles.
    /// </summary>
    [DataField]
    public float LungeKnockbackDistance = 4f;

    [DataField]
    public float LungeKnockbackSpeed = 10f;

    [DataField]
    public SoundSpecifier LungeChargeSound = new SoundPathSpecifier("/Audio/Items/Defib/defib_charge.ogg");

    [DataField]
    public SoundSpecifier LungeHitSound = new SoundCollectionSpecifier("MetalThud");

    // ── Groundbreaker (leap + 3x3 slam where you land) ───────────────────

    /// <summary>
    /// How long the leap takes, in seconds. The slam happens when it ends.
    /// </summary>
    [DataField]
    public float HopTime = 0.5f;

    /// <summary>
    /// Furthest the leap goes, in tiles. Clicking further away still leaps this far in that direction.
    /// </summary>
    [DataField]
    public float HopRange = 4f;

    [DataField]
    public float SlamHeat = 40f;

    /// <summary>
    /// Tiles out from the user's tile. 1 = a 3x3 square.
    /// </summary>
    [DataField]
    public int SlamRadius = 1;

    [DataField]
    public DamageSpecifier SlamDamage = new() { DamageDict = { ["Blunt"] = 20 } };

    [DataField]
    public float SlamSlowTime = 3f;

    [DataField]
    public float SlamSlowMultiplier = 0.5f;

    // ── Orbital Descent (up a z-level, crash down 5x5) ───────────────────

    [DataField]
    public float AirTime = 2f;

    [DataField]
    public float DescentHeat = 60f;

    /// <summary>
    /// Blunt damage by ring: index 0 = the centre tile, 1 = the ring around it, 2 = the outer ring (5x5).
    /// </summary>
    [DataField]
    public List<float> DescentRingDamage = new() { 45f, 30f, 15f };

    [DataField]
    public DamageSpecifier CeilingDamage = new() { DamageDict = { ["Blunt"] = 10 } };

    [DataField]
    public float ShakeStrength = 6f;

    [DataField]
    public SoundSpecifier LaunchSound = new SoundPathSpecifier("/Audio/Effects/podwoosh.ogg");

    // ── Shared by the slams ──────────────────────────────────────────────

    [DataField]
    public SoundSpecifier ImpactSound = new SoundCollectionSpecifier("MetalSlam");

    [DataField]
    public EntProtoId ImpactEffect = "VertibirdCombatDropImpactEffect";

    [DataField]
    public EntProtoId LandingMarker = "MisfitsDoomarmLandingMarker";

    // ── Shouts (like the warcry) ─────────────────────────────────────────

    [DataField]
    public ProtoId<SpeechVerbPrototype> ShoutVerb = "MisfitsTribalWarcry";

    /// <summary>
    /// Each ability picks one of this many lines, e.g. doomarm-shout-lunge-1..3.
    /// </summary>
    [DataField]
    public int ShoutVariants = 3;
}

/// <summary>
/// The invisible punching "glove" a Doomarm puts in the gloves slot. It carries the power-fist
/// melee stats and keeps the slot locked. Deleted when the Doomarm comes off.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class DoomarmFistComponent : Component;

/// <summary>
/// On the Doomarm arm and hand parts. Mutant limbs are tinted with the skin colour; this keeps the
/// metal art its own colour while attached.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class DoomarmPlatingComponent : Component;
