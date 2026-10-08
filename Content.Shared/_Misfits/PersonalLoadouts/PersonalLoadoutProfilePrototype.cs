using Content.Shared.Roles;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared._Misfits.PersonalLoadouts;

/// <summary>
/// A server-enforced personal appearance profile. It may apply the same visual skin to
/// every compatible power-armor job while the spawned armor retains the job's native
/// prototype and mechanics.
/// </summary>
[Prototype("personalLoadoutProfile")]
public sealed partial class PersonalLoadoutProfilePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>Authenticated account names allowed to receive this kit.</summary>
    [DataField]
    public List<string> AccountNames = new();

    /// <summary>Character profile names allowed to receive this kit.</summary>
    [DataField]
    public List<string> CharacterNames = new();

    // Legacy appearance-only power-armor entries. General equipment profiles
    // may use Equipment instead.
    [DataField]
    public List<PersonalLoadoutPowerArmorSkin> PowerArmorSkins = new();

    /// <summary>
    /// Complete equipment replacements for jobs that do not use a toggleable
    /// power-armor set. Replacements are installed into their inventory slots
    /// when the approved player spawns.
    /// </summary>
    [DataField]
    public List<PersonalLoadoutEquipmentSet> Equipment = new();
}

/// <summary>
/// A job or group of jobs that receives one personal power-armor appearance. The
/// system obtains the actual armor from the job's starting gear rather than listing
/// its armor prototype here.
/// </summary>
[DataDefinition]
public sealed partial class PersonalLoadoutPowerArmorSkin
{
    [DataField(required: true)]
    public List<ProtoId<JobPrototype>> Jobs = new();

    /// <summary>RSI used by the spawned job-issued outer armor while equipped.</summary>
    [DataField(required: true)]
    public string OuterSprite = string.Empty;

    /// <summary>RSI used by the job armor's attached helmet while equipped.</summary>
    [DataField(required: true)]
    public string HelmetSprite = string.Empty;

    /// <summary>
    /// Optional equipped visuals for the attached helmet after its RSI is replaced.
    /// Use this when the replacement RSI has different equipped-state names from
    /// the native helmet supplied by the job's power armor.
    /// </summary>
    [DataField]
    public Dictionary<string, List<PrototypeLayerData>>? HelmetClothingVisuals;
}

/// <summary>
/// A complete personal outfit for one or more jobs. Dictionary keys are the
/// standard inventory slot IDs (for example: head, jumpsuit, gloves, shoes,
/// outerClothing, and mask); values are entity prototypes to equip there.
/// </summary>
[DataDefinition]
public sealed partial class PersonalLoadoutEquipmentSet
{
    [DataField(required: true)]
    public List<ProtoId<JobPrototype>> Jobs = new();

    [DataField(required: true)]
    public Dictionary<string, EntProtoId> Replacements = new();
}
