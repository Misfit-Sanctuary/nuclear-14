// #Cythisiax Added - Faction MOTD: network messages for the /factionmotd window.

using Robust.Shared.Serialization;

namespace Content.Shared._Misfits.FactionMotd;

public static class FactionMotdConfig
{
    public const int MaxLength = 4000;
}

[Serializable, NetSerializable]
public sealed class FactionMotdOpenRequestMsg : EntityEventArgs;

[Serializable, NetSerializable]
public sealed class FactionMotdSetMsg : EntityEventArgs
{
    public string Text = string.Empty;
}

[Serializable, NetSerializable]
public sealed class FactionMotdStateMsg : EntityEventArgs
{
    public bool InFaction;

    public string FactionName = string.Empty;
    public Color FactionColor = Color.White;

    public string Text = string.Empty;

    public string? Author;

    public DateTime? UpdatedAt;

    public bool CanEdit;

    public bool OnSpawn;

    public string? Status;

    public bool StatusIsError;
}
