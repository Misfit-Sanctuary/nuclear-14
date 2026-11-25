using System.Linq;
using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using Content.Shared.Damage;
using Content.Shared.DoAfter;
using Content.Shared.DragDrop;
using Content.Shared.Humanoid;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Verbs;

namespace Content.Shared._Misfits.Gallows;

public abstract partial class SharedGallowsSystem : EntitySystem
{
    // way to fat
    private const string TooHeavySpecies = "SuperMutant";

    [Dependency] protected readonly SharedDoAfterSystem DoAfter = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GallowsComponent, InteractHandEvent>(OnInteractHand,
            before: new[] { typeof(SharedBuckleSystem) });
        SubscribeLocalEvent<GallowsComponent, DragDropTargetEvent>(OnDragDropTarget,
            before: new[] { typeof(SharedBuckleSystem) });
        SubscribeLocalEvent<GallowsComponent, StrapAttemptEvent>(OnStrapAttempt);
        SubscribeLocalEvent<GallowsComponent, UnbuckledEvent>(OnUnbuckled);
        SubscribeLocalEvent<GallowsComponent, GetVerbsEvent<AlternativeVerb>>(OnGetVerbs);
    }

    private void OnInteractHand(Entity<GallowsComponent> ent, ref InteractHandEvent args)
    {
        if (args.Handled || ent.Comp.ForceBuckle)
            return;

        if (!TryComp<BuckleComponent>(args.User, out var buckle) || buckle.BuckledTo != null)
            return;

        if (!CanBeHanged(args.User))
            return;

        args.Handled = true;
        TryBeginAttach(ent, args.User, args.User);
    }

    private void OnDragDropTarget(Entity<GallowsComponent> ent, ref DragDropTargetEvent args)
    {
        if (args.Handled || ent.Comp.ForceBuckle)
            return;

        if (!CanBeHanged(args.Dragged))
            return;

        args.Handled = true;
        TryBeginAttach(ent, args.User, args.Dragged);
    }

    private void OnStrapAttempt(Entity<GallowsComponent> ent, ref StrapAttemptEvent args)
    {
        if (ent.Comp.ForceBuckle)
            return;

        var victim = args.Buckle.Owner;
        if (!CanBeHanged(victim))
            return;

        args.Cancelled = true;
        TryBeginAttach(ent, args.User ?? victim, victim);
    }

    private void TryBeginAttach(Entity<GallowsComponent> ent, EntityUid user, EntityUid victim)
    {
        if (ent.Comp.AttachDoAfter != null)
            return;

        if (IsTooHeavy(victim))
        {
            _popup.PopupClient(Loc.GetString("gallows-too-heavy", ("victim", victim)), ent, user);
            return;
        }

        AttemptAttach(ent, user, victim);
    }

    private bool IsTooHeavy(EntityUid victim)
    {
        return TryComp<HumanoidAppearanceComponent>(victim, out var appearance)
            && appearance.Species == TooHeavySpecies;
    }

    private void OnUnbuckled(Entity<GallowsComponent> ent, ref UnbuckledEvent args)
    {
        if (ent.Comp.AttachDoAfter is { } attachId)
        {
            DoAfter.Cancel(attachId);
            ent.Comp.AttachDoAfter = null;
        }

        if (ent.Comp.HangDoAfter is { } hangId)
        {
            DoAfter.Cancel(hangId);
            ent.Comp.HangDoAfter = null;
        }

        ent.Comp.Hanging = false;

        OnVictimRemoved(ent, args.Buckle.Owner);
    }

    private void OnGetVerbs(Entity<GallowsComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || ent.Comp.HangDoAfter != null || ent.Comp.Hanging
            || !TryComp<StrapComponent>(ent, out var strap)
            || GetFirstBuckled(strap) is not { } victim
            || !CanBeHanged(victim))
            return;

        var user = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Act = () => AttemptHang(ent, user, victim),
            Text = Loc.GetString("gallows-hang-verb"),
            Priority = 2,
        });
    }

    private EntityUid? GetFirstBuckled(StrapComponent strap)
    {
        if (strap.BuckledEntities.Count <= 0)
            return null;

        return strap.BuckledEntities.First();
    }

    public bool CanBeHanged(EntityUid victim)
    {
        if (!HasComp<DamageableComponent>(victim))
            return false;

        if (!TryComp<MobStateComponent>(victim, out var mobState))
            return false;

        return !_mobState.IsDead(victim, mobState);
    }

    protected virtual void AttemptAttach(Entity<GallowsComponent> ent, EntityUid user, EntityUid victim) { }

    protected virtual void AttemptHang(Entity<GallowsComponent> ent, EntityUid user, EntityUid victim) { }

    protected virtual void OnVictimRemoved(Entity<GallowsComponent> ent, EntityUid victim) { }
}
