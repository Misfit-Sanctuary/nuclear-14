using Robust.Shared;
using Robust.Shared.Configuration;

namespace Content.Shared._Misfits.CCVar;

/// <summary>
/// CVars for Misfits performance systems: lag compensation, NPC proximity wake, etc.
/// </summary>
[CVarDefs]
public sealed class PerformanceCVars : CVars
{
    /// <summary>
    /// Maximum lag compensation window in milliseconds.
    /// Actions and shots from clients this many ms behind the server are still accepted,
    /// with <see cref="LagCompensationMarginTiles"/> added to their range checks.
    /// </summary>
    public static readonly CVarDef<int> LagCompensationMs =
        CVarDef.Create("misfits.lag_compensation_ms", 750, CVar.REPLICATED | CVar.SERVER);

    /// <summary>
    /// Extra range margin (tiles) applied during lag-compensated range checks when a
    /// client's last confirmed tick is behind the current server tick.
    /// </summary>
    public static readonly CVarDef<float> LagCompensationMarginTiles =
        CVarDef.Create("misfits.lag_compensation_margin_tiles", 0.35f, CVar.REPLICATED | CVar.SERVER);

    /// <summary>
    /// How often (seconds) the proximity NPC system scans for nearby players.
    /// Higher values are cheaper but increase the delay before an NPC wakes.
    /// </summary>
    public static readonly CVarDef<float> ProximityNPCCheckInterval =
        CVarDef.Create("misfits.proximity_npc_check_interval", 5f, CVar.SERVER | CVar.SERVERONLY);

    /// <summary>
    /// Whether the atmos tile simulation runs on grids. When false, the 9-phase
    /// processing loop (tile equalization, active tiles, hotspots, pipe nets,
    /// atmos devices) is skipped entirely. Breathing, temperature, and smoke
    /// continue working via the static <c>MapAtmosphereComponent</c> mixture.
    /// Reclaims ~2-3ms of tick budget on maps with no functional HVAC.
    /// </summary>
    public static readonly CVarDef<bool> AtmosSimulated =
        CVarDef.Create("misfits.atmos_simulated", false, CVar.SERVERONLY);

    /// <summary>
    /// Whether barotrauma (pressure damage) is applied to entities.
    /// When false, <c>BarotraumaSystem</c> skips all processing — no low or high
    /// pressure HP damage is ever dealt. Safe to disable on maps that use a static
    /// <c>MapAtmosphereComponent</c> rather than live atmos simulation, where tile
    /// pressure data may be stale or absent.
    /// </summary>
    public static readonly CVarDef<bool> PressureDamage =
        CVarDef.Create("misfits.pressure_damage", false, CVar.SERVERONLY);

    /// <summary>
    /// Whether respiratory suffocation (gasping, asphyxiation damage) is active.
    /// When false, <c>RespiratorSystem.Update()</c> returns early — no oxygen
    /// saturation drain, no gasp popups, and no suffocation damage are ever applied.
    /// Safe to disable alongside <c>AtmosSimulated</c> and <c>PressureDamage</c> on
    /// maps that don't need breathing mechanics.
    /// </summary>
    // #Misfits Add - CVar to disable all respiratory suffocation
    public static readonly CVarDef<bool> Suffocation =
        CVarDef.Create("misfits.suffocation", false, CVar.SERVERONLY);
}
