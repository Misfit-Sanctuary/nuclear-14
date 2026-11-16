using Content.Shared.Maps;
using Robust.Shared.Prototypes;

namespace Content.Shared.Procedural.PostGeneration;

/// <summary>
/// Selects [count] rooms and places external doors to them.
/// </summary>
public sealed partial class DungeonEntrancePostGen : IPostDunGen
{
    /// <summary>
    /// How many rooms we place doors on.
    /// </summary>
    [DataField("count")]
    public int Count = 1;

    [DataField("entities")]
    public List<EntProtoId> Entities = new()
    {
        "CableApcExtension",
        "AirlockGlass",
    };

    [DataField("tile")]
    public ProtoId<ContentTileDefinition> Tile = "FloorSteel";
}
