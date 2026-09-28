// SPDX-License-Identifier: AGPL-3.0-or-later
// Adapted from Goob Station / Trauma Station

using Content.Shared.DeviceLinking;
using Content.Shared.Research.Prototypes;

namespace Content.Server._Misfits.Factory.Lathe;

[RegisterComponent]
public sealed partial class LatheAutomationComponent : Component
{
    [ViewVariables]
    public LatheRecipePrototype? LastRecipe;

    [DataField]
    public int Quantity = 1;

    [DataField]
    public ProtoId<SinkPortPrototype> PrintPort = "LathePrint";

    [DataField]
    public ProtoId<SinkPortPrototype> SetRecipePort = "LatheSetRecipe";

    [DataField]
    public ProtoId<SinkPortPrototype> QuantityPort = "LatheQuantity";

    [DataField]
    public ProtoId<SourcePortPrototype> CurrentRecipePort = "LatheCurrentRecipe";
}
