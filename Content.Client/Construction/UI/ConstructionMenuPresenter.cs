using System.Globalization;
using System.Linq;
using Content.Client._Misfits.Construction; // #Misfits Add
using Content.Client.UserInterface.Systems.MenuBar.Widgets;
using Content.Shared._Misfits.Crafting;
using Content.Shared._Misfits.Special;
using Content.Shared.Construction.Prototypes;
using Content.Shared.Materials;
using Content.Shared.Stacks;
using Robust.Shared.Containers;
using Content.Shared.Tag;
using Content.Shared.Whitelist;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Placement;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.Utility;
using Robust.Shared.Enums;
using Robust.Shared.Graphics;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using static Robust.Client.UserInterface.Controls.BaseButton;

namespace Content.Client.Construction.UI
{
    /// <summary>
    /// This class presents the Construction/Crafting UI to the client, linking the <see cref="ConstructionSystem" /> with the
    /// model. This is where the bulk of UI work is done, either calling functions in the model to change state, or collecting
    /// data out of the model to *present* to the screen though the UI framework.
    /// </summary>
    internal sealed class ConstructionMenuPresenter : IDisposable
    {
        [Dependency] private readonly EntityManager _entManager = default!;
        [Dependency] private readonly IEntitySystemManager _systemManager = default!;
        [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
        [Dependency] private readonly IPlacementManager _placementManager = default!;
        [Dependency] private readonly IUserInterfaceManager _uiManager = default!;
        [Dependency] private readonly IPlayerManager _playerManager = default!;

        private readonly IConstructionMenuView _constructionView;
        private readonly EntityWhitelistSystem _whitelistSystem;
        private readonly MisfitsCraftableNowSystem _craftableNow; // #Misfits Add
        private readonly SharedSpecialSystem _special;
        private readonly SharedMaterialStorageSystem _materialStorage;

        private ConstructionSystem? _constructionSystem;
        private ConstructionPrototype? _selected;
        private HandCraftIntellRecipePrototype? _selectedIntellRecipe;
        private Dictionary<string, int> _lastLeftoverMaterials = new();

        private bool CraftingAvailable
        {
            get => _uiManager.GetActiveUIWidget<GameTopMenuBar>().CraftingButton.Visible;
            set
            {
                _uiManager.GetActiveUIWidget<GameTopMenuBar>().CraftingButton.Visible = value;
                if (!value)
                    _constructionView.Close();
            }
        }

        /// <summary>
        /// Does the window have focus? If the window is closed, this will always return false.
        /// </summary>
        private bool IsAtFront => _constructionView.IsOpen && _constructionView.IsAtFront();

        private bool WindowOpen
        {
            get => _constructionView.IsOpen;
            set
            {
                if (value && CraftingAvailable)
                {
                    if (_constructionView.IsOpen)
                        _constructionView.MoveToFront();
                    else
                    {
                        _constructionView.OpenCentered();
                        OnViewPopulateRecipes(_constructionView, (string.Empty, string.Empty));
                    }

                    if (_selected != null)
                        PopulateInfo(_selected);

                    UpdateLeftoverMaterials();
                }
                else
                    _constructionView.Close();
            }
        }

        /// <summary>
        /// Constructs a new instance of <see cref="ConstructionMenuPresenter" />.
        /// </summary>
        public ConstructionMenuPresenter()
        {
            // This is a lot easier than a factory
            IoCManager.InjectDependencies(this);
            _constructionView = new ConstructionMenu();
            _whitelistSystem = _entManager.System<EntityWhitelistSystem>();
            _craftableNow = _entManager.System<MisfitsCraftableNowSystem>(); // #Misfits Add
            _special = _entManager.System<SharedSpecialSystem>();
            _materialStorage = _entManager.System<SharedMaterialStorageSystem>();

            // This is required so that if we load after the system is initialized, we can bind to it immediately
            if (_systemManager.TryGetEntitySystem<ConstructionSystem>(out var constructionSystem))
                SystemBindingChanged(constructionSystem);

            _systemManager.SystemLoaded += OnSystemLoaded;
            _systemManager.SystemUnloaded += OnSystemUnloaded;

            _placementManager.PlacementChanged += OnPlacementChanged;

            _constructionView.OnClose += () => _uiManager.GetActiveUIWidget<GameTopMenuBar>().CraftingButton.Pressed = false;
            _constructionView.ClearAllGhosts += (_, _) => _constructionSystem?.ClearAllGhosts();
            _constructionView.PopulateRecipes += OnViewPopulateRecipes;
            _constructionView.RecipeSelected += OnViewRecipeSelected;
            _constructionView.BuildButtonToggled += (_, b) => BuildButtonToggled(b);
            _constructionView.EraseButtonToggled += (_, b) =>
            {
                if (_constructionSystem is null) return;
                if (b) _placementManager.Clear();
                _placementManager.ToggleEraserHijacked(new ConstructionPlacementHijack(_constructionSystem, null));
                _constructionView.EraseButtonPressed = b;
            };

            PopulateCategories();
            OnViewPopulateRecipes(_constructionView, (string.Empty, string.Empty));

        }

        public void OnHudCraftingButtonToggled(ButtonToggledEventArgs args)
        {
            WindowOpen = args.Pressed;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _constructionView.Dispose();

            SystemBindingChanged(null);
            _systemManager.SystemLoaded -= OnSystemLoaded;
            _systemManager.SystemUnloaded -= OnSystemUnloaded;

            _placementManager.PlacementChanged -= OnPlacementChanged;
        }

        private void OnPlacementChanged(object? sender, EventArgs e)
        {
            _constructionView.ResetPlacement();
        }

        private void OnViewRecipeSelected(object? sender, ItemList.Item? item)
        {
            if (item is null)
            {
                _selected = null;
                _selectedIntellRecipe = null;
                _constructionView.ClearRecipeInfo();
                return;
            }

            if (item.Metadata is HandCraftIntellRecipePrototype intellRecipe)
            {
                _selected = null;
                _selectedIntellRecipe = intellRecipe;
                PopulateIntellInfo(intellRecipe);
                return;
            }

            _selectedIntellRecipe = null;
            _selected = (ConstructionPrototype) item.Metadata!;
            if (_placementManager.IsActive && !_placementManager.Eraser) UpdateGhostPlacement();
            PopulateInfo(_selected);
        }

        private void OnViewPopulateRecipes(object? sender, (string search, string catagory) args)
        {
            var (search, category) = args;
            var recipesList = _constructionView.Recipes;

            recipesList.Clear();
            var recipes = new List<ConstructionPrototype>();

            foreach (var recipe in _prototypeManager.EnumeratePrototypes<ConstructionPrototype>())
            {
                if (recipe.Hide)
                    continue;

                if (_playerManager.LocalSession == null
                || _playerManager.LocalEntity == null
                || _whitelistSystem.IsWhitelistFail(recipe.EntityWhitelist, _playerManager.LocalEntity.Value))
                    continue;

                // Forge-Change-start
                var recipeName = recipe.Name.ToLowerInvariant();
                var localizedRecipeName = Loc.GetString($"ent-{recipe.ID}").ToLowerInvariant();

                if (!string.IsNullOrEmpty(search))
                {
                    var searchLower = search.Trim().ToLowerInvariant();
                    if (!recipeName.Contains(searchLower) && !localizedRecipeName.Contains(searchLower))
                        continue;
                } // Forge-Change-end

                if (!string.IsNullOrEmpty(category) && category != "construction-category-all")
                {
                    if (recipe.Category != category)
                        continue;
                }

                recipes.Add(recipe);
            }

            recipes.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.InvariantCulture));

