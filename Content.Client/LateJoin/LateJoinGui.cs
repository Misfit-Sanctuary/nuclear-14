using System.Linq;
using System.Numerics;
using Content.Client.CrewManifest;
using Content.Client.GameTicking.Managers;
using Content.Client.Lobby;
using Content.Client.UserInterface.Controls;
using Content.Client.Players.PlayTimeTracking;
using Content.Shared.CCVar;
using Content.Shared.Customization.Systems;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Content.Shared.StatusIcon;
using Robust.Client.Console;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using static Robust.Client.UserInterface.Controls.BoxContainer;
using Content.Shared._NC.Sponsor; // Forge-Change
using Content.Client._Misfits.FactionRank; // #Cythisiax Added - latejoin rank picker

namespace Content.Client.LateJoin
{
    public sealed class LateJoinGui : DefaultWindow
    {
        [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
        [Dependency] private readonly IClientConsoleHost _consoleHost = default!;
        [Dependency] private readonly IConfigurationManager _configManager = default!;
        [Dependency] private readonly IEntitySystemManager _entitySystem = default!;
        [Dependency] private readonly JobRequirementsManager _jobRequirements = default!;
        [Dependency] private readonly IEntityManager _entityManager = default!;
        [Dependency] private readonly IClientPreferencesManager _prefs = default!;
        [Dependency] private readonly ISharedSponsorManager _sponsorManager = default!; // Forge-Change

        public event Action<(NetEntity, string)> SelectedId;

        private readonly ClientGameTicker _gameTicker;
        private readonly SpriteSystem _sprites;
        private readonly CrewManifestSystem _crewManifest;
        private readonly CharacterRequirementsSystem _characterRequirements;

        private readonly Dictionary<NetEntity, Dictionary<string, List<JobButton>>> _jobButtons = new();
        private readonly Dictionary<NetEntity, Dictionary<string, BoxContainer>> _jobCategories = new();
        private readonly List<ScrollContainer> _jobLists = new();
        private readonly HashSet<string> _collapsedDepartments = new();

        // #Misfits Add - active job tab filter; persists across RebuildUI calls
        private DepartmentUICategory _selectedCategory = DepartmentUICategory.NoFaction;

        private readonly Control _base;

        // #Cythisiax Added - rank dropdown for pin jobs
        private readonly FactionRankPreferenceSystem _rankPreferences;
        private bool _rankPickersPending;

        public LateJoinGui()
        {
            MinSize = SetSize = new Vector2(520, 560); // #Misfits Tweak - wider to fit 4 job category tabs
            IoCManager.InjectDependencies(this);
            _sprites = _entitySystem.GetEntitySystem<SpriteSystem>();
            _crewManifest = _entitySystem.GetEntitySystem<CrewManifestSystem>();
            _gameTicker = _entitySystem.GetEntitySystem<ClientGameTicker>();
            _characterRequirements = _entitySystem.GetEntitySystem<CharacterRequirementsSystem>();

            Title = Loc.GetString("late-join-gui-title");

            _base = new BoxContainer()
            {
                Orientation = LayoutOrientation.Vertical,
                VerticalExpand = true,
            };

            Contents.AddChild(_base);

            // #Cythisiax Added - redraw once the rank options come back, only if a picker was waiting on them
            _rankPreferences = _entitySystem.GetEntitySystem<FactionRankPreferenceSystem>();
            _rankPreferences.OptionsUpdated += OnRankOptionsUpdated;

            _jobRequirements.Updated += RebuildUI;
            RebuildUI();

            SelectedId += x =>
            {
                var (station, jobId) = x;
                Logger.InfoS("latejoin", $"Late joining as ID: {jobId}");
                _consoleHost.ExecuteCommand($"joingame {CommandParsing.Escape(jobId)} {station}");
                Close();
            };

            _gameTicker.LobbyJobsAvailableUpdated += JobsAvailableUpdated;
        }

        // #Cythisiax Added
        private void OnRankOptionsUpdated(string _)
        {
            if (_rankPickersPending)
                RebuildUI();
        }

        private void RebuildUI()
        {
            _rankPickersPending = false; // #Cythisiax Added
            _base.RemoveAllChildren();
            _jobLists.Clear();
            _jobButtons.Clear();
            _jobCategories.Clear();

            if (!_gameTicker.DisallowedLateJoin && _gameTicker.StationNames.Count == 0)
                Logger.Warning("No stations exist, nothing to display in late-join GUI");

            foreach (var (id, name) in _gameTicker.StationNames)
            {
                var jobList = new BoxContainer
                {
                    Orientation = LayoutOrientation.Vertical,
                    Margin = new Thickness(0, 0, 5f, 0),
                };

                var collapseButton = new ContainerButton()
                {
                    HorizontalAlignment = HAlignment.Right,
                    ToggleMode = true,
                    Children =
                    {
                        new TextureRect
                        {
                            StyleClasses = { OptionButton.StyleClassOptionTriangle },
                            Margin = new Thickness(8, 0),
                            HorizontalAlignment = HAlignment.Center,
                            VerticalAlignment = VAlignment.Center,
                        }
                    }
                };

                _base.AddChild(new StripeBack()
                {
                    Children =
                    {
                        new PanelContainer()
                        {
                            Children =
                            {
                                new Label()
                                {
                                    StyleClasses = { "LabelBig" },
                                    Text = name,
                                    Align = Label.AlignMode.Center,
                                },
                                collapseButton
                            }
                        }
                    }
                });

                if (_configManager.GetCVar(CCVars.CrewManifestWithoutEntity))
                {
                    var crewManifestButton = new Button()
                    {
                        Text = Loc.GetString("crew-manifest-button-label")
                    };
                    crewManifestButton.OnPressed += _ => _crewManifest.RequestCrewManifest(id);

                    _base.AddChild(crewManifestButton);
                }

                // #Misfits Add - 4-tab strip: No Faction | Minor Factions | Major Factions | Whitelist
                var tabStrip = new BoxContainer
                {
                    Orientation = LayoutOrientation.Horizontal,
                    HorizontalExpand = true,
                    Margin = new Thickness(0, 2, 0, 2),
                };

                var tabDefs = new[]
                {
                    (DepartmentUICategory.NoFaction,    "job-tab-no-faction"),
                    (DepartmentUICategory.MinorFaction, "job-tab-minor-factions"),
                    (DepartmentUICategory.MajorFaction, "job-tab-major-factions"),
                    (DepartmentUICategory.Whitelist,    "job-tab-whitelist"),
                };

                // Capture for closure — jobList is rebuilt when the tab changes
                var capturedId = id;
                foreach (var (cat, locKey) in tabDefs)
                {
                    var tab = new Button
                    {
                        Text = Loc.GetString(locKey),
                        HorizontalExpand = true,
                        ToggleMode = true,
                        Pressed = cat == _selectedCategory,
                    };
                    var capturedCat = cat;
                    tab.OnPressed += _ =>
                    {
                        _selectedCategory = capturedCat;
                        RebuildUI();
                    };
                    tabStrip.AddChild(tab);
                }

                _base.AddChild(tabStrip);

                var jobListScroll = new ScrollContainer()
                {
                    VerticalExpand = true,
                    Children = { jobList },
                    Visible = false,
                };

                if (_jobLists.Count == 0)
                    jobListScroll.Visible = true;

                _jobLists.Add(jobListScroll);

                _base.AddChild(jobListScroll);

                collapseButton.OnToggled += _ =>
                {
                    foreach (var section in _jobLists)
                    {
                        section.Visible = false;
                    }
                    jobListScroll.Visible = true;
                };

                var firstCategory = true;
                var departments = _prototypeManager.EnumeratePrototypes<DepartmentPrototype>()
                    .Where(d => d.UICategory == _selectedCategory) // #Misfits Add - filter by active tab
                    .ToArray();
                Array.Sort(departments, DepartmentUIComparer.Instance);

                _jobButtons[id] = new Dictionary<string, List<JobButton>>();

                foreach (var department in departments)
                {
                    var departmentName = Loc.GetString($"department-{department.ID}");
                    _jobCategories[id] = new Dictionary<string, BoxContainer>();
                    var stationAvailable = _gameTicker.JobsAvailable[id];
                    var jobsAvailable = new List<JobPrototype>();

                    foreach (var jobId in department.Roles)
                    {
                        if (!stationAvailable.ContainsKey(jobId))
                            continue;

                        var job = _prototypeManager.Index<JobPrototype>(jobId);
                        var jobWhitelisted = _jobRequirements.IsJobWhitelisted(job.ID);

                        // #Misfits Change - hide whitelist-gated jobs from non-whitelisted players
                        if (job.HideWithoutWhitelist && !_jobRequirements.IsWhitelisted() && !(job.Whitelisted && jobWhitelisted))
                            continue;

                        if (job.HideWithoutJobWhitelist && !jobWhitelisted)
                            continue;

                        if (job.HideIfPlaytimeRequirementsNotMet && !_characterRequirements.CheckPlaytimeRequirementsVisible(
                                job.Requirements ?? new(),
                                job,
                                (HumanoidCharacterProfile) (_prefs.Preferences?.SelectedCharacter
                                                            ?? HumanoidCharacterProfile.DefaultWithSpecies()),
                                _jobRequirements.GetRawPlayTimeTrackers(),
                                _jobRequirements.IsWhitelisted(),
                                job,
                                _entityManager,
                                _prototypeManager,
                                _configManager,
                                _sponsorManager,
                                out _,
                                jobWhitelisted: jobWhitelisted))
                            continue;

                        jobsAvailable.Add(job);
                    }

                    jobsAvailable.Sort(JobUIComparer.Instance);

                    // Do not display departments with no jobs available.
                    if (jobsAvailable.Count == 0)
                        continue;

                    var category = new BoxContainer
                    {
                        Orientation = LayoutOrientation.Vertical,
                        Name = department.ID,
                        ToolTip = Loc.GetString("late-join-gui-jobs-amount-in-department-tooltip",
                            ("departmentName", departmentName))
                    };

                    if (firstCategory)
                    {
                        firstCategory = false;
                    }
                    else
                    {
                        category.AddChild(new Control
                        {
                            MinSize = new Vector2(0, 23),
                        });
                    }

                    var departmentContents = new BoxContainer
                    {
                        Orientation = LayoutOrientation.Vertical,
                        Visible = !_collapsedDepartments.Contains(department.ID),
                    };

                    var departmentButton = new Button
                    {
                        Text = departmentName,
                        HorizontalExpand = true,
                        ToggleMode = true,
                        Pressed = !_collapsedDepartments.Contains(department.ID),
                    };
                    departmentButton.OnToggled += args =>
                    {
                        departmentContents.Visible = args.Pressed;
                        if (args.Pressed)
                            _collapsedDepartments.Remove(department.ID);
                        else
                            _collapsedDepartments.Add(department.ID);
                    };

                    category.AddChild(departmentButton);
                    category.AddChild(departmentContents);

                    _jobCategories[id][department.ID] = category;
                    jobList.AddChild(category);

                    foreach (var prototype in jobsAvailable)
                    {
                        var value = stationAvailable[prototype.ID];

                        var jobLabel = new Label
                        {
                            VerticalAlignment = VAlignment.Center,
                        };

                        var jobButton = new JobButton(jobLabel, prototype.ID, prototype.LocalizedName, value);

                        var jobSelector = new BoxContainer
                        {
                            Orientation = LayoutOrientation.Horizontal,
                            HorizontalExpand = true,
                            SeparationOverride = 6,
                        };

                        // #Misfits Tweak - fixed icon slot keeps mixed faction rank icons aligned in latejoin.
                        var icon = new TextureRect
                        {
                            Stretch = TextureRect.StretchMode.KeepAspectCentered,
                            SetSize = new Vector2(16f, 16f),
                            MinSize = new Vector2(16f, 16f),
                            MaxSize = new Vector2(16f, 16f),
                            VerticalAlignment = VAlignment.Center,
                        };

                        var jobIcon = _prototypeManager.Index(prototype.Icon);
                        icon.Texture = _sprites.Frame0(jobIcon.Icon);
                        jobSelector.AddChild(icon);

                        jobSelector.AddChild(jobLabel);
                        jobButton.AddChild(jobSelector);

                        // #Misfits Tweak - stronger gap makes role tier breaks readable in ranked departments.
                        if (prototype.ShowBorder)
                        {
                            departmentContents.AddChild(new PanelContainer
                            {
                                PanelOverride = new StyleBoxFlat { BackgroundColor = Color.FromHex("#464966") },
                                MinSize = new Vector2(0, 2),
                                Margin = new Thickness(3f, 10f, 3f, 6f),
                            });
                        }

                        // #Cythisiax Edited - pin jobs (NCR / Enclave) get their rank dropdown beside the job button
                        var rankPicker = _rankPreferences.BuildPicker(
                            (_prefs.Preferences?.SelectedCharacter as HumanoidCharacterProfile)?.Name,
                            prototype, 160, out var rankPending);
                        _rankPickersPending |= rankPending;
                        if (rankPicker != null)
                        {
                            jobButton.HorizontalExpand = true;
                            var jobRow = new BoxContainer { Orientation = LayoutOrientation.Horizontal };
                            jobRow.AddChild(jobButton);
                            jobRow.AddChild(rankPicker);
                            departmentContents.AddChild(jobRow);
                        }
                        else
                        {
                            departmentContents.AddChild(jobButton);
                        }

                        jobButton.OnPressed += _ => SelectedId.Invoke((id, jobButton.JobId));

                        if (!_jobRequirements.CheckJobWhitelist(prototype, out var reason))
                        {
                            jobButton.Disabled = true;

                            var tooltip = new Tooltip();
                            tooltip.SetMessage(reason);
                            jobButton.TooltipSupplier = _ => tooltip;

                            jobSelector.AddChild(new TextureRect
                            {
                                TextureScale = new Vector2(0.4f, 0.4f),
                                Stretch = TextureRect.StretchMode.KeepCentered,
                                Texture = _sprites.Frame0(new SpriteSpecifier.Texture(new ("/Textures/Interface/Nano/lock.svg.192dpi.png"))),
                                HorizontalExpand = true,
                                HorizontalAlignment = HAlignment.Right,
                            });
                        }
                        else if (!_characterRequirements.CheckRequirementsValid(
                                prototype.Requirements ?? new(),
                                prototype,
                                (HumanoidCharacterProfile) (_prefs.Preferences?.SelectedCharacter
                                                            ?? HumanoidCharacterProfile.DefaultWithSpecies()),
                                _jobRequirements.GetRawPlayTimeTrackers(),
                                _jobRequirements.IsWhitelisted(),
                                prototype,
                                _entityManager,
                                _prototypeManager,
                                _configManager,
                                _sponsorManager, // Forge-Change
                            out var reasons,
                            jobWhitelisted: _jobRequirements.IsJobWhitelisted(prototype.ID)))
                        {
                            jobButton.Disabled = true;

                            if (reasons.Count > 0)
                            {
                                var tooltip = new Tooltip();
                                tooltip.SetMessage(_characterRequirements.GetRequirementsText(reasons));
                                jobButton.TooltipSupplier = _ => tooltip;
                            }

                            jobSelector.AddChild(new TextureRect
                            {
                                TextureScale = new Vector2(0.4f, 0.4f),
                                Stretch = TextureRect.StretchMode.KeepCentered,
                                Texture = _sprites.Frame0(new SpriteSpecifier.Texture(new ("/Textures/Interface/Nano/lock.svg.192dpi.png"))),
                                HorizontalExpand = true,
                                HorizontalAlignment = HAlignment.Right,
                            });
                        }
                        else if (value == 0)
                        {
                            jobButton.Disabled = true;
                        }

                        if (!_jobButtons[id].ContainsKey(prototype.ID))
                        {
                            _jobButtons[id][prototype.ID] = new List<JobButton>();
                        }

                        _jobButtons[id][prototype.ID].Add(jobButton);
                    }
                }
            }
        }

        private void JobsAvailableUpdated(IReadOnlyDictionary<NetEntity, Dictionary<string, uint?>> updatedJobs)
        {
            foreach (var stationEntries in updatedJobs)
            {
                if (_jobButtons.ContainsKey(stationEntries.Key))
                {
                    var jobsAvailable = stationEntries.Value;

                    var existingJobEntries = _jobButtons[stationEntries.Key];
                    foreach (var existingJobEntry in existingJobEntries)
                    {
                        if (jobsAvailable.ContainsKey(existingJobEntry.Key))
                        {
                            var updatedJobValue = jobsAvailable[existingJobEntry.Key];
                            foreach (var matchingJobButton in existingJobEntry.Value)
                            {
                                if (matchingJobButton.Amount != updatedJobValue)
                                {
                                    matchingJobButton.RefreshLabel(updatedJobValue);
                                    matchingJobButton.Disabled |= matchingJobButton.Amount == 0;
                                }
                            }
                        }
                    }
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (disposing)
            {
                _jobRequirements.Updated -= RebuildUI;
                _rankPreferences.OptionsUpdated -= OnRankOptionsUpdated; // #Cythisiax Added
                _gameTicker.LobbyJobsAvailableUpdated -= JobsAvailableUpdated;
                _jobButtons.Clear();
                _jobCategories.Clear();
            }
        }
    }

    sealed class JobButton : ContainerButton
    {
        public Label JobLabel { get; }
        public string JobId { get; }
        public string JobLocalisedName { get; }
        public uint? Amount { get; private set; }
        private bool _initialised = false;

        public JobButton(Label jobLabel, string jobId, string jobLocalisedName, uint? amount)
        {
            JobLabel = jobLabel;
            JobId = jobId;
            JobLocalisedName = jobLocalisedName;
            RefreshLabel(amount);
            AddStyleClass(StyleClassButton);
            _initialised = true;
        }

        public void RefreshLabel(uint? amount)
        {
            if (Amount == amount && _initialised)
            {
                return;
            }
            Amount = amount;

            JobLabel.Text = Amount != null ?
                Loc.GetString("late-join-gui-job-slot-capped", ("jobName", JobLocalisedName), ("amount", Amount)) :
                Loc.GetString("late-join-gui-job-slot-uncapped", ("jobName", JobLocalisedName));
        }
    }
}
