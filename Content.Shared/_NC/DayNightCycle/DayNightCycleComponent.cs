// #Misfits Change - Reworked to use IGameTiming-based deterministic cycle (no per-frame dirty spam)
// #Misfits Change - 4-hour dawn-to-dawn cycle synced to round start: day = first half, night = second
// half, dawn ramp starts at 3h 46m so round end (4h) is dawn. Nights darkened vs. the old curve.
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._NC14.DayNightCycle
{
    [RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
    public sealed partial class DayNightCycleComponent : Component
    {
        [ViewVariables(VVAccess.ReadWrite)]
        [DataField("cycleDuration")]
        public float CycleDurationMinutes { get; set; } = 240f; // #Misfits Change - 90min -> 240min (4h round = 4h cycle)

        /// <summary>
        /// Offset into the cycle (0–1) applied at startup so the world begins partway through the cycle.
        /// 0 = start at dawn (cycle time 0), which is where each round begins.
        /// </summary>
        [DataField("startOffset")]
        [AutoNetworkedField]
        public float StartOffset { get; set; } = 0f; // #Misfits Change - 0.2 (early morning) -> 0 (dawn)

        /// <summary>
        /// Server <see cref="IGameTiming.CurTime"/> in seconds when the current round started. Set by
        /// DayNightCycleRoundStartSyncSystem on RoundStartedEvent so every round begins at cycle time 0
        /// (dawn) and the 4-hour cycle lines up exactly with the round (round end = dawn again).
        /// </summary>
        [ViewVariables(VVAccess.ReadWrite)]
        [AutoNetworkedField]
        public float RoundStartTimeSeconds { get; set; } = 0f;

        [DataField("timeEntries")]
        public List<TimeEntry> TimeEntries { get; set; } = new()
        {
            // #Misfits Change - Dawn-to-dawn curve for 4h rounds (0 = round start = dawn, 1 = round end = dawn).
            // #Cythisiax Edited - daytime was amber/piss-yellow all day
            new() { Time = 0.00f, ColorHex = "#9A8A7A" },  // Dawn (round start)
            new() { Time = 0.06f, ColorHex = "#C8BBAA" },  // Sunrise
            new() { Time = 0.15f, ColorHex = "#E0DAD0" },  // Early morning
            new() { Time = 0.25f, ColorHex = "#EDEAE4" },  // Morning
            new() { Time = 0.375f, ColorHex = "#F3F2EE" }, // Late morning
            new() { Time = 0.50f, ColorHex = "#F6F6F3" },  // Noon (peak)
            new() { Time = 0.56f, ColorHex = "#F1F0EC" },  // Early afternoon
            new() { Time = 0.63f, ColorHex = "#E2DCD2" },  // Late afternoon
            new() { Time = 0.66f, ColorHex = "#A8907A" },  // Sunset (2h 38m)
            new() { Time = 0.71f, ColorHex = "#5C4650" },  // Twilight
            new() { Time = 0.75f, ColorHex = "#372C40" },  // Night falls
            new() { Time = 0.82f, ColorHex = "#241C30" },  // Night
            new() { Time = 0.89f, ColorHex = "#151021" }, // Deep night
            new() { Time = 0.94f, ColorHex = "#2B2234" },  // Pre-dawn first light
            new() { Time = 0.965f, ColorHex = "#523A3A" }, // Dawn glow
            new() { Time = 0.985f, ColorHex = "#7E6A58" }, // Dawn // #Cythisiax Edited - less orange
            new() { Time = 1.00f, ColorHex = "#9A8A7A" }   // Full dawn (round end, wraps to start)
        };
    }

    [DataDefinition, NetSerializable, Serializable]
    public sealed partial class TimeEntry
    {
        [DataField("colorHex")]
        public string ColorHex { get; set; } = "#FFFFFF";

        [DataField("time")]
        public float Time { get; set; } // Normalized time (0-1)
    }
}
