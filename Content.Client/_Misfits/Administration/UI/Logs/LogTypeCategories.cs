// #Misfits Add - Static mapping of LogType → human-readable categories for the admin logs
// category rail. Each LogType is assigned exactly one category. Unknown/unmapped types
// fall back to "Other". Used by AdminLogsControl to build collapsible category groups
// and by AdminLogRow to pick the badge color.

using Content.Shared.Database;

namespace Content.Client._Misfits.Administration.UI.Logs;

public static class LogTypeCategories
{
    /// <summary>
    /// Ordered list of all category names, in the order they should appear in the rail.
    /// </summary>
    public static readonly string[] AllCategories =
    {
        "Combat",
        "Damage",
        "Interaction",
        "Atmos",
        "Chat",
        "Admin",
        "Construction",
        "Events",
        "Movement",
        "Anomaly",
        "Science",
        "Other",
    };

    private static readonly Dictionary<LogType, string> Map = new()
    {
        // Combat
        { LogType.AttackArmedClick, "Combat" },
        { LogType.AttackArmedWide, "Combat" },
        { LogType.AttackUnarmedClick, "Combat" },
        { LogType.AttackUnarmedWide, "Combat" },
        { LogType.BulletHit, "Combat" },
        { LogType.MeleeHit, "Combat" },
        { LogType.HitScanHit, "Combat" },
        { LogType.DisarmedAction, "Combat" },
        { LogType.DisarmedKnockdown, "Combat" },
        { LogType.Throw, "Combat" },
        { LogType.ThrowHit, "Combat" },
        { LogType.Stamina, "Combat" },

        // Damage
        { LogType.Damaged, "Damage" },
        { LogType.Healed, "Damage" },
        { LogType.Explosion, "Damage" },
        { LogType.Barotrauma, "Damage" },
        { LogType.Electrocution, "Damage" },
        { LogType.Flammable, "Damage" },
        { LogType.Radiation, "Damage" },
        { LogType.Temperature, "Damage" },
        { LogType.Gib, "Damage" },

        // Interaction
        { LogType.InteractHand, "Interaction" },
        { LogType.InteractActivate, "Interaction" },
        { LogType.InteractUsing, "Interaction" },
        { LogType.Pickup, "Interaction" },
        { LogType.Drop, "Interaction" },
        { LogType.Landed, "Interaction" },
        { LogType.ForceFeed, "Interaction" },
        { LogType.Ingestion, "Interaction" },
        { LogType.Stripping, "Interaction" },
        { LogType.Storage, "Interaction" },
        { LogType.RMCHolster, "Interaction" },
        { LogType.Trigger, "Interaction" },
        { LogType.Anchor, "Interaction" },
        { LogType.Unanchor, "Interaction" },

        // Atmos
        { LogType.CanisterValve, "Atmos" },
        { LogType.CanisterPressure, "Atmos" },
        { LogType.CanisterPurged, "Atmos" },
        { LogType.CanisterTankEjected, "Atmos" },
        { LogType.CanisterTankInserted, "Atmos" },
        { LogType.AtmosPressureChanged, "Atmos" },
        { LogType.AtmosPowerChanged, "Atmos" },
        { LogType.AtmosVolumeChanged, "Atmos" },
        { LogType.AtmosFilterChanged, "Atmos" },
        { LogType.AtmosRatioChanged, "Atmos" },
        { LogType.AtmosTemperatureChanged, "Atmos" },
        { LogType.ExplosiveDepressurization, "Atmos" },
        { LogType.Asphyxiation, "Atmos" },

        // Chat
        { LogType.Chat, "Chat" },
        { LogType.ChatRateLimited, "Chat" },

        // Admin
        { LogType.AdminMessage, "Admin" },
        { LogType.Vote, "Admin" },
        { LogType.Verb, "Admin" },
        { LogType.Respawn, "Admin" },
        { LogType.EntitySpawn, "Admin" },
        { LogType.EntityDelete, "Admin" },
        { LogType.Mind, "Admin" },
        { LogType.GhostRoleTaken, "Admin" },
        { LogType.Identity, "Admin" },
        { LogType.RateLimited, "Admin" },

        // Construction
        { LogType.Construction, "Construction" },
        { LogType.RCD, "Construction" },
        { LogType.CableCut, "Construction" },
        { LogType.LatticeCut, "Construction" },
        { LogType.Tile, "Construction" },
        { LogType.CrayonDraw, "Construction" },
        { LogType.WireHacking, "Construction" },
        { LogType.DeviceLinking, "Construction" },
        { LogType.DeviceNetwork, "Construction" },
        { LogType.FieldGeneration, "Construction" },
        { LogType.ItemConfigure, "Construction" },
        { LogType.StorePurchase, "Construction" },
        { LogType.StoreRefund, "Construction" },

        // Events
        { LogType.EventAnnounced, "Events" },
        { LogType.EventStarted, "Events" },
        { LogType.EventRan, "Events" },
        { LogType.EventStopped, "Events" },
        { LogType.ShuttleCalled, "Events" },
        { LogType.ShuttleRecalled, "Events" },
        { LogType.EmergencyShuttle, "Events" },

        // Movement
        { LogType.Slip, "Movement" },
        { LogType.Teleport, "Movement" },
        { LogType.RoundStartJoin, "Movement" },
        { LogType.LateJoin, "Movement" },

        // Anomaly
        { LogType.Anomaly, "Anomaly" },
        { LogType.BagOfHolding, "Anomaly" },
        { LogType.Psionics, "Anomaly" },

        // Science
        { LogType.ChemicalReaction, "Science" },
        { LogType.ReagentEffect, "Science" },

        // Other — explicit entries for completeness; anything not listed also maps here
        { LogType.Hunger, "Other" },
        { LogType.Thirst, "Other" },
        { LogType.Action, "Other" },
        { LogType.Emag, "Other" },
    };

    /// <summary>
    /// Returns the category name for a given LogType. Defaults to "Other" if unmapped.
    /// </summary>
    public static string GetCategory(LogType type)
    {
        return Map.GetValueOrDefault(type, "Other");
    }

    /// <summary>
    /// Returns all LogTypes that belong to a given category.
    /// </summary>
    public static List<LogType> GetTypesForCategory(string category)
    {
        var result = new List<LogType>();
        foreach (var (type, cat) in Map)
        {
            if (cat == category)
                result.Add(type);
        }

        // For "Other", also include any enum values not explicitly mapped
        if (category == "Other")
        {
            foreach (var type in Enum.GetValues<LogType>())
            {
                if (type == LogType.Unknown)
                    continue;
                if (!Map.ContainsKey(type) && !result.Contains(type))
                    result.Add(type);
            }
        }

        result.Sort((a, b) => string.Compare(a.ToString(), b.ToString(), StringComparison.Ordinal));
        return result;
    }
}
