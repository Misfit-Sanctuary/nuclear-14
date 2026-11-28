// #Cythisiax Added - Wells freeze while it snows. Any snowy weather on the map counts, even
// light snow that is only just starting. Trying to fill a bottle or bucket from a frozen
// well gives nothing and says the water is frozen. Once the snow stops the well works again.

using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Weather;
using Robust.Shared.Prototypes;

namespace Content.Shared._Misfits.WaterPlant;

public sealed partial class FreezesInSnowSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedWeatherSystem _weather = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<FreezesInSnowComponent, SolutionTransferAttemptEvent>(OnTransferAttempt);
    }

    private void OnTransferAttempt(Entity<FreezesInSnowComponent> ent, ref SolutionTransferAttemptEvent args)
    {
        // only stop water coming out of the well, pouring into it is left alone
        if (args.From != ent.Owner || args.CancelReason != null)
            return;

        if (IsSnowing(ent.Owner))
            args.Cancel(Loc.GetString(ent.Comp.FrozenMessage));
    }

    public bool IsSnowing(EntityUid uid)
    {
        if (_transform.GetMap(uid) is not { } mapUid || !TryComp<WeatherComponent>(mapUid, out var weather))
            return false;

        foreach (var (protoId, data) in weather.Weather)
        {
            if (_proto.TryIndex(protoId, out var proto) && proto.SnowAccumulation > 0f
                && _weather.GetPercent(data, mapUid) > 0f)
                return true;
        }

        return false;
    }
}
