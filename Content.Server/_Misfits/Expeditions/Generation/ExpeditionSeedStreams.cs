using System;

namespace Content.Server._Misfits.Expeditions.Generation;

/// <summary>
/// Stable, named RNG streams. Adding decoration rolls no longer changes room
/// topology, and the same master seed can be replayed between server runs.
/// </summary>
public static class ExpeditionSeedStreams
{
    public static Random Create(int masterSeed, string stage, int attempt = 0)
    {
        unchecked
        {
            uint hash = 2166136261;
            hash = (hash ^ (uint) masterSeed) * 16777619;
            foreach (var character in stage)
                hash = (hash ^ character) * 16777619;
            hash = (hash ^ (uint) attempt) * 16777619;
            return new Random((int)(hash & 0x7fffffff));
        }
    }
}
