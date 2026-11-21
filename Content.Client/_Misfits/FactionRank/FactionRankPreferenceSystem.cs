// #Cythisiax Added - Faction rank picker on the client. Asks the server which rank pins a character
// has unlocked and sends the player's pick back. The lobby job list reads from here to draw the rank
// dropdown next to NCR and Enclave jobs.

using System.Diagnostics.CodeAnalysis;
using Content.Shared._Misfits.FactionRank;
using Robust.Shared.Timing;

namespace Content.Client._Misfits.FactionRank;

public sealed class FactionRankPreferenceSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;

    // playtime keeps ticking so the cached list goes stale, ask again after this long
    private static readonly TimeSpan CacheTime = TimeSpan.FromMinutes(1);

    private readonly Dictionary<string, (FactionRankOptionsEvent Options, TimeSpan Received)> _options = new();
    private readonly HashSet<string> _requested = new();

    public event Action<string>? OptionsUpdated;

    public override void Initialize()
    {
        SubscribeNetworkEvent<FactionRankOptionsEvent>(OnOptions);
    }

    private void OnOptions(FactionRankOptionsEvent ev)
    {
        _options[ev.CharacterName] = (ev, _timing.RealTime);
        _requested.Remove(ev.CharacterName);
        OptionsUpdated?.Invoke(ev.CharacterName);
    }

    public bool TryGetOptions(string characterName, [NotNullWhen(true)] out FactionRankOptionsEvent? options)
    {
        options = null;
        var fresh = false;
        if (_options.TryGetValue(characterName, out var cached))
        {
            options = cached.Options;
            fresh = _timing.RealTime - cached.Received < CacheTime;
        }

        if (!fresh && _requested.Add(characterName))
            RaiseNetworkEvent(new RequestFactionRankOptionsEvent(characterName));

        return options != null;
    }

    public void SetRank(string characterName, string job, string pin)
    {
        RaiseNetworkEvent(new SetFactionRankEvent(characterName, job, pin));
    }
}
