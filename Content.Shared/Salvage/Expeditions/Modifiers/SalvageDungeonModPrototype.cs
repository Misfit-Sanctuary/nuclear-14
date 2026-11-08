using Content.Shared.Procedural;
using Robust.Shared.Prototypes;

namespace Content.Shared.Salvage.Expeditions.Modifiers;

[Prototype("salvageDungeonMod")]
public sealed partial class SalvageDungeonModPrototype : IPrototype, IBiomeSpecificMod
{
    [IdDataField] public string ID { get; set; } = default!;

    [DataField("desc")] public string Description { get; private set; } = string.Empty;

    /// <inheridoc/>
    [DataField("cost")]
    public float Cost { get; private set; } = 0f;

    /// <inheridoc/>
    [DataField("biomes")]
    public List<string>? Biomes { get; private set; } = null;

    /// <summary>
    /// The config to use for spawning the dungeon.
    /// </summary>
    [DataField("proto", required: true)]
    public ProtoId<DungeonConfigPrototype> Proto = string.Empty;
}
