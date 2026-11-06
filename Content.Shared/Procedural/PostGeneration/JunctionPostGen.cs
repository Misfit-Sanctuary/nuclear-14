using Content.Shared.Maps;
using Robust.Shared.Prototypes;

namespace Content.Shared.Procedural.PostGeneration;

/// <summary>
/// Places the specified entities at junction areas.
/// </summary>
public sealed partial class JunctionPostGen : IPostDunGen
{
    /// <summary>
    /// Width to check for junctions.
    /// </summary>
    [DataField("width")]
    public int Width = 3;

    [DataField("tile")]
    public ProtoId<ContentTileDefinition> Tile = "FloorSteel";

    [DataField("entities")]
    public List<EntProtoId> Entities = new()
    {
        "CableApcExtension",
        "AirlockGlass"
    };
}