            foreach (var recipe in recipes)
            {
                recipesList.Add(GetItem(recipe, recipesList));
            }

            PopulateWorkbenchRecipes(search);

            // There is apparently no way to set which

            PopulateCraftableNow(); // #Misfits Add
        }

        // #Misfits Add: fills the "Craftable Now" section with every recipe whose material
        // requirements can be satisfied by stacks in the player's hands, bags, or on the ground.
        private void PopulateCraftableNow()
        {
            var player = _playerManager.LocalEntity;
            var craftableList = _constructionView.CraftableRecipes;
            craftableList.Clear();

            if (player == null)
            {
                _constructionView.SetCraftableNowVisible(false);
                return;
            }

            foreach (var recipe in _prototypeManager.EnumeratePrototypes<ConstructionPrototype>())
            {
                if (recipe.Hide)
                    continue;

                if (_whitelistSystem.IsWhitelistFail(recipe.EntityWhitelist, player.Value))
                    continue;

                if (!_craftableNow.IsCraftable(recipe, player.Value))
                    continue;

                craftableList.Add(GetItem(recipe, craftableList));
            }

            var playerInt = _special.GetEffective(player.Value, SpecialStat.Intelligence);
            var clientMaterials = CollectClientMaterials(player.Value);
            var spriteSys = _systemManager.GetEntitySystem<SpriteSystem>();

            foreach (var craftData in _prototypeManager.EnumeratePrototypes<HandCraftIntellRecipePrototype>())
            {
                if (playerInt < craftData.MinInt)
                    continue;
                if (!ClientHasMaterials(clientMaterials, craftData.Materials))
                    continue;

                var displayName = GetIntellRecipeName(craftData);
                var icon = spriteSys.Frame0(new SpriteSpecifier.EntityPrototype(craftData.Result));
                var desc = string.Empty;
                if (_prototypeManager.TryIndex<EntityPrototype>(craftData.Result, out var resultProto))
                    desc = resultProto.Description;

                craftableList.Add(new ItemList.Item(craftableList)
                {
                    Metadata = craftData,
                    Text = displayName,
                    Icon = icon,
                    TooltipEnabled = true,
                    TooltipText = desc,
                });
            }

            // Alpha-sort to match the main list.
            // ItemList doesn't support sorting in-place, so rebuild it sorted.
            var sorted = new List<ItemList.Item>();
            for (var i = 0; i < craftableList.Count; i++)
                sorted.Add(craftableList[i]);
            sorted.Sort((a, b) => string.Compare(a.Text, b.Text, StringComparison.InvariantCulture));
            craftableList.Clear();
            foreach (var item in sorted)
                craftableList.Add(item);

            _constructionView.SetCraftableNowVisible(craftableList.Count > 0);
        }

