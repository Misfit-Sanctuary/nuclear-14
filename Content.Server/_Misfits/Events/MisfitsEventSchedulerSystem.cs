using Content.Server.GameTicking.Rules;
using Content.Shared.CCVar;
using Content.Shared.GameTicking.Components;
using Robust.Shared.Configuration;

namespace Content.Server._Misfits.Events;

// Rolls a weighted event from the pool on a randomised interval.
public sealed class MisfitsEventSchedulerSystem : GameRuleSystem<MisfitsEventSchedulerComponent>
{
    public const string SchedulerRule = "MisfitsEventScheduler";

    // Retry delay when the pool had nothing eligible.
    private const float EmptyPoolRetry = 60f;

    [Dependency] private readonly MisfitsEventSystem _events = default!;
    [Dependency] private readonly IConfigurationManager _config = default!;

    // Server-side master switch (misfits.events_enabled); archived, so it survives restarts.
    public bool EventsEnabled { get; private set; }

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_config, CCVars.MisfitsEventsEnabled, value => EventsEnabled = value, true);
    }

    protected override void Started(EntityUid uid, MisfitsEventSchedulerComponent component,
        GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);

        component.TimeUntilNextEvent = component.FirstEventDelay;
    }

    protected override void ActiveTick(EntityUid uid, MisfitsEventSchedulerComponent component,
        GameRuleComponent gameRule, float frameTime)
    {
        base.ActiveTick(uid, component, gameRule, frameTime);

        if (!EventsEnabled || !component.Enabled)
            return;

        component.TimeUntilNextEvent -= frameTime;
        if (component.TimeUntilNextEvent > 0f)
            return;

        component.TimeUntilNextEvent = _events.RunRandomEvent() != null
            ? RobustRandom.NextFloat(component.MinInterval, component.MaxInterval)
            : EmptyPoolRetry;
    }

    // The running scheduler, if the current preset has one.
    public Entity<MisfitsEventSchedulerComponent>? GetScheduler()
    {
        var query = QueryActiveRules();
        while (query.MoveNext(out var uid, out _, out var component, out _))
        {
            return (uid, component);
        }

        return null;
    }

    // Sets the interval between automatic rolls; false when no scheduler is running.
    public bool SetInterval(float min, float max)
    {
        if (EnsureScheduler() is not { } scheduler)
            return false;

        scheduler.Comp.MinInterval = Math.Min(min, max);
        scheduler.Comp.MaxInterval = Math.Max(min, max);
        return true;
    }

    // Toggles automatic rolls; false when no scheduler is running.
    public bool SetEnabled(bool enabled)
    {
        if (EnsureScheduler() is not { } scheduler)
            return false;

        scheduler.Comp.Enabled = enabled;
        return true;
    }

    // Sets the time until the next automatic roll; false when no scheduler is running.
    public bool SetNextRoll(float seconds)
    {
        if (EnsureScheduler() is not { } scheduler)
            return false;

        scheduler.Comp.TimeUntilNextEvent = Math.Max(0f, seconds);
        return true;
    }

    // The running scheduler, or a freshly started one, so the setters work in any preset.
    private Entity<MisfitsEventSchedulerComponent>? EnsureScheduler()
    {
        if (GetScheduler() is { } existing)
            return existing;

        var rule = GameTicker.AddGameRule(SchedulerRule);
        if (!TryComp<MisfitsEventSchedulerComponent>(rule, out var component))
        {
            Log.Error($"Prototype {SchedulerRule} is missing a MisfitsEventSchedulerComponent.");
            Del(rule);
            return null;
        }

        GameTicker.StartGameRule(rule);
        return (rule, component);
    }
}
