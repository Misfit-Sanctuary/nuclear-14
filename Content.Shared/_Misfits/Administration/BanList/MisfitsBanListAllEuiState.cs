using Content.Shared.Administration.BanList;
using Content.Shared.Eui;
using Robust.Shared.Serialization;

// #Misfits Add - Shared EUI state for the global ban list window (banlistall command)
namespace Content.Shared._Misfits.Administration.BanList;

/// <summary>
/// EUI state sent from server to client containing all four ban lists:
/// active server bans, active role bans, all server bans (incl. expired/pardoned), all role bans.
/// </summary>
[Serializable, NetSerializable]
public sealed class MisfitsBanListAllEuiState : EuiStateBase
{
    public List<MisfitsBanEntry> ActiveBans { get; }
    public List<MisfitsRoleBanEntry> ActiveRoleBans { get; }
    public List<MisfitsBanEntry> AllBans { get; }
    public List<MisfitsRoleBanEntry> AllRoleBans { get; }

    public MisfitsBanListAllEuiState(
        List<MisfitsBanEntry> activeBans,
        List<MisfitsRoleBanEntry> activeRoleBans,
        List<MisfitsBanEntry> allBans,
        List<MisfitsRoleBanEntry> allRoleBans)
    {
        ActiveBans = activeBans;
        ActiveRoleBans = activeRoleBans;
        AllBans = allBans;
        AllRoleBans = allRoleBans;
    }
}

/// <summary>
/// A server ban entry with the banned player's resolved username included.
/// </summary>
[Serializable, NetSerializable]
public sealed class MisfitsBanEntry
{
    public SharedServerBan Ban { get; }
    public string PlayerName { get; }

    public MisfitsBanEntry(SharedServerBan ban, string playerName)
    {
        Ban = ban;
        PlayerName = playerName;
    }
}

/// <summary>
/// A role ban entry with the banned player's resolved username included.
/// </summary>
[Serializable, NetSerializable]
public sealed class MisfitsRoleBanEntry
{
    public SharedServerRoleBan Ban { get; }
    public string PlayerName { get; }

    public MisfitsRoleBanEntry(SharedServerRoleBan ban, string playerName)
    {
        Ban = ban;
        PlayerName = playerName;
    }
}
