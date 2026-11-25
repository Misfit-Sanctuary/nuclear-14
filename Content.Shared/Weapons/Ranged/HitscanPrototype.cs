using Content.Shared.Damage;
using Content.Shared.Physics;
using Content.Shared.Weapons.Reflect;
using Robust.Shared.Audio;

namespace Content.Shared.Weapons.Ranged;

[Prototype]
public sealed partial class HitscanPrototype : IPrototype, IShootable
{
    [ViewVariables]
    [IdDataField]
    public string ID { get; private set; } = default!;

    [ViewVariables(VVAccess.ReadWrite), DataField("staminaDamage")]
    public float StaminaDamage;

    [ViewVariables(VVAccess.ReadWrite), DataField("damage")]
    public DamageSpecifier? Damage;

    [ViewVariables(VVAccess.ReadOnly), DataField("muzzleFlash")]
    public SpriteSpecifier? MuzzleFlash;

    [ViewVariables(VVAccess.ReadOnly), DataField("travelFlash")]
    public SpriteSpecifier? TravelFlash;

    [ViewVariables(VVAccess.ReadOnly), DataField("impactFlash")]
    public SpriteSpecifier? ImpactFlash;

    // #Misfits Fix - Hitscan/laser rays must stop on glass like bullets do. Bullets use a
    // BulletImpassable mask (see BaseBullet), and glass uses GlassLayer which has no Opaque,
    // so the old Opaque-only mask made lasers fly straight through windows. Adding
    // BulletImpassable makes lasers collide with exactly what bullets collide with.
    [DataField("collisionMask", readOnly: true)]
    public int CollisionMask = (int) (CollisionGroup.Opaque | CollisionGroup.BulletImpassable);

    /// <summary>
    /// What we count as for reflection.
    /// </summary>
    [DataField("reflective", readOnly: true)]
    public ReflectType Reflective = ReflectType.Energy;

    /// <summary>
    /// Sound that plays upon the thing being hit.
    /// </summary>
    [DataField("sound", readOnly: true)]
    public SoundSpecifier? Sound;

    /// <summary>
    /// Force the hitscan sound to play rather than potentially playing the entity's sound.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField("forceSound")]
    public bool ForceSound;

    /// <summary>
    /// Try not to set this too high.
    /// </summary>
    [DataField("maxLength", readOnly: true)]
    public float MaxLength = 20f;

    // #Misfits Add: optional beam colour tint applied on the client
    [DataField("tintColor", readOnly: true)]
    public Color? TintColor;

    // #Misfits Add: vertical scale multiplier for the beam sprite (wider = thicker beam)
    [DataField("beamWidth", readOnly: true)]
    public float BeamWidth = 1f;

    // #Misfits Add: how long the beam visual stays on-screen (seconds)
    [DataField("beamDuration", readOnly: true)]
    public float BeamDuration = 0.48f;
}
