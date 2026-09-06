// #Misfits Change - Wasteland Map Viewer
using Robust.Shared.GameStates;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;
using Content.Shared._Misfits.Overwatch;

namespace Content.Shared._Misfits.WastelandMap;

/// <summary>
/// UI key for the wasteland map viewer interface.
/// </summary>
[Serializable, NetSerializable]
public enum WastelandMapUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public enum WastelandMapTrackedBlipKind : byte
{
    Unknown,
    Elder,
    Paladin,
    Knight,
    Scribe,
    Squire,
    Willower, // #Misfits Add - Willower identification item marker
    // #Misfits Add - Legion rank blip kinds for the Centurion tactical computer
    LegionCenturion,  // Centurion & Legate — gold star marker
    LegionDecanus,    // Decanus ranks — red diamond
    LegionWarrior,    // Warriors, specialists — dark red square
    LegionRecruit,    // Recruit / Auxilia / Slave — brown circle
    // #Misfits Add - PipBoy Hub contact/group blips for wasteland map integration
    PipBoyContact,    // PipBoy contact sharing location — cyan circle
    PipBoyGroupMember, // PipBoy group member with map tracking — teal triangle
    GroupRallyPoint,   // Shared group rally point — amber diamond
    TribalHuntTarget, // Active tribal hunt Deathclaw target
    // #Misfits Add - Followers of the Apocalypse dead body blip
    DeadBody,          // deceased player-controlled entity shown on Followers tac-map
    CriticalSoul,      // critical player-mind entity shown only on Bwonsamdi's soul compass
    DeadSoul,          // dead player-mind entity shown only on Bwonsamdi's soul compass
    TribeCritical,     // Tribe member in critical condition during the Rite of Returning
    MaterialExtractor, // permanent seismic extractor landmark
    ExpeditionEntrance, // round-scoped unknown underground expedition entrance
}

[Serializable, NetSerializable]
public enum WastelandMapAnnotationType : byte
{
    Marker,
    Box,
    Draw,
}

[Serializable, NetSerializable]
public enum WastelandMapTacticalFeedKind : byte
{
    None,
    Brotherhood,
    Vault,
    NCR,
    Enclave, // #Misfits Change
    Legion, // #Misfits Add - Legion faction tactical feed for Centurion's terminal
    // #Misfits Add - Followers of the Apocalypse feed: shows all dead player bodies
    Followers,
    Tribe, // #Misfits Add - Willower pendant tactical feed
    Bwonsamdi, // private map-wide feed of critical and dead player souls
}

[Serializable, NetSerializable]
public readonly record struct WastelandMapTrackedBlip(float X, float Y, string Label, WastelandMapTrackedBlipKind Kind);

[Serializable, NetSerializable]
public enum WastelandMapCommunicationsChannelKind : byte
{
    Faction,
    Wasteland,
}

[Serializable, NetSerializable]
public readonly record struct WastelandMapCommunicationsEntry(
    NetEntity Target,
    string Name,
    string? JobTitle,
    bool HasFactionHeadset,
    bool FactionRevoked,
    bool HasWastelandHeadset,
    bool WastelandRevoked);

[Serializable, NetSerializable]
public sealed class WastelandMapCommunicationsState
{
    public readonly string FactionChannelId;
    public readonly string FactionChannelName;
    public readonly string WastelandChannelId;
    public readonly string WastelandChannelName;
    public readonly bool CanManage;
    public readonly WastelandMapCommunicationsEntry[] Personnel;

    public WastelandMapCommunicationsState(
        string factionChannelId,
        string factionChannelName,
        string wastelandChannelId,
        string wastelandChannelName,
        bool canManage,
        WastelandMapCommunicationsEntry[]? personnel = null)
    {
        FactionChannelId = factionChannelId;
        FactionChannelName = factionChannelName;
        WastelandChannelId = wastelandChannelId;
        WastelandChannelName = wastelandChannelName;
        CanManage = canManage;
        Personnel = personnel ?? [];
    }
}

[Serializable, NetSerializable]
public readonly record struct WastelandMapAnnotation(
    WastelandMapAnnotationType Type,
    float StartX,
    float StartY,
    float EndX,
    float EndY,
    string Label,
    /// <summary>RGBA8 packed color: (R&lt;&lt;24)|(G&lt;&lt;16)|(B&lt;&lt;8)|A. Default is orange.</summary>
    uint PackedColor,
    /// <summary>Stroke width in screen pixels (1–12). Applies to Draw, Marker, and Box borders.</summary>
    float StrokeWidth,
    /// <summary>For Draw type: interleaved [x0,y0,x1,y1,...] UV coords. Null for Marker/Box.</summary>
    float[]? StrokePoints)
{
    public const uint DefaultPackedColor = 0xF27F26FF;
    public const float DefaultStrokeWidth = 3f;
}

/// <summary>
/// BUI state sent from server to client when the map UI is opened.
/// Uses primitive floats because Box2 is not [NetSerializable].
/// </summary>
[Serializable, NetSerializable]
public sealed class WastelandMapBoundUserInterfaceState : BoundUserInterfaceState
{
    public readonly string MapTitle;
    public readonly string MapTexturePath;
    public readonly bool CompactHud;
    // World-space bounds as separate floats (left, bottom, right, top).
    public readonly float BoundsLeft;
    public readonly float BoundsBottom;
    public readonly float BoundsRight;
    public readonly float BoundsTop;
    public readonly WastelandMapTrackedBlip[] TrackedBlips;
    public readonly WastelandMapAnnotation[] SharedAnnotations;
    public readonly OverwatchConsoleState? Overwatch;
    public readonly WastelandMapCommunicationsState? Communications;

