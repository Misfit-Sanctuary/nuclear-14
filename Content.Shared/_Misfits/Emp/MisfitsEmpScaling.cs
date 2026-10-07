namespace Content.Shared._Misfits.Emp;

/// <summary>
/// Shared balance values for converting an EMP's battery-drain energy into synthetic Shock damage.
/// The standard hand pulse grenade defines full strength; stronger sources cannot exceed it.
/// </summary>
public static class MisfitsEmpScaling
{
    public const float FullPulseEnergy = 2_700_000f;

    public static float GetStrength(float energyConsumption)
        => Math.Clamp(energyConsumption / FullPulseEnergy, 0f, 1f);
}
