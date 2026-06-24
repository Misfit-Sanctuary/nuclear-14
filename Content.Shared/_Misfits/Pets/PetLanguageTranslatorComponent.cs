// #Misfits Add - Pet language translator action component and event.

using Content.Shared.Actions;
using Content.Shared.Language;
using Robust.Shared.Prototypes;

namespace Content.Shared._Misfits.Pets;

[RegisterComponent]
public sealed partial class PetLanguageTranslatorComponent : Component
{
    [DataField]
    public EntProtoId ActionId = "ActionCatTranslator";

    [DataField]
    public ProtoId<LanguagePrototype> Language = "Cat";

    [DataField]
    public string LanguageName = "Cat";

    [DataField]
    public EntityUid? ActionEntity;
}

public sealed partial class PetLanguageTranslatorActionEvent : EntityTargetActionEvent;
