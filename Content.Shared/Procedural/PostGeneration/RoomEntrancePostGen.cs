using Content.Shared.Maps;
using Robust.Shared.Prototypes;

namespace Content.Shared.Procedural.PostGeneration;

/// <summary>
/// Places tiles / entities onto room entrances.
/// </summary>
public sealed partial class RoomEntrancePostGen : IPostDunGen
{
    [DataField("entities")]
    public List<EntProtoId> Entities = new()
    {
        "CableApcExtension",
        "AirlockGlass",
    };

    [DataField("tile")]
    public ProtoId<ContentTileDefinition> Tile = "FloorSteel";
}