        private Dictionary<string, int> CollectClientMaterials(EntityUid player)
        {
            var result = new Dictionary<string, int>();
            var visited = new HashSet<EntityUid>();
            ScanContainersForMaterials(player, result, visited);
            return result;
        }

        private void ScanContainersForMaterials(EntityUid uid, Dictionary<string, int> materials, HashSet<EntityUid> visited)
        {
            if (!visited.Add(uid))
                return;
            if (!_entManager.TryGetComponent<ContainerManagerComponent>(uid, out var containers))
                return;
            foreach (var container in containers.Containers.Values)
            {
                foreach (var contained in container.ContainedEntities)
                {
                    if (_entManager.HasComponent<MaterialComponent>(contained) &&
                        _entManager.TryGetComponent<PhysicalCompositionComponent>(contained, out var comp))
                    {
                        var count = _entManager.TryGetComponent<StackComponent>(contained, out var stack) ? stack.Count : 1;
                        foreach (var (matId, volPerUnit) in comp.MaterialComposition)
                            materials[matId] = materials.GetValueOrDefault(matId) + volPerUnit * count;
                    }
                    ScanContainersForMaterials(contained, materials, visited);
                }
            }
        }

        private static bool ClientHasMaterials(Dictionary<string, int> available, Dictionary<ProtoId<MaterialPrototype>, int> required)
        {
            foreach (var (mat, amount) in required)
            {
                if (!available.TryGetValue(mat, out var have) || have < amount)
                    return false;
            }
            return true;
        }

        private void PopulateCategories()
        {
            var uniqueCategories = new HashSet<string>();

            // hard-coded to show all recipes
            uniqueCategories.Add("construction-category-all");

            foreach (var prototype in _prototypeManager.EnumeratePrototypes<ConstructionPrototype>())
            {
                var category = prototype.Category;

                if (!string.IsNullOrEmpty(category))
                    uniqueCategories.Add(category);
            }

            _constructionView.Category.Clear();

            var array = uniqueCategories.OrderBy(Loc.GetString).ToArray();
            Array.Sort(array);

            for (var i = 0; i < array.Length; i++)
            {
                var category = array[i];
                _constructionView.Category.AddItem(Loc.GetString(category), i);
            }

            _constructionView.Categories = array;
        }

