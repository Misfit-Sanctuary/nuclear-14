namespace Content.Shared.EntityTable.ValueSelector;

/// <summary>
/// Misfit added
/// RNG int range
/// </summary>
public sealed partial class IntegerRangeNumberRNGSelector : NumberSelector
{
    [DataField]
    public int Min = 1;
    [DataField]
    public int Max = 1;
    public IntegerRangeNumberRNGSelector(int min, int max)
    {
        Min = min;
        Max = max;
    }

    public override float Get(System.Random rand, IEntityManager entMan, IPrototypeManager proto)
    {
        var rngInt = rand.Next(Min, Max);
        return rngInt;
    }
}
