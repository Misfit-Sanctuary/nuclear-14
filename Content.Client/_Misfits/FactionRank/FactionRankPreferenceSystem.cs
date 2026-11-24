// #Cythisiax Added - Faction rank picker on the client. Asks the server which rank pins a character
// has unlocked and sends the player's pick back. The lobby job list reads from here to draw the rank
// dropdown next to NCR and Enclave jobs, and so does the latejoin menu, so a player can switch rank
// right before they drop in mid-round.

using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Shared._Misfits.FactionRank;
using Content.Shared.Roles;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
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

    // Rank dropdown for one job, used by the lobby editor and the latejoin list. Null when the job has
    // no pins, or when the options haven't come back from the server yet (pending is true then, so the
    // caller knows to redraw once they land).
    public OptionButton? BuildPicker(string? characterName, JobPrototype job, float minWidth, out bool pending)
    {
        pending = false;
        if (string.IsNullOrWhiteSpace(characterName) || job.Rank is not { Pins.Count: > 0 })
            return null;

        if (!TryGetOptions(characterName, out var options))
        {
            pending = true;
            return null;
        }

        var jobOptions = options.Jobs.FirstOrDefault(j => j.Job == job.ID);
        if (jobOptions?.Held is not { } held)
            return null;

        var picker = new OptionButton
        {
            MinWidth = minWidth,
            VerticalAlignment = Control.VAlignment.Center,
            Margin = new Thickness(3f, 0f, 3f, 0f),
            ToolTip = Loc.GetString("faction-rank-picker-tooltip"),
        };

        for (var i = 0; i < jobOptions.Ranks.Count; i++)
        {
            var option = jobOptions.Ranks[i];
            var label = option.Unlocked
                ? option.Name
                : option.Capped
                    ? Loc.GetString("faction-rank-picker-capped", ("rank", option.Name))
                    : Loc.GetString("faction-rank-picker-locked", ("rank", option.Name), ("hours", option.Hours));

            picker.AddItem(label, i);
            picker.SetItemDisabled(i, !option.Unlocked);

            if (option.Pin == held)
                picker.SelectId(i);
        }

        picker.OnItemSelected += args =>
        {
            picker.SelectId(args.Id);
            SetRank(characterName, job.ID, jobOptions.Ranks[args.Id].Pin);
        };

        return picker;
    }
}
