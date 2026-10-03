using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;

namespace Content.Client._Misfits.SpecialStats;

public sealed class SpecialSecondWindOverlay : Overlay
{
    [Dependency] private readonly IEntityManager _entityManager = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;
    public override bool RequestScreenTexture => true;

    private readonly SpecialSecondWindSystem _system;
    private readonly ShaderInstance _shader;

    private float _intensity;
    private float _impact;

    private const float FadeInSeconds = 0.25f;
    private const float FadeOutSeconds = 1.5f;
    private const float ImpactSeconds = 0.9f;

    public SpecialSecondWindOverlay(SpecialSecondWindSystem system)
    {
        IoCManager.InjectDependencies(this);
        _system = system;
        _shader = _prototypeManager.Index<ShaderPrototype>("SecondWindBlood").InstanceUnique();
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        if (!_system.LocalActive)
            return false;

        if (!_entityManager.TryGetComponent(_playerManager.LocalEntity, out EyeComponent? eye) || args.Viewport.Eye != eye.Eye)
            return false;

        var fadeIn = Math.Clamp(_system.Elapsed / FadeInSeconds, 0f, 1f);
        var fadeOut = Math.Clamp(_system.Remaining / FadeOutSeconds, 0f, 1f);
        _intensity = MathF.Min(fadeIn, fadeOut);

        var impactLeft = Math.Clamp(1f - _system.Elapsed / ImpactSeconds, 0f, 1f);
        _impact = impactLeft * impactLeft;

        return _intensity > 0f;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (ScreenTexture == null)
            return;

        var handle = args.WorldHandle;
        handle.SetTransform(Matrix3x2.Identity);

        _shader.SetParameter("SCREEN_TEXTURE", ScreenTexture);
        _shader.SetParameter("intensity", _intensity);
        _shader.SetParameter("impact", _impact);
        _shader.SetParameter("elapsed", _system.Elapsed);
        _shader.SetParameter("sinceBeat", _system.SinceBeat);
        handle.UseShader(_shader);
        handle.DrawRect(args.WorldBounds, Color.White);
        handle.UseShader(null);
    }
}
