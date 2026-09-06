using Content.Server._Misfits.SpecialStats;
using Content.Shared._Misfits.Scavenging;
using Content.Shared._Misfits.Talents.Components;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Random.Helpers;
using Content.Shared.Storage;
using Content.Shared.Storage.Components;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Misfits.Scavenging;
// TODO: make more general not just junk
/// <summary>
/// Gives mapped junk piles a deliberate, shared search interaction and a cooldown that begins on a successful search.
/// </summary>
public sealed class JunkPileSearchSystem : EntitySystem
{
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SpecialLuckSystem _luck = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<JunkPileSearchableComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<JunkPileSearchableComponent, InteractHandEvent>(OnInteractHand);
        SubscribeLocalEvent<JunkPileSearchableComponent, JunkPileSearchDoAfterEvent>(OnSearchComplete);
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<JunkPileSearchableComponent>();
        while (query.MoveNext(out var uid, out var component))
        {
            if (component.CooldownEnd == TimeSpan.Zero || component.CooldownEnd > _timing.CurTime)
                continue;

            component.CooldownEnd = TimeSpan.Zero;
            component.FinderSearchUsed = false;
            UpdateAppearance((uid, component), false);
        }
    }

    private void OnStartup(Entity<JunkPileSearchableComponent> ent, ref ComponentStartup args)
    {
        UpdateAppearance(ent, false);
    }

    private void OnInteractHand(Entity<JunkPileSearchableComponent> ent, ref InteractHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        StartSearch(ent, args.User);
    }

    private void StartSearch(Entity<JunkPileSearchableComponent> ent, EntityUid user)
    {
        if (ent.Comp.CooldownEnd > _timing.CurTime)
        {
            if (HasComp<TraitJunkerFinderComponent>(user) && !ent.Comp.FinderSearchUsed)
            {
                StartDoAfter(ent, user, true);
                return;
            }

            var remaining = (int) Math.Ceiling((ent.Comp.CooldownEnd - _timing.CurTime).TotalMinutes);
            _popup.PopupEntity(Loc.GetString("junk-pile-search-cooldown", ("minutes", remaining)), ent, user, PopupType.SmallCaution);
            return;
        }

        StartDoAfter(ent, user, false);
    }

    private void StartDoAfter(Entity<JunkPileSearchableComponent> ent, EntityUid user, bool finderSearch)
    {
        _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, user, ent.Comp.SearchDuration,
            new JunkPileSearchDoAfterEvent { FinderSearch = finderSearch }, ent, ent)
        {
            BlockDuplicate = true,
            BreakOnDamage = true,
            BreakOnMove = true,
            DistanceThreshold = 2f,
        });
    }

    private void OnSearchComplete(Entity<JunkPileSearchableComponent> ent, ref JunkPileSearchDoAfterEvent args)
    {
        if (args.Cancelled)
            return;

        var onCooldown = ent.Comp.CooldownEnd > _timing.CurTime;
        if (onCooldown && (!args.FinderSearch || ent.Comp.FinderSearchUsed))
            return;

        if (!TryComp<StorageFillComponent>(ent, out var fill) || fill.Contents.Count == 0)
            return;

        if (onCooldown)
        {
            ent.Comp.FinderSearchUsed = true;
        }
        else
        {
            ent.Comp.CooldownEnd = _timing.CurTime + TimeSpan.FromSeconds(ent.Comp.CooldownSeconds);
            ent.Comp.FinderSearchUsed = false;
            UpdateAppearance(ent, true);
        }
        var coordinates = Transform(ent).Coordinates;
        foreach (var prototype in EntitySpawnCollection.GetSpawns(fill.Contents, _random))
        {
            Spawn(prototype, coordinates);
        }

        if (TryComp<LuckJunkBonusComponent>(ent, out var luck))
            _luck.TryGrantJunkBonus((ent, luck), args.User);

        _popup.PopupEntity(Loc.GetString(args.FinderSearch
            ? "junk-pile-search-finder-complete"
            : "junk-pile-search-complete"), ent, args.User);
    }

    private void UpdateAppearance(Entity<JunkPileSearchableComponent> ent, bool depleted)
    {
        _appearance.SetData(ent, JunkPileVisuals.Depleted, depleted);
    }
}
