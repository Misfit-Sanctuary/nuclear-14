// Rain drawn with its own shader, same idea as the snowfall one but rain falls fast and straight,
// leans with the wind, splashes on the ground and rolls through in heavier sheets.
// Thunderstorms flash the open sky and play thunder a moment later. Strike times come from the
// synced game clock so everyone on the map sees the same flash and hears the same roll.

using System.Numerics;
using Content.Client.Weather;
using Content.Shared.Weather;
using Robust.Client.Audio;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._Misfits.Weather;

public sealed partial class RainfallRenderer
{
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IGameTiming _timing = default!;

    private static readonly ProtoId<ShaderPrototype> RainfallShader = "Rainfall";
    private const float Wrap = 8192f;

    private readonly ShaderInstance _shader;
    private readonly Dictionary<string, RainfallState> _states = new();

    private sealed class RainfallState
    {
        public float Fall;
        public float SheetDrift;
        public float Slant;
        public TimeSpan Last;
        public float Seed;
    }

    public RainfallRenderer()
    {
        IoCManager.InjectDependencies(this);
        _shader = _proto.Index(RainfallShader).InstanceUnique();
    }

    public void Draw(in OverlayDrawArgs args, WeatherPrototype proto, RainfallLook look, float alpha, Texture mask, Matrix3x2 invMatrix)
    {
        if (args.Viewport.Eye is not { } eye)
            return;

        var now = _timing.RealTime;
        if (!_states.TryGetValue(proto.ID, out var state))
        {
            state = new RainfallState { Last = now, Seed = _states.Count * 17.3f };
            _states[proto.ID] = state;
        }

        var dt = (float) Math.Clamp((now - state.Last).TotalSeconds, 0, 0.1);
        state.Last = now;

        // gusts push the rain over and then let it settle back
        var t = (float) now.TotalSeconds + state.Seed;
        var wave = 0.6f * MathF.Sin(t * 0.17f) + 0.4f * MathF.Sin(t * 0.43f + 2.1f);
        var direction = look.Wind >= 0f ? 1f : -1f;
        var gust = MathF.Max(wave, 0f);
        var wind = look.Wind + direction * look.Gust * gust * gust;

        var fallSpeed = MathF.Max(look.FallSpeed, 0.5f);
        var targetSlant = wind / fallSpeed;
        state.Slant += (targetSlant - state.Slant) * Math.Clamp(dt * 1.5f, 0f, 1f);

        state.Fall = (state.Fall + fallSpeed * dt) % Wrap;
        state.SheetDrift = (state.SheetDrift + wind * 0.35f * dt) % Wrap;

        var eyePos = eye.Position.Position;
        var size = args.Viewport.Size;
        var eyePixel = Vector2.Transform(eyePos, invMatrix);
        var pixelsPerTile = EyeManager.PixelsPerMeter * args.Viewport.RenderScale.X / eye.Zoom.X;

        _shader.SetParameter("MASK_TEXTURE", mask);
        _shader.SetParameter("eyePos", eyePos);
        _shader.SetParameter("eyePixel", new Vector2(eyePixel.X, size.Y - eyePixel.Y));
        _shader.SetParameter("pixelsPerTile", pixelsPerTile);
        _shader.SetParameter("intensity", alpha);
        _shader.SetParameter("fall", state.Fall);
        _shader.SetParameter("slant", state.Slant);
        _shader.SetParameter("sheetDrift", state.SheetDrift);
        _shader.SetParameter("density", look.Density);
        _shader.SetParameter("streakLength", look.StreakLength);
        _shader.SetParameter("haze", look.Haze);
        _shader.SetParameter("splash", look.Splash);
        _shader.SetParameter("flash", RainLightning.Flash(_timing.CurTime, look.Lightning));
        _shader.SetParameter("rainColor", new Vector3(look.RainColor.R, look.RainColor.G, look.RainColor.B));
        _shader.SetParameter("hazeColor", new Vector3(look.HazeColor.R, look.HazeColor.G, look.HazeColor.B));
        _shader.SetParameter("clearRadius", proto.VisibilityClearRadius);
        _shader.SetParameter("clearBuffer", proto.VisibilityClearBuffer);

        var handle = args.WorldHandle;
        handle.SetTransform(Matrix3x2.Identity);
        handle.UseShader(_shader);
        handle.DrawRect(args.WorldBounds, Color.White);
        handle.UseShader(null);
    }
}

public sealed partial class RainThunderSystem : EntitySystem
{
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private WeatherSystem _weather = default!;

    private const long MaxCatchUp = 40;

    private long _lastSlot = long.MinValue;
    private readonly List<(TimeSpan Due, SoundSpecifier Sound, float Volume)> _pending = new();

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        var now = _timing.CurTime;
        var slot = RainLightning.Slot(now);
        var from = _lastSlot == long.MinValue || slot - _lastSlot > MaxCatchUp ? slot : _lastSlot + 1;

        if (slot > _lastSlot)
        {
            QueueStrikes(from, slot);
            _lastSlot = slot;
        }

        for (var i = _pending.Count - 1; i >= 0; i--)
        {
            var (due, sound, volume) = _pending[i];
            if (due > now)
                continue;

            _pending.RemoveAt(i);
            _audio.PlayGlobal(sound, Filter.Local(), false, sound.Params.AddVolume(volume));
        }
    }

    private void QueueStrikes(long from, long to)
    {
        if (_player.LocalEntity is not { } player ||
            Transform(player).MapUid is not { } mapUid ||
            !TryComp<WeatherComponent>(mapUid, out var weather))
        {
            return;
        }

        foreach (var (protoId, data) in weather.Weather)
        {
            if (data.State != WeatherState.Starting && data.State != WeatherState.Running)
                continue;

            if (!_proto.TryIndex<WeatherPrototype>(protoId, out var proto) ||
                proto.Rainfall is not { Lightning: > 0f, Thunder: { } thunder } rain)
            {
                continue;
            }

            var strength = _weather.GetPercent(data, mapUid);
            for (var slot = from; slot <= to; slot++)
            {
                if (!RainLightning.Strikes(slot, rain.Lightning))
                    continue;

                // close strikes come quick and loud, far ones roll in later and quieter
                var delay = RainLightning.ThunderDelay(slot);
                var volume = 3f - delay * 3f + (strength - 1f) * 8f;
                var due = TimeSpan.FromSeconds(slot * RainLightning.SlotSeconds + delay);
                _pending.Add((due, thunder, volume));
            }
        }
    }
}
