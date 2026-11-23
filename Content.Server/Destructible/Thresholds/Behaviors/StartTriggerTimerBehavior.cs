using Content.Server.Explosion.Components;
using Content.Server.Explosion.EntitySystems;
using JetBrains.Annotations;

namespace Content.Server.Destructible.Thresholds.Behaviors
{
    /// <summary>
    ///     Way for damage to set off trigger timers. Assumes you have <c>OnUseTimerTriggerComponent</c>
    /// AutomatedTimerComponent
    /// OnUseTimerTriggerComponent
    /// 
    /// </summary>
    [UsedImplicitly]
    [DataDefinition]
    public sealed partial class StartTriggerTimerBehavior : IThresholdBehavior
    {
        public void Execute(EntityUid owner, DestructibleSystem system, EntityUid? cause = null)
        {
            system.EntityManager.System<TriggerSystem>().StartTimer(owner, cause);
        }
    }
}