        private void PopulateInfo(ConstructionPrototype prototype)
        {
            var spriteSys = _systemManager.GetEntitySystem<SpriteSystem>();
            _constructionView.ClearRecipeInfo();

            // Forge-Change-start
            var localizedName = Loc.TryGetString($"ent-{prototype.ID}", out var name) ? name : prototype.Name;
            var localizedDescription = Loc.TryGetString($"ent-{prototype.ID}.desc", out var desc) ? desc : prototype.Description;

            _constructionView.SetRecipeInfo(localizedName, localizedDescription, spriteSys.Frame0(prototype.Icon), prototype.Type != ConstructionType.Item);
            // Forge-Change-end
            var stepList = _constructionView.RecipeStepList;
            GenerateStepList(prototype, stepList);
        }

        private void GenerateStepList(ConstructionPrototype prototype, ItemList stepList)
        {
            if (_constructionSystem?.GetGuide(prototype) is not { } guide)
                return;

            var spriteSys = _systemManager.GetEntitySystem<SpriteSystem>();

            foreach (var entry in guide.Entries)
            {
                if (string.IsNullOrEmpty(entry.Localization)) // #Misfits Change - skip spacer entries with empty localization
                {
                    stepList.AddItem(string.Empty, Texture.Transparent, false);
                    continue;
                }

                var text = entry.Arguments != null
                    ? Loc.GetString(entry.Localization, entry.Arguments) : Loc.GetString(entry.Localization);

                if (entry.EntryNumber is { } number)
                {
                    text = Loc.GetString("construction-presenter-step-wrapper",
                        ("step-number", number), ("text", text));
                }

                // The padding needs to be applied regardless of text length... (See PadLeft documentation)
                text = text.PadLeft(text.Length + entry.Padding);

                var icon = entry.Icon != null ? spriteSys.Frame0(entry.Icon) : Texture.Transparent;
                stepList.AddItem(text, icon, false);
            }
        }

        private static ItemList.Item GetItem(ConstructionPrototype recipe, ItemList itemList)
        {
            var localizedName = Loc.TryGetString($"ent-{recipe.ID}", out var name) ? name : recipe.Name; // Forge-Change

            return new(itemList)
            {
                Metadata = recipe,
                Text = localizedName, // Forge-Change
                Icon = recipe.Icon.Frame0(),
                TooltipEnabled = true,
                TooltipText = recipe.Description
            };
        }

