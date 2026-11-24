// #Cythisiax Added - Faction rank picker. Lobby messages so a player can see which rank pins their
// character has unlocked (playtime or leadership) and choose which one to wear. Nobody is moved up
// on their own, picking a higher rank is the player's call.

using Robust.Shared.Serialization;

namespace Content.Shared._Misfits.FactionRank;

[Serializable, NetSerializable]
public sealed class RequestFactionRankOptionsEvent(string characterName) : EntityEventArgs
{
    public readonly string CharacterName = characterName;
}

[Serializable, NetSerializable]
public sealed class SetFactionRankEvent(string characterName, string job, string pin) : EntityEventArgs
{
    public readonly string CharacterName = characterName;
    public readonly string Job = job;
    public readonly string Pin = pin;
}

[Serializable, NetSerializable]
public sealed class FactionRankOptionsEvent(string characterName, List<FactionRankJobOptions> jobs) : EntityEventArgs
{
    public readonly string CharacterName = characterName;
    public readonly List<FactionRankJobOptions> Jobs = jobs;
}

[Serializable, NetSerializable]
public sealed record FactionRankJobOptions(string Job, List<FactionRankOption> Ranks, string? Held);

[Serializable, NetSerializable]
public sealed record FactionRankOption(string Pin, string Name, bool Unlocked, int Hours, bool Capped);
