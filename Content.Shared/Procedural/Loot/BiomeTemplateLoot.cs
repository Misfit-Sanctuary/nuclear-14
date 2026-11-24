using Robust.Shared.Prototypes;
using Content.Shared.Parallax.Biomes;

namespace Content.Shared.Procedural.Loot;

/// <summary>
/// Adds a biome template layer for dungeon loot.
/// </summary>
public sealed partial class BiomeTemplateLoot : IDungeonLoot
{
    [DataField("proto", required: true)]
    public ProtoId<BiomeTemplatePrototype> Prototype = string.Empty;
}
