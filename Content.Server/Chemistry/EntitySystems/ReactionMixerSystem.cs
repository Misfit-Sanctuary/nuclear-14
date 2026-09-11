using Content.Shared.Chemistry.Reaction;
using Content.Shared.Chemistry.Components;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Nutrition.EntitySystems;
using Content.Server.Chemistry.Containers.EntitySystems;
using Content.Server.Popups;

namespace Content.Server.Chemistry.EntitySystems;

public sealed partial class ReactionMixerSystem : EntitySystem
{
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly SolutionContainerSystem _solutionContainers = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ReactionMixerComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<ReactionMixerComponent, ShakeEvent>(OnShake);
    }

	private void OnShake(Entity<ReactionMixerComponent> entity, ref ShakeEvent args)
    {
		TryMix(entity.AsNullable(), entity);
	}

	private void TryMix(Entity<ReactionMixerComponent?> entity, EntityUid target)
    {
		if (!Resolve(entity, ref entity.Comp, false))
            return;

        bool shakerFlag = false;
        if (entity.Comp.ReactionTypes.Contains("FakeShake"))
        {
            entity.Comp.ReactionTypes.Add("Shake");
            shakerFlag = true;
        }

		var mixAttemptEvent = new MixingAttemptEvent(entity);
        RaiseLocalEvent(entity, ref mixAttemptEvent);

        if (mixAttemptEvent.Cancelled)
        {
            return;
        }

        if (!_solutionContainers.TryGetMixableSolution(target, out var solution, out _))
            return;

        _solutionContainers.UpdateChemicals(solution.Value, true, entity.Comp);

        var afterMixingEvent = new AfterMixingEvent(entity, target);
        RaiseLocalEvent(entity, afterMixingEvent);

        if (shakerFlag)
            entity.Comp.ReactionTypes.Remove("Shake");

		return;
	}

    private void OnAfterInteract(Entity<ReactionMixerComponent> entity, ref AfterInteractEvent args)
    {
        if (!args.Target.HasValue || !args.CanReach)
            return;

        var mixAttemptEvent = new MixingAttemptEvent(entity);
        RaiseLocalEvent(entity, ref mixAttemptEvent);
        if (mixAttemptEvent.Cancelled)
        {
            return;
        }

        if (!_solutionContainers.TryGetMixableSolution(args.Target.Value, out var solution, out _))
            return;

        _popup.PopupEntity(Loc.GetString(entity.Comp.MixMessage, ("mixed", Identity.Entity(args.Target.Value, EntityManager)), ("mixer", Identity.Entity(entity.Owner, EntityManager))), args.User, args.User);

        _solutionContainers.UpdateChemicals(solution.Value, true, entity.Comp);

        var afterMixingEvent = new AfterMixingEvent(entity, args.Target.Value);
        RaiseLocalEvent(entity, afterMixingEvent);
    }
}
