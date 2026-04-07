// #Misfits Add - Fallout-themed martial arts style identifiers
using Robust.Shared.Serialization;

namespace Content.Shared._Misfits.MartialArts;

/// <summary>
/// The six Fallout-themed martial arts styles available in Nuclear-14.
/// </summary>
[Serializable, NetSerializable]
public enum MisfitsMartialArtsForms : byte
{
    /// <summary>Legion gladiatorial combat — brutal throw-and-slam style. Assigned to Centurion and Legate.</summary>
    LegionGladiatorial,

    /// <summary>Desert Ranger combat technique — precise joint-locks, disarms, and takedowns. Assigned to Veteran Ranger and Ranger Chief.</summary>
    RangerCombatTechnique,

    /// <summary>Desert survival fighting — momentum and velocity-based improvised brawling. Learned via Training Manual.</summary>
    DesertSurvivalFighting,

    /// <summary>Wasteland street fighting — dirty tricks, throws, and eye gouges. Learned via Training Manual.</summary>
    WastelandStreetFighting,

    /// <summary>Tribal warrior style — relentless consecutive strikes; disables firearms while fighting. Learned via Training Manual.</summary>
    TribalWarriorStyle,

    /// <summary>Shadow strike — assassin tactics, sneak attack multiplier, silent choke from a hug. Learned via Training Manual.</summary>
    ShadowStrike,
}
