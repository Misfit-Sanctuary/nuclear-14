// #Misfits Add - Central dispatcher for martial arts combo events
// RobustToolbox only allows one directed subscription per (Component, Event) pair across all systems.
// This system holds the single subscription and routes to the correct style system based on MartialArtsForm.
using Content.Server._Misfits.MartialArts.Styles;
using Content.Shared._Misfits.MartialArts;
using Content.Shared.Weapons.Ranged.Events;

namespace Content.Server._Misfits.MartialArts;

/// <summary>
/// Routes <see cref="MisfitsComboTriggeredEvent"/> to the correct style system.
/// Also routes <see cref="ShotAttemptedEvent"/> for styles that suppress gunfire (TribalWarrior).
/// </summary>
public sealed class MisfitsComboDispatcherSystem : EntitySystem
{
    // Style system references — resolved lazily via EntitySystemManager
    [Dependency] private readonly LegionGladiatorialSystem _legion = default!;
    [Dependency] private readonly RangerCombatSystem _ranger = default!;
    [Dependency] private readonly DesertSurvivalSystem _desert = default!;
    [Dependency] private readonly WastelandStreetFightingSystem _wasteland = default!;
    [Dependency] private readonly TribalWarriorSystem _tribal = default!;
    [Dependency] private readonly ShadowStrikeSystem _shadow = default!;

    public override void Initialize()
    {
        base.Initialize();

        // Single directed subscription for combo events — dispatched to style systems by form
        SubscribeLocalEvent<MartialArtsKnowledgeComponent, MisfitsComboTriggeredEvent>(OnComboTriggered);

        // Single directed subscription for gun suppression (only TribalWarrior uses this currently)
        SubscribeLocalEvent<MartialArtsKnowledgeComponent, ShotAttemptedEvent>(OnShotAttempted);
    }

    private void OnComboTriggered(EntityUid uid, MartialArtsKnowledgeComponent comp, MisfitsComboTriggeredEvent args)
    {
        // Route to the correct style handler based on the entity's active martial art form
        switch (comp.MartialArtsForm)
        {
            case MisfitsMartialArtsForms.LegionGladiatorial:
                _legion.OnComboTriggered(uid, comp, args);
                break;
            case MisfitsMartialArtsForms.RangerCombatTechnique:
                _ranger.OnComboTriggered(uid, comp, args);
                break;
            case MisfitsMartialArtsForms.DesertSurvivalFighting:
                _desert.OnComboTriggered(uid, comp, args);
                break;
            case MisfitsMartialArtsForms.WastelandStreetFighting:
                _wasteland.OnComboTriggered(uid, comp, args);
                break;
            case MisfitsMartialArtsForms.TribalWarriorStyle:
                _tribal.OnComboTriggered(uid, comp, args);
                break;
            case MisfitsMartialArtsForms.ShadowStrike:
                _shadow.OnComboTriggered(uid, comp, args);
                break;
        }
    }

    private void OnShotAttempted(EntityUid uid, MartialArtsKnowledgeComponent comp, ref ShotAttemptedEvent args)
    {
        // Only Tribal Warrior suppresses gunfire — route directly
        if (comp.MartialArtsForm == MisfitsMartialArtsForms.TribalWarriorStyle)
            _tribal.OnShotAttempted(uid, comp, ref args);
    }
}
