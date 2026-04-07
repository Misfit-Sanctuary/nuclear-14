using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

public sealed partial class CCVars
{
    public static readonly CVarDef<int> NPCMaxUpdates =
        CVarDef.Create("npc.max_updates", 512); // #Misfits Tweak: Reduced from 2048 to 512 for performance

    public static readonly CVarDef<bool> NPCEnabled = CVarDef.Create("npc.enabled", true);

    /// <summary>
    ///     Should NPCs pathfind when steering. For debug purposes.
    /// </summary>
    public static readonly CVarDef<bool> NPCPathfinding = CVarDef.Create("npc.pathfinding", true);

    /// <summary>
    ///     #Misfits Add: Pathfinding time limit per frame in milliseconds. Higher values reduce stuttering but may impact performance.
    /// </summary>
    public static readonly CVarDef<int> NPCPathfindingTimeMs = CVarDef.Create("npc.pathfinding_time_ms", 3);

    /// <summary>
    ///     #Misfits Add: Override for juke cooldown timing. -1 uses default behavior, >0 overrides all juke cooldowns.
    /// </summary>
    public static readonly CVarDef<float> NPCJukeCooldownOverride = CVarDef.Create("npc.juke_cooldown_override", -1f);

    /// <summary>
    ///     #Misfits Add: Enable debug logging for NPC performance metrics.
    /// </summary>
    public static readonly CVarDef<bool> NPCDebugPerformance = CVarDef.Create("npc.debug_performance", false);
}
