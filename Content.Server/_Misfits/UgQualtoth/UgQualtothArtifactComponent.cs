// #Misfits Add — Component for the Ug-Qualtoth idol structure.
// Marks the entity as the altar and stores configuration for sacrifice detection.

using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Server._Misfits.UgQualtoth;

/// <summary>
/// Marks this entity as the Ug-Qualtoth idol. Tribal worshippers can pray at it
/// to accumulate devotion and power their gradual transformation into abominations.
/// </summary>
[RegisterComponent]
public sealed partial class UgQualtothArtifactComponent : Component
{
    /// <summary>
    /// Tile-distance within which a tribal player receives the proximity ambient message
    /// and the "Pray" verb becomes available.
    /// </summary>
    [DataField]
    public float ProximityRange = 5f;

    /// <summary>
    /// Maximum tile-distance from the idol that a kill counts as a blood sacrifice.
    /// </summary>
    [DataField]
    public float SacrificeRange = 7f;

    /// <summary>
    /// Devotion awarded when a humanoid (player or NPC) is killed near the idol.
    /// </summary>
    [DataField]
    public float HumanoidSacrificeDevotionGain = 20f;

    /// <summary>
    /// Devotion awarded when an animal/creature is killed near the idol.
    /// </summary>
    [DataField]
    public float AnimalSacrificeDevotionGain = 8f;

    /// <summary>
    /// Sound played when someone prays successfully.
    /// </summary>
    [DataField]
    public SoundSpecifier? PraySound = new SoundPathSpecifier("/Audio/Ambience/ambimystery.ogg");

    /// <summary>
    /// Sound played when a stage upgrade happens.
    /// </summary>
    [DataField]
    public SoundSpecifier? TransformSound = new SoundPathSpecifier("/Audio/Effects/bone_rattle.ogg");
}
