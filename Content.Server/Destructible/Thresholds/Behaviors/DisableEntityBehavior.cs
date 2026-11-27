using JetBrains.Annotations;


namespace Content.Server.Destructible.Thresholds.Behaviors;

[UsedImplicitly]
[DataDefinition]
public sealed partial class DisableEntityBehavior : IThresholdBehavior
{
    public void Execute(EntityUid owner, DestructibleSystem system, EntityUid? cause = null)
    {
        if (system.EntityManager.HasComponent<DisabledEntityComponent>(owner))
            return;

        system.EntityManager.EnsureComponent<DisabledEntityComponent>(owner);

    }
}
