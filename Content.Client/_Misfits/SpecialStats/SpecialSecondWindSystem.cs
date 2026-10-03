using Content.Shared._Misfits.Special.Components;
using Content.Shared._Misfits.SpecialStats;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;

namespace Content.Client._Misfits.SpecialStats;

public sealed class SpecialSecondWindSystem : SharedSpecialSecondWindSystem
{
    [Dependency] private readonly IOverlayManager _overlayManager = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;

    private static readonly SoundSpecifier HeartbeatSound = new SoundPathSpecifier("/Audio/_Misfits/Effects/second_wind_heartbeat.ogg");

    // secs between beats, speeds up as it goes on
    private const float StartBeatInterval = 0.556f;
    private const float FinalBeatSpeed = 1.8f;

    private SpecialSecondWindOverlay _overlay = default!;

    private TimeSpan _seenActiveUntil;
    private TimeSpan _startedAt;
    private TimeSpan _lastBeatAt;
    private TimeSpan _nextBeatAt;

    public bool LocalActive { get; private set; }
    public float Elapsed { get; private set; }
    public float Remaining { get; private set; }
    public float SinceBeat { get; private set; }

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SpecialComponent, AfterAutoHandleStateEvent>(OnAfterHandleState);

        _overlay = new SpecialSecondWindOverlay(this);
        _overlayManager.AddOverlay(_overlay);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlayManager.RemoveOverlay(_overlay);
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        LocalActive = TryComp<SpecialComponent>(_player.LocalEntity, out var special) && IsActive(special);
        if (!LocalActive || special == null)
            return;

        var now = Timing.RealTime;
        if (special.SecondWindActiveUntil != _seenActiveUntil)
        {
            _seenActiveUntil = special.SecondWindActiveUntil;
            _startedAt = now;
            _lastBeatAt = now;
            _nextBeatAt = now;
        }

        Elapsed = (float) (now - _startedAt).TotalSeconds;
        Remaining = MathF.Max(0f, (float) (special.SecondWindActiveUntil - Timing.CurTime).TotalSeconds);

        if (now >= _nextBeatAt && Remaining > 0f)
        {
            _audio.PlayGlobal(HeartbeatSound, Filter.Local(), false);
            _lastBeatAt = now;

            var progress = Math.Clamp(Elapsed / MathF.Max(0.01f, Elapsed + Remaining), 0f, 1f);
            var speed = 1f + (FinalBeatSpeed - 1f) * progress * progress;
            _nextBeatAt = now + TimeSpan.FromSeconds(StartBeatInterval / speed);
        }

        SinceBeat = (float) (now - _lastBeatAt).TotalSeconds;
    }

    private void OnAfterHandleState(Entity<SpecialComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        MovementSpeed.RefreshMovementSpeedModifiers(ent.Owner);
    }
}