        private void PopulateWorkbenchRecipes(string search)
        {
            var recipesList = _constructionView.Recipes;

            var player = _playerManager.LocalEntity;
            if (player == null)
                return;

            var playerInt = _special.GetEffective(player.Value, SpecialStat.Intelligence);
            var spriteSys = _systemManager.GetEntitySystem<SpriteSystem>();

            var workbenchItems = new List<(string name, ItemList.Item item)>();

            foreach (var craftData in _prototypeManager.EnumeratePrototypes<HandCraftIntellRecipePrototype>())
            {
                if (playerInt < craftData.MinInt)
                    continue;

                var displayName = GetIntellRecipeName(craftData);

                if (!string.IsNullOrEmpty(search))
                {
                    var searchLower = search.Trim().ToLowerInvariant();
                    if (!displayName.ToLowerInvariant().Contains(searchLower))
                        continue;
                }

                var icon = spriteSys.Frame0(new SpriteSpecifier.EntityPrototype(craftData.Result));
                var desc = string.Empty;
                if (_prototypeManager.TryIndex<EntityPrototype>(craftData.Result, out var resultProto))
                    desc = resultProto.Description;

                workbenchItems.Add((displayName, new ItemList.Item(recipesList)
                {
                    Metadata = craftData,
                    Text = displayName,
                    Icon = icon,
                    TooltipEnabled = true,
                    TooltipText = desc,
                }));
            }

            workbenchItems.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.InvariantCulture));

            if (workbenchItems.Count > 0)
            {
                recipesList.AddItem("─── Intelligence ───", null, false);
                foreach (var (_, item) in workbenchItems)
                    recipesList.Add(item);
            }
        }

        private void PopulateIntellInfo(HandCraftIntellRecipePrototype craftData)
        {
            var spriteSys = _systemManager.GetEntitySystem<SpriteSystem>();
            _constructionView.ClearRecipeInfo();

            var name = GetIntellRecipeName(craftData);
            var icon = spriteSys.Frame0(new SpriteSpecifier.EntityPrototype(craftData.Result));

            var desc = string.Empty;
            if (_prototypeManager.TryIndex<EntityPrototype>(craftData.Result, out var resultProto))
                desc = resultProto.Description;

            _constructionView.SetRecipeInfo(name, desc, icon, false);

            var stepList = _constructionView.RecipeStepList;
            stepList.AddItem($"Requires INT {craftData.MinInt}", Texture.Transparent, false);

            foreach (var (mat, amount) in craftData.Materials)
            {
                string label;
                Texture matIcon = Texture.Transparent;

                if (_prototypeManager.TryIndex<MaterialPrototype>(mat, out var matProto))
                {
                    var sheetVolume = Math.Max(1, _materialStorage.GetSheetVolume(matProto));
                    var quantity = amount / (double) sheetVolume;
                    var matName = Loc.GetString(matProto.Name);
                    label = $"{quantity.ToString("0.##", CultureInfo.InvariantCulture)}x {matName}";
                    matIcon = spriteSys.Frame0(matProto.Icon);
                }
                else
                {
                    label = $"{mat.Id}: {amount}";
                }

                stepList.AddItem(label, matIcon, false);
            }
        }

        public void UpdateLeftoverMaterials()
        {
            if (!_constructionView.IsOpen)
                return;

            var mats = new Dictionary<string, int>();
            if (_playerManager.LocalEntity is { } player &&
                _entManager.TryGetComponent<MaterialStorageComponent>(player, out var storage))
            {
                foreach (var (mat, amount) in storage.Storage)
                {
                    if (amount > 0)
                        mats[mat.Id] = amount;
                }
            }

            if (_lastLeftoverMaterials.Count == mats.Count &&
                _lastLeftoverMaterials.All(pair => mats.GetValueOrDefault(pair.Key) == pair.Value))
                return;

            _lastLeftoverMaterials = mats;

            var list = _constructionView.LeftoverMaterialsList;
            list.Clear();

            var spriteSys = _systemManager.GetEntitySystem<SpriteSystem>();
            foreach (var (matId, amount) in mats.OrderBy(pair => pair.Key))
            {
                if (!_prototypeManager.TryIndex<MaterialPrototype>(matId, out var matProto))
                    continue;

                var sheetVolume = Math.Max(1, _materialStorage.GetSheetVolume(matProto));
                var quantity = amount / (double) sheetVolume;
                var matName = Loc.GetString(matProto.Name);
                var label = $"{quantity.ToString("0.##", CultureInfo.InvariantCulture)}x {matName}";
                list.AddItem(label, spriteSys.Frame0(matProto.Icon), false);
            }
        }

        private string GetIntellRecipeName(HandCraftIntellRecipePrototype recipe)
        {
            if (Loc.TryGetString($"ent-{recipe.Result}", out var name))
                return name;
            if (_prototypeManager.TryIndex<EntityPrototype>(recipe.Result, out var entProto) && !string.IsNullOrEmpty(entProto.Name))
                return entProto.Name;
            return recipe.ID;
        }

        private void BuildButtonToggled(bool pressed)
        {
            if (pressed)
            {
                if (_selectedIntellRecipe != null)
                {
                    if (_constructionSystem is not null)
                        _constructionSystem.TryHandCraftIntellRecipe(_selectedIntellRecipe.ID);
                    _constructionView.BuildButtonPressed = false;
                    return;
                }

                if (_selected == null) return;

                // not bound to a construction system
                if (_constructionSystem is null)
                {
                    _constructionView.BuildButtonPressed = false;
                    return;
                }

                if (_selected.Type == ConstructionType.Item)
                {
                    _constructionSystem.TryStartItemConstruction(_selected.ID);
                    _constructionView.BuildButtonPressed = false;
                    return;
                }

                _placementManager.BeginPlacing(new PlacementInformation
                {
                    IsTile = false,
                    PlacementOption = _selected.PlacementMode
                }, new ConstructionPlacementHijack(_constructionSystem, _selected));

                UpdateGhostPlacement();
            }
            else
                _placementManager.Clear();

            _constructionView.BuildButtonPressed = pressed;
        }

        private void UpdateGhostPlacement()
        {
            if (_selected == null)
                return;

            if (_selected.Type != ConstructionType.Structure)
            {
                _placementManager.Clear();
                return;
            }

            var constructSystem = _systemManager.GetEntitySystem<ConstructionSystem>();

            _placementManager.BeginPlacing(new PlacementInformation()
            {
                IsTile = false,
                PlacementOption = _selected.PlacementMode,
            }, new ConstructionPlacementHijack(constructSystem, _selected));

            _constructionView.BuildButtonPressed = true;
        }

        private void OnSystemLoaded(object? sender, SystemChangedArgs args)
        {
            if (args.System is ConstructionSystem system) SystemBindingChanged(system);
        }

        private void OnSystemUnloaded(object? sender, SystemChangedArgs args)
        {
            if (args.System is ConstructionSystem) SystemBindingChanged(null);
        }

        private void SystemBindingChanged(ConstructionSystem? newSystem)
        {
            if (newSystem is null)
            {
                if (_constructionSystem is null)
                    return;

                UnbindFromSystem();
            }
            else
            {
                if (_constructionSystem is null)
                {
                    BindToSystem(newSystem);
                    return;
                }

                UnbindFromSystem();
                BindToSystem(newSystem);
            }
        }

        private void BindToSystem(ConstructionSystem system)
        {
            _constructionSystem = system;
            system.ToggleCraftingWindow += SystemOnToggleMenu;
            system.FlipConstructionPrototype += SystemFlipConstructionPrototype;
            system.CraftingAvailabilityChanged += SystemCraftingAvailabilityChanged;
            system.ConstructionGuideAvailable += SystemGuideAvailable;
            if (_uiManager.GetActiveUIWidgetOrNull<GameTopMenuBar>() != null)
            {
                CraftingAvailable = system.CraftingEnabled;
            }
        }

        private void UnbindFromSystem()
        {
            var system = _constructionSystem;

            if (system is null)
                throw new InvalidOperationException();

            system.ToggleCraftingWindow -= SystemOnToggleMenu;
            system.FlipConstructionPrototype -= SystemFlipConstructionPrototype;
            system.CraftingAvailabilityChanged -= SystemCraftingAvailabilityChanged;
            system.ConstructionGuideAvailable -= SystemGuideAvailable;
            _constructionSystem = null;
        }

        private void SystemCraftingAvailabilityChanged(object? sender, CraftingAvailabilityChangedArgs e)
        {
            if (_uiManager.ActiveScreen == null)
                return;
            CraftingAvailable = e.Available;
        }

        private void SystemOnToggleMenu(object? sender, EventArgs eventArgs)
        {
            if (!CraftingAvailable)
                return;

            if (WindowOpen)
            {
                if (IsAtFront)
                {
                    WindowOpen = false;
                    _uiManager.GetActiveUIWidget<GameTopMenuBar>().CraftingButton.SetClickPressed(false); // This does not call CraftingButtonToggled
                }
                else
                    _constructionView.MoveToFront();
            }
            else
            {
                WindowOpen = true;
                _uiManager.GetActiveUIWidget<GameTopMenuBar>().CraftingButton.SetClickPressed(true); // This does not call CraftingButtonToggled
            }
        }

        private void SystemFlipConstructionPrototype(object? sender, EventArgs eventArgs)
        {
            if (!_placementManager.IsActive || _placementManager.Eraser)
            {
                return;
            }

            if (_selected == null || _selected.Mirror == null)
            {
                return;
            }

            _selected = _prototypeManager.Index<ConstructionPrototype>(_selected.Mirror);
            UpdateGhostPlacement();
        }

        private void SystemGuideAvailable(object? sender, string e)
        {
            if (!CraftingAvailable)
                return;

            if (!WindowOpen)
                return;

            if (_selected == null)
                return;

            PopulateInfo(_selected);
        }
    }
}
