// #Misfits Add - Client ambient light modulation: darkens map ambient light
// proportionally to cloud opacity, giving an overcast feel when clouds are heavy.
// Runs after DayNightCycleClientSystem (by declaration order) so the base
// cycle color is already committed before we darken it.
using Content.Shared._NC.Clouds;
using Robust.Shared.Map.Components;

namespace Content.Client._Misfits.Clouds;

/// <summary>
/// Lerps <see cref="MapLightComponent.AmbientLightColor"/> toward
/// <see cref="NCCloudLayerComponent.OvercastAmbientTint"/> based on the current
/// cloud opacity, giving a visually darker overcast feeling during cloud events.
///
/// Both this system and <see cref="DayNightCycleClientSystem"/> run in Update()
/// each frame. DNC computes color from time and writes it; this system reads that
/// color and applies the cloud-darkness blend on top.
/// At worst there is a 1-frame lag, which is imperceptible with 40 s fades.
///
/// Lerp formula per frame:
///   factor   = CurrentOpacity * OvercastAmbientBlend  (clamped 0–1)
///   newColor = Lerp(currentAmbient, OvercastAmbientTint, factor)
/// </summary>
public sealed class MisfitsCloudAmbientSystem : EntitySystem
{
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<NCCloudLayerComponent, MapLightComponent>();
        while (query.MoveNext(out _, out var clouds, out var mapLight))
        {
            // No visible clouds — leave ambient color as-is.
            if (clouds.CurrentOpacity <= 0f)
                continue;

            var factor = MathF.Min(clouds.CurrentOpacity * clouds.OvercastAmbientBlend, 1f);
            var current = mapLight.AmbientLightColor;
            var tint = clouds.OvercastAmbientTint;

            // Manual per-channel lerp (same pattern used by DayNightCycleSystem).
            mapLight.AmbientLightColor = new Color(
                current.R + (tint.R - current.R) * factor,
                current.G + (tint.G - current.G) * factor,
                current.B + (tint.B - current.B) * factor,
                current.A);
        }
    }
}
