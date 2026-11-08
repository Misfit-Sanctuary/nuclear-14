using Robust.Shared.Prototypes;
using Content.Shared.Maps;
using Content.Shared.Tag;

namespace Content.Shared.Procedural.DungeonGenerators;

/// <summary>
/// Places rooms in pre-selected pack layouts. Chooses rooms from the specified whitelist.
/// </summary>
public sealed partial class PrefabDunGen : IDunGen
{
    /// <summary>
    /// Rooms need to match any of these tags
    /// </summary>
    [DataField("roomWhitelist")]
    public List<ProtoId<TagPrototype>> RoomWhitelist = new();

    /// <summary>
    /// Room pack presets we can use for this prefab.
    /// </summary>
    [DataField("presets", required: true)]
    public List<ProtoId<DungeonPresetPrototype>> Presets = new();

    /// <summary>
    /// Fallback tile.
    /// </summary>
    [DataField("tile")]
    public ProtoId<ContentTileDefinition> Tile = "FloorSteel";
}
