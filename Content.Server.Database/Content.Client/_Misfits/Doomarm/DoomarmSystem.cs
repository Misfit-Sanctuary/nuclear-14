// #Misfits Add - Client side of the Doomarm: the Groundbreaker hop animation.

using System.Numerics;
using Content.Shared._Misfits.Doomarm;
using Robust.Client.Animations;
using Robust.Client.GameObjects;
using Robust.Shared.Animations;

namespace Content.Client._Misfits.Doomarm;

public sealed partial class DoomarmSystem : SharedDoomarmSystem
{
    [Dependency] private AnimationPlayerSystem _anim = default!;

    private const string HopKey = "doomarm-hop";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<DoomarmHopEvent>(OnHop);
    }

    private void OnHop(DoomarmHopEvent ev)
    {
        var uid = GetEntity(ev.User);
        if (!Exists(uid) || _anim.HasRunningAnimation(uid, HopKey))
            return;

        // Same idea as the jump emote, just higher: up, then slam back down faster than it went up.
        var animation = new Animation
        {
            Length = TimeSpan.FromSeconds(ev.Duration),
            AnimationTracks =
            {
                new AnimationTrackComponentProperty
                {
                    ComponentType = typeof(SpriteComponent),
                    Property = nameof(SpriteComponent.Offset),
                    InterpolationMode = AnimationInterpolationMode.Cubic,
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(Vector2.Zero, 0f),
                        new AnimationTrackProperty.KeyFrame(new Vector2(0f, 0.75f), ev.Duration * 0.6f),
                        new AnimationTrackProperty.KeyFrame(Vector2.Zero, ev.Duration * 0.4f),
                    },
                },
            },
        };

        _anim.Play(uid, animation, HopKey);
    }
}
