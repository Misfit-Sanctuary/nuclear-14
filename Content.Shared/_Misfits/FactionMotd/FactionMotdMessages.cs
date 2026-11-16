// #Cythisiax Added - Faction MOTD: network messages for the /factionmotd window.

using Robust.Shared.Serialization;

namespace Content.Shared._Misfits.FactionMotd;

public static class FactionMotdConfig
{
    /// <summary>Longest MOTD leadership may save.</summary>
    public const int MaxLength = 4000;
}

/// <summary>Client → server: the player ran /factionmotd and wants the current state.</summary>
[Serializable, NetSerializable]
public sealed class FactionMotdOpenRequestMsg : EntityEventArgs;

/// <summary>Client → server: leadership saves a new MOTD. Empty text clears it.</summary>
[Serializable, NetSerializable]
public sealed class FactionMotdSetMsg : EntityEventArgs
{
    public string Text = string.Empty;
}

/// <summary>Server → client: everything the MOTD window shows.</summary>
[Serializable, NetSerializable]
public sealed class FactionMotdStateMsg : EntityEventArgs
{
    /// <summary>False when the player's current job is not in any faction; the window says so.</summary>
    public bool InFaction;

    public string FactionName = string.Empty;
    public Color FactionColor = Color.White;

    /// <summary>The current MOTD, or empty when none is set.</summary>
    public string Text = string.Empty;

    /// <summary>Character name and job of whoever last set the MOTD.</summary>
    public string? Author;

    /// <summary>UTC time the MOTD was last set.</summary>
    public DateTime? UpdatedAt;

    /// <summary>The viewer holds a leadership role and may edit the MOTD.</summary>
    public bool CanEdit;

    /// <summary>Sent automatically on spawn rather than because the player asked for it.</summary>
    public bool OnSpawn;

    /// <summary>Optional result line (save confirmations, errors).</summary>
    public string? Status;

    public bool StatusIsError;
}
