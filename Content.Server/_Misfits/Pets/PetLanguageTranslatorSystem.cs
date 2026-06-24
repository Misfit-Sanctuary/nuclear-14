// #Misfits Add - Pet action for teaching targets to understand their language.

using Content.Server.Language;
using Content.Shared._Misfits.Pets;
using Content.Shared.Actions;
using Content.Shared.Language;
using Content.Shared.Language.Components;
using Content.Shared.Popups;
using Robust.Shared.Prototypes;

namespace Content.Server._Misfits.Pets;

public sealed class PetLanguageTranslatorSystem : EntitySystem
{
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly LanguageSystem _language = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<PetLanguageTranslatorComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<PetLanguageTranslatorComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<PetLanguageTranslatorComponent, PetLanguageTranslatorActionEvent>(OnTranslate);
    }

    private void OnMapInit(Entity<PetLanguageTranslatorComponent> ent, ref MapInitEvent args)
    {
        _actions.AddAction(ent, ref ent.Comp.ActionEntity, ent.Comp.ActionId);
    }

    private void OnShutdown(Entity<PetLanguageTranslatorComponent> ent, ref ComponentShutdown args)
    {
        _actions.RemoveAction(ent, ent.Comp.ActionEntity);
    }

    private void OnTranslate(Entity<PetLanguageTranslatorComponent> ent, ref PetLanguageTranslatorActionEvent args)
    {
        if (args.Handled)
            return;

        if (!HasComp<LanguageSpeakerComponent>(args.Target))
        {
            _popup.PopupEntity(Loc.GetString("pet-language-translator-invalid"), args.Performer, args.Performer);
            return;
        }

        if (_language.CanUnderstand(args.Target, ent.Comp.Language))
        {
            _popup.PopupEntity(Loc.GetString("pet-language-translator-already", ("language", ent.Comp.LanguageName)), args.Performer, args.Performer);
            return;
        }

        _language.AddLanguage(args.Target, ent.Comp.Language, addSpoken: false);
        _popup.PopupEntity(Loc.GetString("pet-language-translator-success", ("language", ent.Comp.LanguageName)), args.Target, args.Target);
        _popup.PopupEntity(Loc.GetString("pet-language-translator-success-user", ("target", args.Target), ("language", ent.Comp.LanguageName)), args.Target, args.Performer);
        args.Handled = true;
    }
}
