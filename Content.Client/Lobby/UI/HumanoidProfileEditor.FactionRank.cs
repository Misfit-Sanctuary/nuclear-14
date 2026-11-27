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
            if (RankPreferences.BuildPicker(Profile?.Name, job, 190, out _) is { } picker)
                jobContainer.AddChild(picker);
        }
    }
}
