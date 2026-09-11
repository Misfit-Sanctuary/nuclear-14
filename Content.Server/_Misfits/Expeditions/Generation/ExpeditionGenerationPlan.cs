using System.Collections.Generic;
using Content.Shared._Misfits.Expeditions;

namespace Content.Server._Misfits.Expeditions.Generation;

/// <summary>
/// A semantic room that must be realized by the geometry pass. Keeping this
/// separate from <see cref="RoomDef"/> lets validation reject a bad layout
/// before any tiles or entities are spawned.
/// </summary>
public sealed class PlannedExpeditionRoom
{
    public required string Id { get; init; }
    public required RoomType RoomType { get; init; }
    public required ZoneRole ZoneRole { get; init; }
    public bool Required { get; init; } = true;
    public int SecurityLevel { get; init; }
    public bool IsObjective { get; init; }
    public int FactionIndex { get; init; } = -1;
}

/// <summary>
/// A deliberate relationship between two rooms. Corridors are carved from
/// these edges instead of inferring the ruin's meaning from a nearest-room MST.
/// </summary>
public sealed class PlannedExpeditionConnection
{
    public required string From { get; init; }
    public required string To { get; init; }
    public bool Required { get; init; } = true;
}

public sealed class ExpeditionRuinIdentity
{
    public required string SiteType { get; init; }
    public required string FailureCause { get; init; }
    public required string CurrentState { get; init; }
}

public sealed class ExpeditionGenerationPlan
{
    public required int Seed { get; init; }
    public required UndergroundTheme Theme { get; init; }
    public required ExpeditionRuinIdentity Identity { get; init; }
    public required string EntryRoomId { get; init; }
    public required string ObjectiveRoomId { get; init; }
    public List<PlannedExpeditionRoom> Rooms { get; } = new();
    public List<PlannedExpeditionConnection> Connections { get; } = new();
}

public sealed class ExpeditionPlanValidationResult
{
    public List<string> Errors { get; } = new();
    public bool IsValid => Errors.Count == 0;
}
