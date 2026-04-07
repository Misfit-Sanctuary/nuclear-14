// #Misfits Add - System that grants martial arts knowledge from Training Manuals and job startup components
using Content.Shared._Misfits.MartialArts;
using Content.Shared.Interaction.Events;
using Content.Shared.Popups;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Prototypes;

namespace Content.Server._Misfits.MartialArts;

/// <summary>
/// Grants a martial arts form to an entity when a <see cref="GrantMartialArtKnowledgeComponent"/> is used in hand.
/// Also handles job-default styles: Legion Centurion/Legate receive LegionGladiatorial,
/// Veteran Ranger/Ranger Chief receive RangerCombatTechnique — these use ComponentStartup since
/// their GrantXxx components are added via AddComponentSpecial in YAML.
/// </summary>
public sealed class GrantMartialArtsSystem : EntitySystem
{
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly SharedMisfitsMartialArtsSystem _martialArts = default!;

    public override void Initialize()
    {
        base.Initialize();

        // RobustToolbox requires concrete registered component types for directed subscriptions —
        // abstract base classes are not valid. Subscribe to each concrete grant component individually.

        // Training manuals (UseInHand path):
        SubscribeLocalEvent<GrantLegionGladiatorialComponent, UseInHandEvent>(OnUseInHand); // #Misfits Fix - Was missing, manuals didn't work
        SubscribeLocalEvent<GrantRangerCombatComponent, UseInHandEvent>(OnUseInHand); // #Misfits Fix - Was missing, manuals didn't work
        SubscribeLocalEvent<GrantDesertSurvivalComponent, UseInHandEvent>(OnUseInHand);
        SubscribeLocalEvent<GrantWastelandStreetFightingComponent, UseInHandEvent>(OnUseInHand);
        SubscribeLocalEvent<GrantTribalWarriorComponent, UseInHandEvent>(OnUseInHand);
        SubscribeLocalEvent<GrantShadowStrikeComponent, UseInHandEvent>(OnUseInHand);

        // Job-default startup path (added via AddComponentSpecial in YAML):
        SubscribeLocalEvent<GrantLegionGladiatorialComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<GrantRangerCombatComponent, ComponentStartup>(OnStartup);

        // Also support manuals being added via roundstart component in edge cases:
        SubscribeLocalEvent<GrantDesertSurvivalComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<GrantWastelandStreetFightingComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<GrantTribalWarriorComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<GrantShadowStrikeComponent, ComponentStartup>(OnStartup);
    }

    /// <summary>
    /// When a mob-owned grant component starts (e.g. added by AddComponentSpecial from a profile/job),
    /// grant the style to the mob itself without consuming anything.
    /// We check that the entity is a mob (has MobStateComponent) to skip item-entity startups.
    /// </summary>
    private void OnStartup<TComp>(EntityUid uid, TComp comp, ComponentStartup args)
        where TComp : GrantMartialArtKnowledgeComponent
    {
        // Only auto-grant if this is a mob (not a carried item)
        if (!HasComp<Robust.Shared.GameObjects.TransformComponent>(uid))
            return;

        // Items will be handled on UseInHand instead
        if (HasComp<Content.Shared.Item.ItemComponent>(uid))
            return;

        TryGrantMartialArt(uid, uid, comp);
    }

    /// <summary>
    /// When an item with a GrantMartialArtKnowledgeComponent is used in hand,
    /// grant the form to the user and optionally consume the item.
    /// </summary>
    private void OnUseInHand<TComp>(EntityUid uid, TComp comp, UseInHandEvent args)
        where TComp : GrantMartialArtKnowledgeComponent
    {
        if (args.Handled)
            return;

        var user = args.User;

        if (!TryGrantMartialArt(uid, user, comp))
            return;

        args.Handled = true;

        // Play a sound cue on learn
        if (comp.SoundOnUse != null)
            _audio.PlayEntity(comp.SoundOnUse, user, user);

        // Consume the item unless MultiUse
        if (!comp.MultiUse)
        {
            // If there is a residue proto (e.g. "WornScrollAsh"), spawn it
            if (!string.IsNullOrEmpty(comp.SpawnedProto))
                Spawn(comp.SpawnedProto, Transform(uid).Coordinates);

            QueueDel(uid);
        }
    }

    /// <summary>
    /// Core: grants <paramref name="comp"/>'s form to <paramref name="recipient"/>.
    /// Returns false if the recipient already has the form (and shows a popup).
    /// </summary>
    private bool TryGrantMartialArt(EntityUid item, EntityUid recipient, GrantMartialArtKnowledgeComponent comp)
    {
        // If the recipient already knows a martial art, block (one form per entity for now)
        if (TryComp<MartialArtsKnowledgeComponent>(recipient, out var existing))
        {
            _popup.PopupEntity(Loc.GetString("martial-arts-already-trained", ("form", existing.MartialArtsForm)), item, recipient, PopupType.Small);
            return false;
        }

        // Add the knowledge component
        var knowledge = AddComp<MartialArtsKnowledgeComponent>(recipient);
        knowledge.MartialArtsForm = comp.MartialArtsForm;

        // Add the combo performer component and load the initial combo list from the style prototype
        var performer = AddComp<CanPerformComboComponent>(recipient);

        // Prefer combo list from grant component; fall back to the style prototype
        if (comp.RoundstartCombos != null && _proto.TryIndex(comp.RoundstartCombos.Value, out var directComboList))
        {
            _martialArts.LoadCombos(recipient, performer, directComboList);
        }
        else
        {
            // Look up the misfitsMartialArt prototype for this form to get its combo list
            foreach (var styleProto in _proto.EnumeratePrototypes<MisfitsMartialArtPrototype>())
            {
                if (styleProto.MartialArtsForm != comp.MartialArtsForm)
                    continue;

                if (_proto.TryIndex(styleProto.RoundstartCombos, out var styleComboList))
                    _martialArts.LoadCombos(recipient, performer, styleComboList);
                break;
            }
        }

        // Show the learn popup on the recipient
        if (comp.LearnMessage != null)
            _popup.PopupEntity(Loc.GetString(comp.LearnMessage.Value), recipient, recipient, PopupType.Large);

        return true;
    }
}