    public WastelandMapBoundUserInterfaceState(string mapTitle, string mapTexturePath,
        bool compactHud,
        float boundsLeft, float boundsBottom, float boundsRight, float boundsTop,
        WastelandMapTrackedBlip[]? trackedBlips = null,
        WastelandMapAnnotation[]? sharedAnnotations = null,
        OverwatchConsoleState? overwatch = null,
        WastelandMapCommunicationsState? communications = null)
    {
        MapTitle = mapTitle;
        MapTexturePath = mapTexturePath;
        CompactHud = compactHud;
        BoundsLeft = boundsLeft;
        BoundsBottom = boundsBottom;
        BoundsRight = boundsRight;
        BoundsTop = boundsTop;
        TrackedBlips = trackedBlips ?? [];
        SharedAnnotations = sharedAnnotations ?? [];
        Overwatch = overwatch;
        Communications = communications;
    }
}

[Serializable, NetSerializable]
public sealed class WastelandMapAddAnnotationMessage : BoundUserInterfaceMessage
{
    public readonly WastelandMapAnnotation Annotation;

    public WastelandMapAddAnnotationMessage(WastelandMapAnnotation annotation)
    {
        Annotation = annotation;
    }
}

[Serializable, NetSerializable]
public sealed class WastelandMapClearAnnotationsMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class WastelandMapRemoveAnnotationMessage : BoundUserInterfaceMessage
{
    public readonly int Index;

    public WastelandMapRemoveAnnotationMessage(int index)
    {
        Index = index;
    }
}

[Serializable, NetSerializable]
public sealed class WastelandMapCommunicationsMessage : BoundUserInterfaceMessage
{
    public readonly NetEntity Target;
    public readonly WastelandMapCommunicationsChannelKind ChannelKind;
    public readonly bool Revoke;

    public WastelandMapCommunicationsMessage(
        NetEntity target,
        WastelandMapCommunicationsChannelKind channelKind,
        bool revoke)
    {
        Target = target;
        ChannelKind = channelKind;
        Revoke = revoke;
    }
}

/// <summary>
/// Component that marks an entity as a wasteland map viewer.
/// Displays a static map image when used.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WastelandMapComponent : Component
{
    /// <summary>
    /// Path to the map texture to display, relative to Resources/Textures/.
    /// If not set, the system will resolve it from the MapConfigId prototype.
    /// </summary>
    [DataField, AutoNetworkedField]
    public ResPath? MapTexturePath;

    /// <summary>
    /// Title displayed on the map window.
    /// </summary>
    [DataField, AutoNetworkedField]
    public string MapTitle = "Map";

    /// <summary>
    /// The world-space tile bounds (left, bottom, right, top) that the map image covers.
    /// Used server-side to populate the BUI state. NOT AutoNetworkedField because Box2
    /// is not [NetSerializable] in RobustToolbox.
    /// If left at default (zero), the system will auto-detect bounds from the
    /// entity's current map grid(s), or resolve from MapConfigId.
    /// </summary>
    [DataField]
    public Box2 WorldBounds = default;

    /// <summary>
    /// Optional reference to a WastelandMapConfig prototype. When set, the system
    /// resolves MapTexturePath and WorldBounds from the named config at runtime,
    /// allowing tactical maps to work across different game maps without hardcoding
    /// per-map values. Falls back to the component's own values if the config is
    /// not found or these fields are already set.
    /// Example values: "Wendover", "Vault", "Sunnyvale"
    /// </summary>
    [DataField]
    public string? MapConfigId;

    /// <summary>
    /// If true, the server streams live positions for Brotherhood holotag entities
    /// on the same map into the tactical map UI as tracked blips.
    /// </summary>
    [DataField]
    public bool TrackBrotherhoodHolotags;

    /// <summary>
    /// Shared tactical feed for tracked IDs and synchronized annotations.
    /// Brotherhood and Vault viewers can share the same layer across tables,
    /// Pip-Boys, and armor HUDs.
    /// </summary>
    [DataField]
    public WastelandMapTacticalFeedKind TacticalFeed;

    // #Misfits Add - optional leadership-only Tree map activation.
    [DataField]
    public HashSet<string>? ActivatorJobs;

    /// <summary>
    /// Jobs allowed to revoke and restore the faction radio encryption for online members.
    /// If unset, communications management is disabled for this map.
    /// </summary>
    [DataField]
    public HashSet<string>? CommunicationsJobs;

    /// <summary>
    /// If true, the UI hides the annotation toolbar and uses a smaller HUD-style layout.
    /// </summary>
    [DataField]
    public bool CompactHud;

    /// <summary>
    /// Shared tactical annotations synchronized to all viewers of this map.
    /// Stored in normalized UV space so they remain aligned with the rendered image.
    /// </summary>
    [DataField]
    public List<WastelandMapAnnotation> SharedAnnotations = new();
}

