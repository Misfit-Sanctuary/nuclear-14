using System.Numerics;
using Content.Shared.Movement.Systems;
using Robust.Shared.GameStates;
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

    public sealed class SnowWalkerState
    {
        public EntityUid Grid;
        public Vector2 LastPosition;
        public float Carry;
        public bool LeftFoot;
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
}
