using System.Numerics;

namespace Content.Shared._Misfits.Clothing;

// #Misfits Add - nudges a slot's clothing sprites toward the head, separately for each facing direction.
[RegisterComponent]
public sealed partial class MisfitsClothingDirectionOffsetComponent : Component
{
    /// <summary>
    /// Slot name to the offset added to that slot's clothing layers for each direction, in tiles.
    /// </summary>
    [DataField]
    public Dictionary<string, Dictionary<Direction, Vector2>> Slots = new();
}
