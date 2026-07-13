// #Misfits Add - Wim dog action for teaching targets to understand dog.

using Content.Server.Language;
using Content.Shared._Misfits.Pets;
using Content.Shared.Actions;
using Content.Shared.Language;
using Content.Shared.Language.Components;
using Content.Shared.Popups;
using Robust.Shared.Prototypes;

namespace Content.Server._Misfits.Pets;

public sealed class WimDogTranslatorSystem : EntitySystem
{
    private static readonly ProtoId<LanguagePrototype> N14Dog = "Dog";

    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly LanguageSystem _language = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<WimDogTranslatorComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<WimDogTranslatorComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<WimDogTranslatorComponent, WimDogTranslatorActionEvent>(OnTranslate);
    }

    private void OnMapInit(Entity<WimDogTranslatorComponent> ent, ref MapInitEvent args)
    {
        _actions.AddAction(ent, ref ent.Comp.ActionEntity, ent.Comp.ActionId);
    }

    private void OnShutdown(Entity<WimDogTranslatorComponent> ent, ref ComponentShutdown args)
    {
        _actions.RemoveAction(ent, ent.Comp.ActionEntity);
    }

    private void OnTranslate(Entity<WimDogTranslatorComponent> ent, ref WimDogTranslatorActionEvent args)
    {
        if (args.Handled)
            return;

        if (!HasComp<LanguageSpeakerComponent>(args.Target))
        {
            _popup.PopupEntity(Loc.GetString("wim-dog-translator-invalid"), args.Performer, args.Performer);
            return;
        }

        if (_language.CanUnderstand(args.Target, N14Dog))
        {
            _popup.PopupEntity(Loc.GetString("wim-dog-translator-already"), args.Performer, args.Performer);
            return;
        }

        _language.AddLanguage(args.Target, N14Dog, addSpoken: false);
        _popup.PopupEntity(Loc.GetString("wim-dog-translator-success"), args.Target, args.Target);
        _popup.PopupEntity(Loc.GetString("wim-dog-translator-success-user", ("target", args.Target)), args.Target, args.Performer);
        args.Handled = true;
    }
}
