using Content.Shared.Maps;
using Robust.Shared.Prototypes;

namespace Content.Shared.Procedural.PostGeneration;

/// <summary>
/// If external areas are found will try to generate windows.
/// </summary>
public sealed partial class ExternalWindowPostGen : IPostDunGen
{
    [DataField("entities")]
    public List<EntProtoId> Entities = new()
    {
        "Grille",
        "Window",
    };

    [DataField("tile")]
    public ProtoId<ContentTileDefinition> Tile = "FloorSteel";
}
