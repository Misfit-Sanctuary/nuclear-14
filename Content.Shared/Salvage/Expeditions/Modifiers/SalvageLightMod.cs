using Robust.Shared.Prototypes;

namespace Content.Shared.Salvage.Expeditions.Modifiers;

[Prototype("salvageLightMod")]
public sealed partial class SalvageLightMod : IPrototype, IBiomeSpecificMod
{
    [IdDataField] public string ID { get; set; } = default!;

    [DataField("desc")] public string Description { get; private set; } = string.Empty;

    /// <inheritdoc/>
    [DataField("cost")]
    public float Cost { get; private set; } = 0f;

    /// <inheritdoc/>
    [DataField("biomes")]
    public List<string>? Biomes { get; private set; } = null;

    [DataField("color", required: true)] public Color? Color;
}
