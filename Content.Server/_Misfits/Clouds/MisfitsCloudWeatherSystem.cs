// #Misfits Add - Weather prestaging system: starts cloud cover ahead of rain/storm
// events and lets it linger after weather ends via the natural FadeOut phase.
// Only affects map entities with NCCloudLayerComponent.WeatherLinkEnabled = true.
using Content.Server._NC.Clouds;
using Content.Shared._NC.Clouds;
using Content.Shared.Weather;

namespace Content.Server._Misfits.Clouds;

/// <summary>
/// Polls maps that have both <see cref="NCCloudLayerComponent"/> (with
/// <c>WeatherLinkEnabled = true</c>) and <see cref="WeatherComponent"/>.
/// When a weather event becomes active the cloud layer is force-started so
/// clouds build BEFORE rain arrives.  When weather ends the clouds are
/// stopped normally, letting them linger through their FadeOutSeconds before
/// auto-scheduling resumes.
///
/// Only clouds this system triggered are stopped — auto-scheduled or
/// manually-triggered clouds are never interrupted.
/// </summary>
public sealed class MisfitsCloudWeatherSystem : EntitySystem
{
    [Dependency] private readonly NCCloudLayerSystem _cloudLayer = default!;

    // Throttle: poll once per second rather than every tick.
    private float _accumulator;
    private const float Interval = 1f;

    /// <summary>Map entities whose cloud layer was started by this system.</summary>
    private readonly HashSet<EntityUid> _weatherTriggered = new();

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _accumulator += frameTime;
        if (_accumulator < Interval)
            return;
        _accumulator -= Interval;

        var query = EntityQueryEnumerator<NCCloudLayerComponent, WeatherComponent>();
        while (query.MoveNext(out var uid, out var clouds, out var weather))
        {
            if (!clouds.WeatherLinkEnabled)
                continue;

            var weatherActive = IsWeatherRunning(weather);

            if (weatherActive)
            {
                // Weather is active — start clouds if they are idle and we haven't
                // started them yet (avoids double-triggering auto-running clouds).
                if (!_weatherTriggered.Contains(uid)
                    && (clouds.Phase == NCCloudLayerPhase.Inactive
                        || clouds.Phase == NCCloudLayerPhase.FadingOut))
                {
                    // null duration = indefinite; we control stop via weather end.
                    _cloudLayer.ForceStartClouds(uid, clouds, null);
                    _weatherTriggered.Add(uid);
                }
            }
            else
            {
                // Weather gone — only stop what WE started.
                if (!_weatherTriggered.Contains(uid))
                    continue;

                if (clouds.Phase == NCCloudLayerPhase.FadingIn
                    || clouds.Phase == NCCloudLayerPhase.Active)
                {
                    // ForceStopClouds clears ManualOverride and begins FadeOut;
                    // after fade-out completes the base system calls ScheduleNextAutomatic.
                    _cloudLayer.ForceStopClouds(uid, clouds);
                }

                _weatherTriggered.Remove(uid);
            }
        }
    }

    /// <summary>
    /// Returns true if at least one weather entry is in the Starting or Running state.
    /// </summary>
    private static bool IsWeatherRunning(WeatherComponent weather)
    {
        foreach (var (_, data) in weather.Weather)
        {
            if (data.State == WeatherState.Starting || data.State == WeatherState.Running)
                return true;
        }
        return false;
    }
}
