// #Cythisiax Added - Faction rank picker, a rank dropdown next to NCR and Enclave jobs in the job list.
// Only ranks the character has unlocked (playtime or leadership) can be picked. Locked ones show the
// hours they need, or that command is holding them back. Nobody moves up unless they pick it here.

using System.Linq;
using Content.Client._Misfits.FactionRank;
using Content.Shared.Roles;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.Lobby.UI
{
    public sealed partial class HumanoidProfileEditor
    {
        private FactionRankPreferenceSystem? _rankPreferences;

        private FactionRankPreferenceSystem RankPreferences
        {
            get
            {
                if (_rankPreferences != null)
                    return _rankPreferences;

                _rankPreferences = _entManager.System<FactionRankPreferenceSystem>();
                _rankPreferences.OptionsUpdated += name =>
                {
                    if (Profile?.Name == name)
                        RefreshJobs();
                };
                return _rankPreferences;
            }
        }

        private void AddRankPicker(BoxContainer jobContainer, JobPrototype job)
        {
            if (Profile == null || string.IsNullOrWhiteSpace(Profile.Name) || job.Rank is not { Pins.Count: > 0 })
                return;

            var prefs = RankPreferences;
            if (!prefs.TryGetOptions(Profile.Name, out var options))
                return;

            var jobOptions = options.Jobs.FirstOrDefault(j => j.Job == job.ID);
            if (jobOptions?.Held is not { } held)
                return;

            var characterName = Profile.Name;
            var picker = new OptionButton
            {
                MinWidth = 190,
                VerticalAlignment = VAlignment.Center,
                Margin = new(3f, 3f, 3f, 0f),
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
                prefs.SetRank(characterName, job.ID, jobOptions.Ranks[args.Id].Pin);
            };

            jobContainer.AddChild(picker);
        }
    }
}
