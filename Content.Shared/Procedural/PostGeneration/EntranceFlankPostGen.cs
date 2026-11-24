using Robust.Shared.Prototypes;
using Content.Shared.Maps;

namespace Content.Shared.Procedural.PostGeneration;

/// <summary>
/// Spawns entities on either side of an entrance.
/// </summary>
public sealed partial class EntranceFlankPostGen : IPostDunGen
{
    [DataField("tile")]
    public ProtoId<ContentTileDefinition> Tile = "FloorSteel";

    [DataField("entities")]
    public List<string> Entities = new();
}
