using Content.Shared.Containers.ItemSlots;
using Robust.Shared.Prototypes;

namespace Content.Shared._RMC14.Mortar;

/// A shell with no payload of its own that mixes two beakers and lands them in a puff.

[RegisterComponent]
[Access(typeof(SharedMortarSystem))]
public sealed partial class MortarChemicalShellComponent : Component
{
    public const string BeakerSlotAId = "BeakerSlotA";
    public const string BeakerSlotBId = "BeakerSlotB";

    [DataField(required: true)]
    public ItemSlot BeakerSlotA = new();

    [DataField(required: true)]
    public ItemSlot BeakerSlotB = new();

    /// Smoke entity the mixed solution is released through.

    [DataField]
    public EntProtoId SmokePrototype = "Smoke";

    /// How long the impact puff lasts. It never spreads.

    [DataField]
    public float PuffDuration = 2f;
}
