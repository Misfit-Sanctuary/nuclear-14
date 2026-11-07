using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;


[ByRefEvent]
public record struct SolutionConsumedEv(Entity<SolutionComponent> SolutionSource, FixedPoint2 AmountToConsume,
    List<ProtoId<ReagentPrototype>> AllowedReagents, bool Handled = false);
