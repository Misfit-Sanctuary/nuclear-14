// SPDX-License-Identifier: AGPL-3.0-or-later
// Adapted from Goob Station / Trauma Station

using Content.Server.DeviceLinking.Components;
using Content.Server.DeviceLinking.Events;
using Content.Server.DeviceLinking.Systems;
using Content.Server.DeviceNetwork;
using Content.Server.Lathe;
using Content.Shared.DeviceNetwork;
using Content.Shared.Lathe;
using Content.Shared.Research.Prototypes;

namespace Content.Server._Misfits.Factory.Lathe;

public sealed partial class LatheAutomationSystem : EntitySystem
{
    [Dependency] private LatheSystem _lathe = default!;
    [Dependency] private DeviceLinkSystem _device = default!;

    private const string QuantityKey = "quantity";
    private const string RecipeKey = "recipe";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<LatheAutomationComponent, LatheStartPrintingEvent>(OnStartPrinting);
        SubscribeLocalEvent<LatheAutomationComponent, SignalReceivedEvent>(OnSignalReceived);
    }

    private void OnStartPrinting(EntityUid uid, LatheAutomationComponent comp, LatheStartPrintingEvent args)
    {
        SetRecipe((uid, comp), args.Recipe);
    }

    private void OnSignalReceived(Entity<LatheAutomationComponent> ent, ref SignalReceivedEvent args)
    {
        if (args.Port == ent.Comp.QuantityPort)
        {
            if (args.Data?.TryGetValue(QuantityKey, out var rawQuantity) == true && rawQuantity is int quantity && quantity >= 1)
                ent.Comp.Quantity = quantity;
            return;
        }

        if (args.Port == ent.Comp.SetRecipePort)
        {
            var recipeId = args.Data?.TryGetValue(RecipeKey, out var rawRecipe) == true ? rawRecipe as string : null;
            // invalid ids will reset it to null
            // lathe system checks if the recipe is allowed on this lathe in CanProduce, don't need to check it here
            ProtoMan.TryIndex<LatheRecipePrototype>(recipeId ?? string.Empty, out var recipe);
            SetRecipe(ent, recipe);
            return;
        }

        var state = SignalState.Momentary;
        args.Data?.TryGetValue(DeviceNetworkConstants.LogicState, out state);
        if (state == SignalState.Low || args.Port == ent.Comp.PrintPort)
            TryPrintLast(ent);
    }

    private void SetRecipe(Entity<LatheAutomationComponent> ent, LatheRecipePrototype? recipe)
    {
        if (ent.Comp.LastRecipe == recipe)
            return;

        ent.Comp.LastRecipe = recipe;
        var payload = new NetworkPayload { [RecipeKey] = recipe?.ID ?? string.Empty };
        _device.InvokePort(ent.Owner, ent.Comp.CurrentRecipePort, payload);
    }

    private void TryPrintLast(Entity<LatheAutomationComponent> ent)
    {
        if (ent.Comp.LastRecipe is not {} recipe)
            return;

        for (var i = 0; i < Math.Max(1, ent.Comp.Quantity); i++)
        {
            if (!_lathe.TryAddToQueue(ent.Owner, recipe))
                break;
        }

        _lathe.TryStartProducing(ent.Owner);
    }
}
