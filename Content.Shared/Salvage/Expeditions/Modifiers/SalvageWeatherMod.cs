using Content.Shared.Weather;
using Robust.Shared.Prototypes;

namespace Content.Shared.Salvage.Expeditions.Modifiers;

[Prototype("salvageWeatherMod")]
public sealed partial class SalvageWeatherMod : IPrototype, IBiomeSpecificMod
{
    [IdDataField] public string ID { get; set; } = default!;

    [DataField("desc")] public string Description { get; private set; } = string.Empty;

    /// <inheritdoc/>
    [DataField("cost")]
    public float Cost { get; private set; } = 0f;

    /// <inheritdoc/>
    [DataField("biomes")]
    public List<string>? Biomes { get; private set; } = null;

    /// <summary>
    /// Weather prototype to use on the planet.
    /// </summary>
    [DataField("weather", required: true)]
    public ProtoId<WeatherPrototype> WeatherPrototype = string.Empty;
}
