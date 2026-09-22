// SPDX-License-Identifier: AGPL-3.0-or-later
// Adapted from Goob Station / Trauma Station

using Content.Shared._Misfits.Factory;
using Content.Shared.Construction.Prototypes;
using Content.Shared.Whitelist;
using Robust.Shared.Prototypes;

namespace Content.Client._Misfits.Factory.UI;

public sealed partial class ConstructorBUI : BoundUserInterface
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    private readonly EntityWhitelistSystem _whitelist;

    private ConstructorRecipeWindow? _window;

    public ConstructorBUI(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
        _whitelist = EntMan.System<EntityWhitelistSystem>();
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<ConstructorRecipeWindow>();

        var recipes = new List<ConstructionPrototype>();
        foreach (var recipe in _proto.EnumeratePrototypes<ConstructionPrototype>())
        {
            if (recipe.Hide)
                continue;

            if (PlayerManager.LocalEntity is {} user && _whitelist.IsWhitelistFail(recipe.EntityWhitelist, user))
                continue;

            recipes.Add(recipe);
        }

        recipes.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.InvariantCulture));
        _window.SetRecipes(recipes);

        _window.RecipeSelected += recipe => SendPredictedMessage(new ConstructorSetProtoMessage(recipe.ID));
    }
}
