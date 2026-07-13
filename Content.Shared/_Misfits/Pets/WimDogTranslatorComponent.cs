
// #Misfits Add - Wim Dog translator action component and event.

using Content.Shared.Actions;
using Robust.Shared.Prototypes;

namespace Content.Shared._Misfits.Pets;

[RegisterComponent]
public sealed partial class WimDogTranslatorComponent : Component
{
    [DataField]
    public EntProtoId ActionId = "ActionWimDogTranslator";

    [DataField]
    public EntityUid? ActionEntity;
}

public sealed partial class WimDogTranslatorActionEvent : EntityTargetActionEvent;
