using Robust.Shared.Random;


namespace Content.Shared.EntityTable.ValueSelector;

/// <summary>
/// Gives a constant value.
/// </summary>
public sealed partial class ConstantNumberSelector : NumberSelector
{
    [DataField]
    public float Value = 1;

    public ConstantNumberSelector(float value)
    {
        Value = value;
    }

    public override float Get(IRobustRandom rand, IEntityManager entMan, IPrototypeManager proto)
    {
        return Value;
    }
}
