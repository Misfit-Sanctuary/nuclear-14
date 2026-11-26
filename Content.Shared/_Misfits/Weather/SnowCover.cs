using System.Numerics;
using Content.Shared.DoAfter;
using Content.Shared.Movement.Systems;
using Robust.Shared.Audio; // #Cythisiax Added - thunder sound on rain
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Random;
using Robust.Shared.Serialization;

namespace Content.Shared.Weather
{
    public sealed partial class WeatherPrototype
    {
        [ViewVariables(VVAccess.ReadWrite), DataField]
        public List<string> AllowedMaps = new();

        [ViewVariables(VVAccess.ReadWrite), DataField]
        public float SnowAccumulation;

        [ViewVariables(VVAccess.ReadWrite), DataField]
        public SnowfallLook? Snowfall;

        // #Cythisiax Added - rain drawn with the rain shader instead of the old sprite
        [ViewVariables(VVAccess.ReadWrite), DataField]
        public RainfallLook? Rainfall;
    }

    // #Cythisiax Added - how a rain weather looks, streaks fall fast and straight where snow drifts and sways
    [DataDefinition]
    public sealed partial class RainfallLook
    {
        [DataField]
        public float Density = 0.4f;

        // how long the streaks are, higher is a harder downpour
        [DataField]
        public float StreakLength = 1f;

        [DataField]
        public float FallSpeed = 9f;

        [DataField]
        public float Wind = 0.6f;

        [DataField]
        public float Gust = 0.6f;

        [DataField]
        public float Haze;

        [DataField]
        public float Splash = 0.5f;

        [DataField]
        public Color RainColor = new(0.72f, 0.79f, 0.88f);

        [DataField]
        public Color HazeColor = new(0.55f, 0.6f, 0.68f);

        // average lightning strikes per minute, 0 is no lightning
        [DataField]
        public float Lightning;

        [DataField]
        public SoundSpecifier? Thunder;
    }

    // #Cythisiax Added - lightning timing worked out from the synced game clock so every player sees the same strikes
    public static class RainLightning
    {
        public const double SlotSeconds = 0.25;
        public const double FlashSeconds = 0.9;

        public static long Slot(TimeSpan time)
        {
            return (long) Math.Floor(time.TotalSeconds / SlotSeconds);
        }

        public static bool Strikes(long slot, float perMinute)
        {
            if (perMinute <= 0f)
                return false;

            var chance = perMinute * SlotSeconds / 60.0;
            return Content.Shared._Misfits.Weather.SnowCoverMath.Hash((int) slot, 9157) < chance;
        }

        public static float Strength(long slot)
        {
            return 0.6f + 0.4f * Content.Shared._Misfits.Weather.SnowCoverMath.Hash((int) slot, 31);
        }

        public static float ThunderDelay(long slot)
        {
            return 0.3f + 2.4f * Content.Shared._Misfits.Weather.SnowCoverMath.Hash((int) slot, 77);
        }

        // how bright the sky is from every strike in the last second, a quick double flicker then a fade
        public static float Flash(TimeSpan time, float perMinute)
        {
            if (perMinute <= 0f)
                return 0f;

            var now = Slot(time);
            var back = (long) Math.Ceiling(FlashSeconds / SlotSeconds);
            var total = 0f;

            for (var slot = now - back; slot <= now; slot++)
            {
                if (!Strikes(slot, perMinute))
                    continue;

                var age = (float) (time.TotalSeconds - slot * SlotSeconds);
                if (age < 0f || age > FlashSeconds)
                    continue;

                float shape;
                if (age < 0.07f)
                    shape = 1f;
                else if (age < 0.13f)
                    shape = 0.25f;
                else if (age < 0.22f)
                    shape = 0.85f;
                else
                    shape = 0.85f * MathF.Exp(-(age - 0.22f) * 6f);

                total = MathF.Max(total, shape * Strength(slot));
            }

            return total;
        }
    }

    [DataDefinition]
    public sealed partial class SnowfallLook
    {
        [DataField]
        public float Density = 0.4f;

        [DataField]
        public float FlakeSize = 1f;

        [DataField]
        public float FallSpeed = 1.5f;

        [DataField]
        public float Wind = 0.3f;

