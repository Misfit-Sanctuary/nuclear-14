using Content.Shared.Damage;
using Robust.Shared.Audio;
/// <summary>
/// Copy paste of <see cref="DamageOnHighSpeedImpactComponent"/> with added/changed fields
/// Difference being this is to dish out some effect like damage on collision
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class EffectOnCollideComponent : Component
{
    /// <summary>
    /// main fields for damage
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public bool DamageOnCollide = true;

    [DataField("minimumSpeed"), ViewVariables(VVAccess.ReadWrite)]
    public float MinimumSpeed = 20f;

    public float MinimumImpactVelocity = 1f;
    [DataField("speedDamageFactor"), ViewVariables(VVAccess.ReadWrite)]
    public float SpeedDamageFactor = 1f;

    [DataField("damage"), ViewVariables(VVAccess.ReadWrite)]
    public DamageSpecifier Damage = default!;

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public bool CollideHardOnly = false;

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public TimeSpan CollideCooldown = TimeSpan.Zero;

    /// <summary>
    /// push force fields
    /// todo make own comp maybe
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public bool PushOnCollide = false;

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float PushForce = 1f;

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float PushSpeedScale = 1f;

    /// <summary>
    /// stun fields
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public bool StunOnCollide = false;
    [DataField("stunChance"), ViewVariables(VVAccess.ReadWrite)]
    public float StunChance = 0.25f;

    [DataField("stunMinimumDamage"), ViewVariables(VVAccess.ReadWrite)]
    public int StunMinimumDamage = 10;

    [DataField("stunSeconds"), ViewVariables(VVAccess.ReadWrite)]
    public float StunSeconds = 1f;

    [DataField("soundHit"), ViewVariables(VVAccess.ReadWrite)]
    public SoundSpecifier? SoundHit = default!;

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public TimeSpan NextHit = TimeSpan.Zero;
}
