// #Misfits Add - Grant components that give an entity a martial arts style on startup or use-in-hand
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Shared._Misfits.MartialArts;

/// <summary>
/// Abstract base for all "GrantMartialArt" components.
/// Attach to an entity (item) that, when used in hand or held on spawn, grants the user a martial art.
/// </summary>
public abstract partial class GrantMartialArtKnowledgeComponent : Component
{
    /// <summary>Which form to grant.</summary>
    [DataField]
    public virtual MisfitsMartialArtsForms MartialArtsForm { get; set; }

    /// <summary>Locale string shown as a popup when the art is learned.</summary>
    [DataField]
    public virtual LocId? LearnMessage { get; set; }

    /// <summary>If true, the item is not consumed and can teach multiple users.</summary>
    [DataField]
    public bool MultiUse;

    /// <summary>Entity prototype to spawn in place of the consumed item (e.g. "Ash" for a burned scroll).</summary>
    [DataField]
    public string? SpawnedProto;

    /// <summary>Sound played when the art is successfully learned.</summary>
    [DataField]
    public SoundSpecifier? SoundOnUse;

    /// <summary>The combo list ID this style starts with.</summary>
    [DataField]
    public ProtoId<MisfitsComboListPrototype>? RoundstartCombos;
}

// ---- Concrete grant components for each style ----

/// <summary>Grants Legion Gladiatorial combat. Assigned to Centurion and Legate via AddComponentSpecial.</summary>
[RegisterComponent]
public sealed partial class GrantLegionGladiatorialComponent : GrantMartialArtKnowledgeComponent
{
    public override MisfitsMartialArtsForms MartialArtsForm { get; set; } = MisfitsMartialArtsForms.LegionGladiatorial;
    public override LocId? LearnMessage { get; set; } = "martial-arts-learn-legion";
}

/// <summary>Grants Ranger Combat Technique. Assigned to Veteran Ranger and Ranger Chief via AddComponentSpecial.</summary>
[RegisterComponent]
public sealed partial class GrantRangerCombatComponent : GrantMartialArtKnowledgeComponent
{
    public override MisfitsMartialArtsForms MartialArtsForm { get; set; } = MisfitsMartialArtsForms.RangerCombatTechnique;
    public override LocId? LearnMessage { get; set; } = "martial-arts-learn-ranger";
}

/// <summary>Grants Desert Survival Fighting. Learned via Training Manual item.</summary>
[RegisterComponent]
public sealed partial class GrantDesertSurvivalComponent : GrantMartialArtKnowledgeComponent
{
    public override MisfitsMartialArtsForms MartialArtsForm { get; set; } = MisfitsMartialArtsForms.DesertSurvivalFighting;
    public override LocId? LearnMessage { get; set; } = "martial-arts-learn-desert-survival";
}

/// <summary>Grants Wasteland Street Fighting. Learned via Training Manual item.</summary>
[RegisterComponent]
public sealed partial class GrantWastelandStreetFightingComponent : GrantMartialArtKnowledgeComponent
{
    public override MisfitsMartialArtsForms MartialArtsForm { get; set; } = MisfitsMartialArtsForms.WastelandStreetFighting;
    public override LocId? LearnMessage { get; set; } = "martial-arts-learn-wasteland";
}

/// <summary>Grants Tribal Warrior Style. Learned via Training Manual item.</summary>
[RegisterComponent]
public sealed partial class GrantTribalWarriorComponent : GrantMartialArtKnowledgeComponent
{
    public override MisfitsMartialArtsForms MartialArtsForm { get; set; } = MisfitsMartialArtsForms.TribalWarriorStyle;
    public override LocId? LearnMessage { get; set; } = "martial-arts-learn-tribal";
}

/// <summary>Grants Shadow Strike. Learned via Training Manual item.</summary>
[RegisterComponent]
public sealed partial class GrantShadowStrikeComponent : GrantMartialArtKnowledgeComponent
{
    public override MisfitsMartialArtsForms MartialArtsForm { get; set; } = MisfitsMartialArtsForms.ShadowStrike;
    public override LocId? LearnMessage { get; set; } = "martial-arts-learn-shadow";
}