        [DataField]
        public float Gust = 0.5f;

        [DataField]
        public float Sway = 1f;

        [DataField]
        public float Haze;

        [DataField]
        public float Landing = 0.5f;

        // #Cythisiax Added - flake and haze colours so other snow types like radioactive snow can tint the fall
        [DataField]
        public Color FlakeColor = new(0.95f, 0.97f, 1f);

        [DataField]
        public Color HazeColor = new(0.84f, 0.87f, 0.92f);

        // #Cythisiax Added - makes the flakes pulse a little brighter, used for radioactive snow
        [DataField]
        public float Glow;
    }
}

namespace Content.Shared._Misfits.Weather
{
    [RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
    public sealed partial class SnowCoverComponent : Component
    {
        [DataField, AutoNetworkedField]
        public float Depth;

        [DataField]
        public float MeltRate = 1f / 1200f;

        [DataField, AutoNetworkedField]
        public float FillRate;

        [ViewVariables]
        public float SentDepth;
    }

    [RegisterComponent]
    public sealed partial class SnowTrampleComponent : Component
    {
        [ViewVariables]
        public Dictionary<Vector2i, SnowTrampleTile> Tiles = new();

        [ViewVariables]
        public int Version;

        [ViewVariables]
        public float FillCarry;
    }

    public sealed class SnowTrampleTile
    {
        public readonly byte[] Cells = new byte[SnowCoverMath.Cells * SnowCoverMath.Cells];
        public int Version;
    }

    [Serializable, NetSerializable]
    public struct SnowFootprint
    {
        public Vector2 Position;
        public float Radius;
        public byte Strength;

        public SnowFootprint(Vector2 position, float radius, byte strength)
        {
            Position = position;
            Radius = radius;
            Strength = strength;
        }
    }

    [Serializable, NetSerializable]
    public sealed class SnowWalkerPrints
    {
        public NetEntity Walker;
        public List<SnowFootprint> Prints = new();
    }

    [Serializable, NetSerializable]
    public sealed class SnowFootprintsEvent : EntityEventArgs
    {
        public NetEntity Grid;
        public List<SnowWalkerPrints> Walkers = new();
    }

    [Serializable, NetSerializable]
    public sealed class SnowTrampleSnapshotEvent : EntityEventArgs
    {
        public NetEntity Grid;
        public Dictionary<Vector2i, byte[]> Tiles = new();
    }

    [Serializable, NetSerializable]
    public struct SnowHeat
    {
        public Vector2 Position;
        public float Radius;

        public SnowHeat(Vector2 position, float radius)
        {
            Position = position;
            Radius = radius;
        }
    }

    [Serializable, NetSerializable]
    public sealed class SnowMeltEvent : EntityEventArgs
    {
        public NetEntity Grid;
        public float Elapsed;
        public List<SnowHeat> Heats = new();
    }

    public sealed class SnowWalkerState
    {
        public EntityUid Grid;
        public Vector2 LastPosition;
        public float Carry;
        public bool LeftFoot;
        public float Snowy;
    }

    [RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
    public sealed partial class SnowSlowedComponent : Component
    {
        [DataField, AutoNetworkedField]
        public float Modifier = 1f;
    }

    public sealed partial class SharedSnowSlowedSystem : EntitySystem
    {
        [Dependency] private MovementSpeedModifierSystem _speed = default!;

        public override void Initialize()
        {
            base.Initialize();
            SubscribeLocalEvent<SnowSlowedComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshSpeed);
            SubscribeLocalEvent<SnowSlowedComponent, AfterAutoHandleStateEvent>(OnState);
            SubscribeLocalEvent<SnowSlowedComponent, ComponentShutdown>(OnShutdown);
        }

        private void OnRefreshSpeed(Entity<SnowSlowedComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
        {
            if (ent.Comp.Modifier < 1f)
                args.ModifySpeed(ent.Comp.Modifier);
        }

        private void OnState(Entity<SnowSlowedComponent> ent, ref AfterAutoHandleStateEvent args)
        {
            _speed.RefreshMovementSpeedModifiers(ent);
        }

        private void OnShutdown(Entity<SnowSlowedComponent> ent, ref ComponentShutdown args)
        {
            ent.Comp.Modifier = 1f;
            if (!TerminatingOrDeleted(ent))
                _speed.RefreshMovementSpeedModifiers(ent);
        }

        public void SetModifier(Entity<SnowSlowedComponent> ent, float modifier)
        {
            if (MathF.Abs(ent.Comp.Modifier - modifier) < 0.01f)
                return;

            ent.Comp.Modifier = modifier;
            Dirty(ent);
            _speed.RefreshMovementSpeedModifiers(ent);
        }
    }

    public static class SnowCoverMath
    {
        public const int Cells = 16;

        private const float StepLength = 0.32f;
        private const float FootSpread = 0.09f;

        private const float FeetOffset = -0.3f;

        public static float TileCoverage(Vector2i indices, float depth)
        {
            var noise = Noise(indices.X / 5f, indices.Y / 5f) * 0.75f + Hash(indices.X, indices.Y) * 0.25f;
            return Math.Clamp((depth - noise * 0.75f) / 0.25f, 0f, 1f);
        }

        public static void Walk(SnowWalkerState state, EntityUid grid, Vector2 position, IRobustRandom random, List<SnowFootprint> output)
        {
            var feet = position + new Vector2(0f, FeetOffset);

            if (state.Grid != grid)
            {
                state.Grid = grid;
                state.LastPosition = feet;
                state.Carry = 0f;
                return;
            }

            var delta = feet - state.LastPosition;
            var distance = delta.Length();

            if (distance > 3f)
            {
                state.LastPosition = feet;
                state.Carry = 0f;
                return;
            }

            if (distance < 0.001f)
                return;

            var dir = delta / distance;
            var side = new Vector2(-dir.Y, dir.X);
            var travel = StepLength - state.Carry;

            while (travel <= distance)
            {
                var foot = state.LeftFoot ? 1f : -1f;
                var spot = state.LastPosition + dir * travel
                    + side * (FootSpread * foot + random.NextFloat(-0.02f, 0.02f))
                    + dir * random.NextFloat(-0.03f, 0.03f);

                var radius = random.NextFloat(0.07f, 0.11f);
                var strength = (byte) random.Next(45, 85);
                output.Add(new SnowFootprint(spot, radius, strength));

                state.LeftFoot = !state.LeftFoot;
                travel += StepLength;
            }

            state.Carry = distance - (travel - StepLength);
            state.LastPosition = feet;
        }

        private const float ChurnRadiusMin = 0.14f;
        private const float ChurnRadiusMax = 0.36f;
        private const float WearSampleRadius = 0.3f;

        public static void Stamp(SnowTrampleComponent trample, SnowFootprint print)
        {
            var wear = Math.Clamp(SampleCells(trample, print.Position, WearSampleRadius) * 1.6f, 0f, 1f);

            StampCircle(trample, print.Position, print.Radius, print.Strength);

            if (wear > 0.05f)
            {
                var churnRadius = ChurnRadiusMin + (ChurnRadiusMax - ChurnRadiusMin) * wear;
                StampCircle(trample, print.Position, churnRadius, 8f + 32f * wear);
            }

            trample.Version++;
        }

        private static void StampCircle(SnowTrampleComponent trample, Vector2 position, float radius, float strength)
        {
            var reach = radius + 1f / Cells;
            var minX = (int) MathF.Floor((position.X - reach) * Cells);
            var maxX = (int) MathF.Floor((position.X + reach) * Cells);
            var minY = (int) MathF.Floor((position.Y - reach) * Cells);
            var maxY = (int) MathF.Floor((position.Y + reach) * Cells);

            for (var cy = minY; cy <= maxY; cy++)
            {
                for (var cx = minX; cx <= maxX; cx++)
                {
                    var centre = new Vector2((cx + 0.5f) / Cells, (cy + 0.5f) / Cells);
                    var falloff = 1f - (centre - position).Length() / radius;
                    if (falloff <= 0f)
                        continue;

                    var add = (int) (strength * MathF.Sqrt(falloff));
                    if (add <= 0)
                        continue;

                    var tileIndices = new Vector2i(FloorDiv(cx, Cells), FloorDiv(cy, Cells));
                    if (!trample.Tiles.TryGetValue(tileIndices, out var tile))
                    {
                        tile = new SnowTrampleTile();
                        trample.Tiles[tileIndices] = tile;
                    }

                    var index = (cy - tileIndices.Y * Cells) * Cells + (cx - tileIndices.X * Cells);
                    tile.Cells[index] = (byte) Math.Min(255, tile.Cells[index] + add);
                    tile.Version++;
                }
            }
        }

        public const float BigHeatRadius = 1.7f;
        public const float SmallHeatRadius = 0.6f;
        private const float MeltSpeed = 0.18f;

        public static void Melt(SnowTrampleComponent trample, SnowHeat heat, float elapsed)
        {
            var step = (int) (255f * MeltSpeed * elapsed);
            if (step <= 0)
                return;

            var reach = heat.Radius * 1.25f + 1f / Cells;
            var minX = (int) MathF.Floor((heat.Position.X - reach) * Cells);
            var maxX = (int) MathF.Floor((heat.Position.X + reach) * Cells);
            var minY = (int) MathF.Floor((heat.Position.Y - reach) * Cells);
            var maxY = (int) MathF.Floor((heat.Position.Y + reach) * Cells);
            var changed = false;

            for (var cy = minY; cy <= maxY; cy++)
            {
                for (var cx = minX; cx <= maxX; cx++)
                {
                    var centre = new Vector2((cx + 0.5f) / Cells, (cy + 0.5f) / Cells);
                    var edge = heat.Radius * (0.8f + 0.45f * Noise(cx / 7f, cy / 7f));
                    var falloff = 1f - (centre - heat.Position).Length() / edge;
                    if (falloff <= 0f)
                        continue;

                    var target = (int) (255f * MathF.Min(1f, falloff * 2.2f));
                    var tileIndices = new Vector2i(FloorDiv(cx, Cells), FloorDiv(cy, Cells));
                    if (!trample.Tiles.TryGetValue(tileIndices, out var tile))
                    {
                        tile = new SnowTrampleTile();
                        trample.Tiles[tileIndices] = tile;
                    }

                    var index = (cy - tileIndices.Y * Cells) * Cells + (cx - tileIndices.X * Cells);
                    var current = tile.Cells[index];
                    if (current >= target)
                        continue;

                    tile.Cells[index] = (byte) Math.Min(target, current + step);
                    tile.Version++;
                    changed = true;
                }
            }

            if (changed)
                trample.Version++;
        }

        public static bool NearHeat(List<SnowHeat> heats, Vector2 position, float scale)
        {
            foreach (var heat in heats)
            {
                var reach = heat.Radius * scale;
                if (Vector2.DistanceSquared(heat.Position, position) <= reach * reach)
                    return true;
            }

            return false;
        }

        public static float Sample(SnowTrampleComponent trample, Vector2 position, float radius)
        {
            return SampleCells(trample, position + new Vector2(0f, FeetOffset), radius);
        }

        private static float SampleCells(SnowTrampleComponent trample, Vector2 point, float radius)
        {
            var total = 0f;
            var count = 0;
            var minX = (int) MathF.Floor((point.X - radius) * Cells);
            var maxX = (int) MathF.Floor((point.X + radius) * Cells);
            var minY = (int) MathF.Floor((point.Y - radius) * Cells);
            var maxY = (int) MathF.Floor((point.Y + radius) * Cells);

            for (var cy = minY; cy <= maxY; cy++)
            {
                for (var cx = minX; cx <= maxX; cx++)
                {
                    count++;
                    var tileIndices = new Vector2i(FloorDiv(cx, Cells), FloorDiv(cy, Cells));
                    if (!trample.Tiles.TryGetValue(tileIndices, out var tile))
                        continue;

                    total += tile.Cells[(cy - tileIndices.Y * Cells) * Cells + (cx - tileIndices.X * Cells)] / 255f;
                }
            }

            return count == 0 ? 0f : total / count;
        }

        public static bool FillPaths(SnowTrampleComponent trample, float amount)
        {
            if (trample.Tiles.Count == 0 || amount <= 0f)
                return false;

            trample.FillCarry += amount * 255f;
            var take = (int) trample.FillCarry;
            if (take <= 0)
                return false;

            trample.FillCarry -= take;

            foreach (var indices in new List<Vector2i>(trample.Tiles.Keys))
            {
                var tile = trample.Tiles[indices];
                var any = false;
                for (var i = 0; i < tile.Cells.Length; i++)
                {
                    var value = tile.Cells[i] - take;
                    tile.Cells[i] = (byte) Math.Max(0, value);
                    any |= value > 0;
                }

                if (any)
                    tile.Version++;
                else
                    trample.Tiles.Remove(indices);
            }

            trample.Version++;
            return true;
        }

        public static int FloorDiv(int value, int size)
        {
            return value >= 0 ? value / size : (value - size + 1) / size;
        }

        public static float Hash(int x, int y)
        {
            unchecked
            {
                var h = (uint) (x * 374761393 + y * 668265263);
                h = (h ^ (h >> 13)) * 1274126177;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777216f;
            }
        }

        public static float Noise(float x, float y)
        {
            var ix = (int) MathF.Floor(x);
            var iy = (int) MathF.Floor(y);
            var fx = x - ix;
            var fy = y - iy;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);

            var a = Hash(ix, iy);
            var b = Hash(ix + 1, iy);
            var c = Hash(ix, iy + 1);
            var d = Hash(ix + 1, iy + 1);

            var top = a + (b - a) * fx;
            var bottom = c + (d - c) * fx;
            return top + (bottom - top) * fy;
        }
    }

    [RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
    public sealed partial class SnowballComponent : Component
    {
        [DataField, AutoNetworkedField]
        public float Size = 1f;

        [DataField]
        public bool Giant;

        [DataField]
        public bool Snowman;

        [ViewVariables]
        public EntityUid Grid;

        [ViewVariables]
        public Vector2 LastPosition;

        [ViewVariables]
        public float Carry;

        [ViewVariables]
        public float Trail;
    }

    [Serializable, NetSerializable]
    public struct SnowTrailDot
    {
        public Vector2 Position;
        public float Radius;
        public float Strength;
        public float Life;

        public SnowTrailDot(Vector2 position, float radius, float strength = 1f, float life = 90f)
        {
            Position = position;
            Radius = radius;
            Strength = strength;
            Life = life;
        }
    }

    [Serializable, NetSerializable]
    public sealed class SnowTrailEvent : EntityEventArgs
    {
        public NetEntity Grid;
        public List<SnowTrailDot> Dots = new();
    }

    [RegisterComponent]
    public sealed partial class SnowTrailComponent : Component
    {
        [ViewVariables]
        public List<(SnowTrailDot Dot, TimeSpan Time)> Marks = new();
    }

    [Serializable, NetSerializable]
    public sealed partial class SnowballMakeDoAfterEvent : SimpleDoAfterEvent
    {
        [DataField]
        public NetCoordinates Coordinates;

        private SnowballMakeDoAfterEvent()
        {
        }

        public SnowballMakeDoAfterEvent(NetCoordinates coordinates)
        {
            Coordinates = coordinates;
        }
    }

    [Serializable, NetSerializable]
    public sealed partial class SnowballPackDoAfterEvent : SimpleDoAfterEvent
    {
        [DataField]
        public NetCoordinates Coordinates;

        private SnowballPackDoAfterEvent()
        {
        }

        public SnowballPackDoAfterEvent(NetCoordinates coordinates)
        {
            Coordinates = coordinates;
        }
    }

    [Serializable, NetSerializable]
    public sealed partial class SnowmanBuildDoAfterEvent : SimpleDoAfterEvent
    {
    }

    [Serializable, NetSerializable]
    public sealed class SnowballHitEvent : EntityEventArgs
    {
        public NetEntity Target;
        public float Amount;
    }

    public static class SnowballMath
    {
        public const int MaxPacks = 4;
        public const float GiantStart = 1f;
        public const float GiantMax = 2.5f;
        public const float GiantRadius = 0.3f;
        public const float SnowmanMin = 1.6f;
        public const float SnowmanScale = 31f / 28f;

        public static float ItemScale(float size)
        {
            return 1f + 0.15f * (size - 1f);
        }

        public static float GiantScale(float size)
        {
            return 0.33f * size;
        }
    }
}
