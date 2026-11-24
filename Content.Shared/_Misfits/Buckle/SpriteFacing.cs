namespace Content.Shared._Misfits.Buckle;

public static class SpriteFacing
{
    public static double DirectionBias = -0.05;

    public static Direction GetCardinalDir(Angle rotation)
    {
        var angle = rotation.Reduced().FlipPositive();

        var mod = Math.Floor(angle.Theta / MathHelper.PiOver2) % 2 - 0.5;
        var modTheta = angle.Theta + mod * DirectionBias;

        return ((int) Math.Round(modTheta / MathHelper.PiOver2) % 4) switch
        {
            0 => Direction.South,
            1 => Direction.East,
            2 => Direction.North,
            _ => Direction.West,
        };
    }
}
